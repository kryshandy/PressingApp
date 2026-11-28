using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Pressing.Api.Endpoints;
using Pressing.Api.Infrastructure;
using Pressing.Core;
using Pressing.Core.Data;
using Pressing.Core.Email;
using Pressing.Core.Options;
using Pressing.Core.Reports;
using Pressing.Core.Security;
using Pressing.Core.Services;

EnvFile.Load();

var builder = WebApplication.CreateBuilder(args);

// PRESSING_API_URL (ex. http://0.0.0.0:5180) : adresse d'écoute. L'option --urls de la ligne de commande
// et les variables ASPNETCORE_URLS / DOTNET_URLS restent prioritaires.
string? configuredApiUrl = Environment.GetEnvironmentVariable("PRESSING_API_URL");
bool urlsFromCommandLine = args.Any(a => a.StartsWith("--urls", StringComparison.OrdinalIgnoreCase));
bool urlsFromEnvironment =
    !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")) ||
    !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOTNET_URLS"));
if (!string.IsNullOrWhiteSpace(configuredApiUrl) && !urlsFromCommandLine && !urlsFromEnvironment)
{
    builder.WebHost.UseUrls(configuredApiUrl.Trim());
}

PressingDatabaseOptions databaseOptions = PressingDatabase.ResolveOptions(builder.Environment.ContentRootPath);
string? configuredDatabase = builder.Configuration["Database:Path"];
if (!string.IsNullOrWhiteSpace(configuredDatabase))
{
    databaseOptions.Path = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, configuredDatabase));
    databaseOptions.BackupPath = Path.Combine(Path.GetDirectoryName(databaseOptions.Path) ?? builder.Environment.ContentRootPath, "Backups");
}

PressingDatabase.EnsureDirectory(databaseOptions.Path);
string connectionString = PressingDatabase.BuildConnectionString(databaseOptions.Path);

EmailOptions emailOptions = BuildEmailOptions(builder.Configuration);
TokenOptions tokenOptions = new()
{
    Key = JwtKeyProvider.GetOrCreate(databaseOptions.Path),
    Issuer = "pressing-eclat",
    Audience = "pressing-app",
    StaffHours = 12,
    ClientDays = 7
};

builder.Services.AddSingleton(databaseOptions);
builder.Services.AddSingleton(emailOptions);
builder.Services.AddSingleton(tokenOptions);
builder.Services.AddSingleton<TokenService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>(sp =>
{
    IHttpContextAccessor accessor = sp.GetRequiredService<IHttpContextAccessor>();
    return CurrentUserFactory.FromPrincipal(accessor.HttpContext?.User);
});

builder.Services.AddDbContext<PressingDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddScoped<DatabaseInitializer>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<ClientService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<PromotionService>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<ReceiptService>();
builder.Services.AddScoped<EmailTemplateService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<StaffAuthService>();
builder.Services.AddScoped<CustomerAuthService>();
builder.Services.AddScoped<StaffService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddAssistant();

if (emailOptions.EffectiveProvider == "resend")
{
    builder.Services.AddHttpClient<IEmailTransport, ResendEmailTransport>();
}
else if (emailOptions.EffectiveProvider == "smtp")
{
    builder.Services.AddSingleton<IEmailTransport, SmtpEmailTransport>();
}
else
{
    builder.Services.AddSingleton<IEmailTransport, SimulatedEmailTransport>();
}

