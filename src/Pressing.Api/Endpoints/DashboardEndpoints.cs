using Pressing.Core.Services;

namespace Pressing.Api.Endpoints;

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dashboard", async (DashboardService dashboard, CancellationToken ct) =>
            Results.Ok(await dashboard.GetAsync(ct)))
            .WithTags("Tableau de bord")
            .RequireAuthorization("StaffOnly");

        var notifications = app.MapGroup("/api/notifications").WithTags("Notifications").RequireAuthorization("StaffOnly");

        notifications.MapGet("/", async (NotificationService service, int? take, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(take ?? 20, ct)));

        notifications.MapGet("/unread-count", async (NotificationService service, CancellationToken ct) =>
            Results.Ok(new { count = await service.GetUnreadCountAsync(ct) }));

        notifications.MapPost("/{id:int}/read", async (int id, NotificationService service, CancellationToken ct) =>
        {
            await service.MarkReadAsync(id, ct);
            return Results.NoContent();
        });

        notifications.MapPost("/read-all", async (NotificationService service, CancellationToken ct) =>
        {
            await service.MarkAllReadAsync(ct);
            return Results.NoContent();
        });

        var reports = app.MapGroup("/api/reports").WithTags("Rapports").RequireAuthorization("ManagerOrAdmin");

        reports.MapGet("/", async (ReportService service, DateTime? from, DateTime? to, CancellationToken ct) =>
        {
            (DateTime start, DateTime end) = Range(from, to);
            return Results.Ok(await service.GetAsync(start, end, ct));
        });

        reports.MapGet("/export", async (ReportService service, DateTime? from, DateTime? to, CancellationToken ct) =>
        {
            (DateTime start, DateTime end) = Range(from, to);
            byte[] bytes = await service.ExportCsvAsync(start, end, ct);
            return Results.File(bytes, "text/csv; charset=utf-8", $"rapport-{start:yyyyMMdd}-{end:yyyyMMdd}.csv");
        });

        return app;
    }

    private static (DateTime From, DateTime To) Range(DateTime? from, DateTime? to)
    {
        DateTime end = to ?? DateTime.Today;
        DateTime start = from ?? end.AddDays(-29);
        return (start, end);
    }
}
