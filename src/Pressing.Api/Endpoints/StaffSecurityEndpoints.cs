using Pressing.Core.Dtos;
using Pressing.Core.Security;
using Pressing.Core.Services;

namespace Pressing.Api.Endpoints;

/// <summary>
/// Sécurité des comptes du personnel : second temps de la connexion (TOTP), activation / désactivation de la double
/// authentification depuis « Mon compte », déverrouillage et réinitialisation 2FA par un administrateur.
/// </summary>
public static class StaffSecurityEndpoints
{
    public static IEndpointRouteBuilder MapStaffSecurityEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth/staff").WithTags("Authentification");

        // Anonyme : le jeton « mfa-pending » ne passe volontairement pas par l'authentification JWT (TokenRevalidation le
        // refuse partout ailleurs) ; il est lu et contrôlé ici.
        auth.MapPost("/login/mfa", async (
            StaffMfaRequest request,
            StaffAuthService service,
            HttpContext context,
            CancellationToken ct) =>
        {
            string? header = context.Request.Headers.Authorization.ToString();
            string? pending = header is not null && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? header["Bearer ".Length..].Trim()
                : null;
            try
            {
                return Results.Ok(await service.LoginMfaAsync(pending, request.Code, context.Connection.RemoteIpAddress?.ToString(), ct));
            }
            catch (StaffAccountLockedException ex)
            {
                return Locked(ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status401Unauthorized);
            }
        }).RequireRateLimiting("auth");

        var twoFactor = auth.MapGroup("/2fa").RequireAuthorization("StaffOnly");

        twoFactor.MapGet("/", async (CurrentUser current, TwoFactorService service, CancellationToken ct) =>
            current.StaffId.HasValue
                ? Results.Ok(await service.GetStatusAsync(current.StaffId.Value, ct))
                : Results.Unauthorized());

        twoFactor.MapPost("/setup", async (CurrentUser current, TwoFactorService service, CancellationToken ct) =>
            current.StaffId.HasValue
                ? Results.Ok(await service.BeginSetupAsync(current.StaffId.Value, ct))
                : Results.Unauthorized());

        twoFactor.MapPost("/enable", async (
            TwoFactorCodeRequest request,
            CurrentUser current,
            TwoFactorService service,
            HttpContext context,
            CancellationToken ct) =>
            current.StaffId.HasValue
                ? Results.Ok(await service.EnableAsync(current.StaffId.Value, request.Code, context.Connection.RemoteIpAddress?.ToString(), ct))
                : Results.Unauthorized()).RequireRateLimiting("auth");

        twoFactor.MapPost("/disable", async (
            DisableTwoFactorRequest request,
            CurrentUser current,
            TwoFactorService service,
            HttpContext context,
            CancellationToken ct) =>
            current.StaffId.HasValue
                ? Results.Ok(await service.DisableAsync(current.StaffId.Value, request, context.Connection.RemoteIpAddress?.ToString(), ct))
                : Results.Unauthorized()).RequireRateLimiting("auth");

        var staff = app.MapGroup("/api/staff").WithTags("Personnel").RequireAuthorization("AdminOnly");

        staff.MapPost("/{id:int}/unlock", async (
            int id,
            CurrentUser current,
            StaffService service,
            HttpContext context,
            CancellationToken ct) =>
            Results.Ok(await service.UnlockAsync(id, current, context.Connection.RemoteIpAddress?.ToString(), ct)));

        staff.MapPost("/{id:int}/two-factor/reset", async (
            int id,
            CurrentUser current,
            TwoFactorService service,
            HttpContext context,
            CancellationToken ct) =>
            Results.Ok(await service.ResetAsync(id, current, context.Connection.RemoteIpAddress?.ToString(), ct)));

        return app;
    }

    /// <summary>423 Locked : le message indique l'heure de déverrouillage.</summary>
    internal static IResult Locked(StaffAccountLockedException ex) =>
        Results.Json(new { message = ex.Message, lockedUntil = ex.Until }, statusCode: StatusCodes.Status423Locked);
}
