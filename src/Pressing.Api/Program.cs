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

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});
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
