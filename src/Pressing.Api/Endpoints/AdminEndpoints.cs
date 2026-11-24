using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Core.Email;
using Pressing.Core.Security;
using Pressing.Core.Services;

namespace Pressing.Api.Endpoints;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var staff = app.MapGroup("/api/staff").WithTags("Personnel").RequireAuthorization("AdminOnly");

        staff.MapGet("/", async (StaffService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(ct)));

        staff.MapPost("/", async (
            CreateStaffRequest request,
            CurrentUser current,
            StaffService service,
            CancellationToken ct) =>
        {
            StaffUserDto dto = await service.CreateAsync(request, current, ct);
            return Results.Created($"/api/staff/{dto.Id}", dto);
        });

        staff.MapPut("/{id:int}", async (
            int id,
            UpdateStaffRequest request,
            CurrentUser current,
            StaffService service,
            CancellationToken ct) =>
            Results.Ok(await service.UpdateAsync(id, request, current, ct)));

        staff.MapPost("/{id:int}/password", async (
            int id,
            StaffPasswordRequest request,
            CurrentUser current,
            StaffService service,
            CancellationToken ct) =>
            Results.Ok(await service.SetPasswordAsync(id, request.Password, current, ct)));

        var settings = app.MapGroup("/api/settings").WithTags("Paramètres");

        settings.MapGet("/", async (SettingsService service, CancellationToken ct) =>
            Results.Ok(await service.GetDtoAsync(ct)))
            .RequireAuthorization("ManagerOrAdmin");

        settings.MapPut("/", async (
            UpdateSettingsRequest request,
            CurrentUser current,
            SettingsService service,
            CancellationToken ct) =>
            Results.Ok(await service.UpdateAsync(request, current, ct)))
            .RequireAuthorization("AdminOnly");

        settings.MapPost("/backup", async (CurrentUser current, SettingsService service, CancellationToken ct) =>
            Results.Ok(await service.CreateBackupAsync(current, ct)))
            .RequireAuthorization("AdminOnly");

        settings.MapGet("/backups", async (SettingsService service, CancellationToken ct) =>
            Results.Ok(await service.GetBackupsAsync(ct)))
            .RequireAuthorization("AdminOnly");

        var emails = app.MapGroup("/api/emails").WithTags("E-mails").RequireAuthorization("ManagerOrAdmin");

        emails.MapGet("/", async (
            EmailService service,
            int? page,
            int? pageSize,
            EmailStatus? status,
            CancellationToken ct) =>
            Results.Ok(await service.GetOutboxAsync(page ?? 1, pageSize ?? 30, status, ct)));

        emails.MapPost("/test", async (TestEmailRequest request, EmailService service, CancellationToken ct) =>
        {
            EmailMessageDto? dto = await service.SendTestAsync(request.To, ct);
            return dto is null
                ? Results.BadRequest(new { message = "Impossible d'envoyer l'e-mail de test." })
                : Results.Ok(dto);
        });

        emails.MapPost("/{id:int}/retry", async (int id, EmailService service, CancellationToken ct) =>
        {
            EmailMessageDto? dto = await service.RetryAsync(id, ct);
            return dto is null
                ? Results.NotFound(new { message = "E-mail introuvable." })
                : Results.Ok(dto);
        });

        app.MapGet("/api/audit", async (
            AuditService service,
            int? page,
            int? pageSize,
            string? search,
            CancellationToken ct) =>
            Results.Ok(await service.GetAsync(page ?? 1, pageSize ?? 50, search, ct)))
            .WithTags("Audit")
            .RequireAuthorization("AdminOnly");

        return app;
    }
}
