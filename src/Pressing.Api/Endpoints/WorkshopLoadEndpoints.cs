using Pressing.Core.Dtos;
using Pressing.Core.Security;
using Pressing.Core.Services;

namespace Pressing.Api.Endpoints;

/// <summary>Charge prévisionnelle de l'atelier : charge par jour, date de retrait conseillée, capacité journalière.</summary>
public static class WorkshopLoadEndpoints
{
    public static IEndpointRouteBuilder MapWorkshopLoadEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/workshop").WithTags("Atelier");

        group.MapGet("/load", async (
            int? days,
            WorkshopLoadService load,
            CancellationToken ct) =>
            Results.Ok(await load.GetLoadAsync(days ?? WorkshopLoadService.DefaultDays, null, ct)))
            .RequireAuthorization("StaffOnly");

        group.MapPost("/suggest-due-date", async (
            WorkshopSuggestDueDateRequest request,
            WorkshopLoadService load,
            CancellationToken ct) =>
            Results.Ok(await load.SuggestDueDateAsync(request.Items, request.IsExpress, null, ct)))
            .RequireAuthorization("StaffOnly");

        group.MapPut("/capacity", async (
            WorkshopCapacityDto request,
            CurrentUser current,
            WorkshopLoadService load,
            CancellationToken ct) =>
            Results.Ok(new WorkshopCapacityDto(await load.SetCapacityAsync(request.DailyCapacity, current, ct))))
            .RequireAuthorization("ManagerOrAdmin");

        return app;
    }
}
