using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Pressing.Client;
using Pressing.Web.Infrastructure;

Pressing.Core.EnvFile.Load();

var builder = WebApplication.CreateBuilder(args);

var apiUrl = Environment.GetEnvironmentVariable("PRESSING_PUBLIC_API_URL");
if (string.IsNullOrWhiteSpace(apiUrl)) apiUrl = builder.Configuration["Api:BaseUrl"];
if (string.IsNullOrWhiteSpace(apiUrl)) apiUrl = "http://localhost:5180";
apiUrl = apiUrl.TrimEnd('/');

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    options.Filters.Add<ApiExceptionFilter>();
});

builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<PressingApiClient>(c =>
{
    c.BaseAddress = new Uri(apiUrl + "/");
    c.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped<PortalApi>();
builder.Services.AddScoped<ShopDirectory>();

builder.Services.AddAntiforgery(o =>
{
    o.Cookie.Name = "pressing.af";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
});

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "pressing.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/compte/connexion";
        options.LogoutPath = "/compte/deconnexion";
        options.AccessDeniedPath = "/compte/connexion";
        options.ReturnUrlParameter = "returnUrl";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.Events = new CookieAuthenticationEvents
        {
            // Un jeton API échu rend le cookie caduc, même si le cookie lui-même est encore valide.
            OnValidatePrincipal = async ctx =>
            {
                var raw = ctx.Principal?.FindFirst(AuthClaims.TokenExpires)?.Value;
                if (raw is null || !DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var expires) || expires.ToUniversalTime() <= DateTime.UtcNow)
                {
                    ctx.RejectPrincipal();
                    await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                }
            },
            OnRedirectToLogin = ctx =>
            {
                var uri = ctx.RedirectUri;
                // Cookie présent mais refusé : on l'explique sur la page de connexion.
                if (ctx.Request.Cookies.ContainsKey(ctx.Options.Cookie.Name!))
                    uri += (uri.Contains('?') ? "&" : "?") + "expiree=1";
                ctx.Response.Redirect(uri);
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

builder.Services.Configure<RouteOptions>(o => o.LowercaseUrls = true);

var app = builder.Build();

var fr = new CultureInfo("fr-FR");
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(fr),
    SupportedCultures = [fr],
    SupportedUICultures = [fr],
    RequestCultureProviders = []
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/erreur/500");
    app.UseHsts();
}

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    h["Content-Security-Policy"] =
        "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; script-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    await next();
});

// UseStaticFiles plutôt que MapStaticAssets : les variantes pré-compressées de MapStaticAssets
// sont servies vides lorsque le chemin du projet contient un « # » (cas de « C#\PressingApp »).
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Les URL versionnées (?v=hash, asp-append-version) peuvent être mises en cache longtemps.
        if (ctx.Context.Request.Query.ContainsKey("v"))
            ctx.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
    }
});

app.UseStatusCodePagesWithReExecute("/erreur/{0}");

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
