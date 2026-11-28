using Pressing.Core.Security;
using Pressing.Core.Services;

namespace Pressing.Api.Endpoints;

/// <summary>Rappels clients : journal d'envoi et relance manuelle (responsable ou administrateur).</summary>
public static class RemindersEndpoints
{
    public static IEndpointRouteBuilder MapRemindersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reminders").WithTags("Rappels").RequireAuthorization("ManagerOrAdmin");

        group.MapGet("/", async (
            ReminderService service,
            int? page,
            int? pageSize,
            string? type,
            string? channel,
            DateTime? from,
            DateTime? to,
            CancellationToken ct) =>
            Results.Ok(await service.GetLogsAsync(page ?? 1, pageSize ?? 30, type, channel, from, to, ct)));

        group.MapPost("/orders/{orderId:int}/send", async (
            int orderId,
            ReminderService service,
            CurrentUser user,
            CancellationToken ct) =>
            Results.Ok(await service.SendManualAsync(orderId, user, ct)));

        return app;
    }
}
