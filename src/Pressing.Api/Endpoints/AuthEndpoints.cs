using Pressing.Core.Dtos;
using Pressing.Core.Security;
using Pressing.Core.Services;

namespace Pressing.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Authentification");

        group.MapPost("/staff/login", async (
            StaffLoginRequest request,
            StaffAuthService auth,
            HttpContext context,
            CancellationToken ct) =>
        {
            try
            {
                AuthResponse response = await auth.LoginAsync(
                    request,
                    context.Connection.RemoteIpAddress?.ToString(),
                    ct);
                return Results.Ok(response);
            }
            catch (StaffAccountLockedException ex)
            {
                return StaffSecurityEndpoints.Locked(ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status401Unauthorized);
            }
        }).RequireRateLimiting("auth");

        group.MapPost("/client/login", async (
            ClientLoginRequest request,
            CustomerAuthService auth,
            HttpContext context,
            CancellationToken ct) =>
        {
            try
            {
                AuthResponse response = await auth.LoginAsync(
                    request,
                    context.Connection.RemoteIpAddress?.ToString(),
                    ct);
                return Results.Ok(response);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status401Unauthorized);
            }
        }).RequireRateLimiting("auth");

        group.MapPost("/client/forgot", async (
            ForgotPasswordRequest request,
            CustomerAuthService auth,
            HttpContext context,
            CancellationToken ct) =>
        {
            MessageResponse response = await auth.ForgotPasswordAsync(
                request.Identifier,
                context.Connection.RemoteIpAddress?.ToString(),
                ct);
            return Results.Ok(response);
        }).RequireRateLimiting("auth");

        group.MapPost("/client/reset", async (
            ResetPasswordRequest request,
            CustomerAuthService auth,
            HttpContext context,
            CancellationToken ct) =>
        {
            MessageResponse response = await auth.ResetPasswordAsync(
                request.Token,
                request.NewPassword,
                context.Connection.RemoteIpAddress?.ToString(),
                ct);
            return Results.Ok(response);
        }).RequireRateLimiting("auth");

        group.MapGet("/me", async (
            CurrentUser current,
            StaffAuthService staffAuth,
            CustomerAuthService customerAuth,
            CancellationToken ct) =>
        {
            if (current.StaffId.HasValue)
            {
                return Results.Ok(new { type = "staff", staff = await staffAuth.GetAsync(current.StaffId.Value, ct) });
            }

            if (current.ClientId.HasValue)
            {
                return Results.Ok(new { type = "client", client = await customerAuth.GetAsync(current.ClientId.Value, ct) });
            }

            return Results.Unauthorized();
        }).RequireAuthorization();

        group.MapPost("/staff/change-password", async (
            ChangeStaffPasswordRequest request,
            CurrentUser current,
            StaffAuthService auth,
            HttpContext context,
            CancellationToken ct) =>
        {
            if (!current.StaffId.HasValue) return Results.Unauthorized();
            MessageResponse response = await auth.ChangePasswordAsync(
                current.StaffId.Value,
                request,
                context.Connection.RemoteIpAddress?.ToString(),
                ct);
            return Results.Ok(response);
        }).RequireAuthorization("StaffOnly");

        group.MapPost("/client/change-password", async (
            ClientChangePasswordRequest request,
            CurrentUser current,
            CustomerAuthService auth,
            HttpContext context,
            CancellationToken ct) =>
        {
            if (!current.ClientId.HasValue) return Results.Unauthorized();
            MessageResponse response = await auth.ChangePasswordAsync(
                current.ClientId.Value,
                request,
                context.Connection.RemoteIpAddress?.ToString(),
                ct);
            return Results.Ok(response);
        }).RequireAuthorization("ClientOnly");

        return app;
    }
}