if (emailOptions.BackgroundEnabled)
{
    builder.Services.AddHostedService<EmailBackgroundService>();
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = TokenRevalidation.ValidateAsync
        };
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = tokenOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = tokenOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(tokenOptions.Key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("StaffOnly", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("token_type", "staff"));
    options.AddPolicy("AdminOnly", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("token_type", "staff")
        .RequireRole("Admin"));
    options.AddPolicy("ManagerOrAdmin", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("token_type", "staff")
        .RequireRole("Admin", "Manager"));
    options.AddPolicy("ClientOnly", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("token_type", "client"));
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 12,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("tracking", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 30,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// IP réelle derrière le site web / un reverse proxy : boucle locale (par défaut) + PRESSING_TRUSTED_PROXIES
// (adresses IP séparées par des virgules). Les autres sources ne peuvent pas imposer leur X-Forwarded-For.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Un seul hop accepté : un client ne peut pas fabriquer une chaîne de proxys pour changer son IP aux yeux
    // de la limitation de débit. X-Real-IP n'est pas lu par l'application.
    options.ForwardLimit = 1;
    // Le site web n'envoie que X-Forwarded-For ; ne pas imposer autant d'entrées X-Forwarded-Proto.
    options.RequireHeaderSymmetry = false;
    foreach (string raw in (Environment.GetEnvironmentVariable("PRESSING_TRUSTED_PROXIES") ?? string.Empty)
                 .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (IPAddress.TryParse(raw, out IPAddress? proxy))
        {
            options.KnownProxies.Add(proxy);
        }
    }
});

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var app = builder.Build();

// Doit rester avant UseRateLimiter : la limitation de débit est partitionnée par l'IP déjà corrigée.
app.UseForwardedHeaders();
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapOpenApi();
app.MapGet("/", () => Results.Ok(new
{
    name = "Pressing Éclat API",
    version = "2.0.0",
    documentation = "/openapi/v1.json",
    health = "/api/health"
}));

app.MapPublicEndpoints();
app.MapAuthEndpoints();
app.MapClientEndpoints();
app.MapCatalogEndpoints();
app.MapOrderEndpoints();
app.MapPortalEndpoints();
app.MapDashboardEndpoints();
app.MapAdminEndpoints();
app.MapAssistantEndpoints();

await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initializer.InitializeAsync();
}

app.Run();

static EmailOptions BuildEmailOptions(IConfiguration configuration)
{
    static string Read(IConfiguration configuration, string key, string env, string fallback = "")
    {
        string? fromEnv = Environment.GetEnvironmentVariable(env);
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;
        string? fromConfig = configuration[key];
        return string.IsNullOrWhiteSpace(fromConfig) ? fallback : fromConfig;
    }

    static bool ReadBool(IConfiguration configuration, string key, string env, bool fallback)
    {
        string value = Read(configuration, key, env, fallback.ToString());
        return bool.TryParse(value, out bool parsed) ? parsed : fallback;
    }

    return new EmailOptions
    {
        Provider = Read(configuration, "Email:Provider", "EMAIL_PROVIDER", "auto"),
        ResendApiKey = Read(configuration, "Email:ResendApiKey", "RESEND_API_KEY"),
        ResendFromEmail = Read(configuration, "Email:ResendFromEmail", "RESEND_FROM_EMAIL", "onboarding@resend.dev"),
        ResendFromName = Read(configuration, "Email:ResendFromName", "RESEND_FROM_NAME", "Pressing Éclat"),
        SmtpHost = Read(configuration, "Email:SmtpHost", "SMTP_HOST"),
        SmtpPort = int.TryParse(Read(configuration, "Email:SmtpPort", "SMTP_PORT", "587"), out int port) ? port : 587,
        SmtpUseSsl = ReadBool(configuration, "Email:SmtpUseSsl", "SMTP_USE_SSL", true),
        SmtpUser = Read(configuration, "Email:SmtpUser", "SMTP_USER"),
        SmtpAppPassword = Read(configuration, "Email:SmtpAppPassword", "SMTP_APP_PASSWORD"),
        ReplyTo = Read(configuration, "Email:ReplyTo", "EMAIL_REPLY_TO"),
        AppBaseUrl = Read(configuration, "App:WebUrl", "PRESSING_WEB_URL", "http://localhost:5181").TrimEnd('/'),
        BackgroundEnabled = ReadBool(configuration, "Email:BackgroundEnabled", "EMAIL_BACKGROUND_ENABLED", true)
    };
}

public partial class Program;
