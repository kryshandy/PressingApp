using Pressing.Core.Dtos;
using Pressing.Core.Security;
using Pressing.Core.Services;

namespace Pressing.Api.Endpoints;

/// <summary>Clôture de caisse journalière (« Z de caisse »).</summary>
public static class CashClosingEndpoints
{
    public static IEndpointRouteBuilder MapCashClosingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/cash-closings").WithTags("Clôture de caisse");

        group.MapGet("/preview", async (CashClosingService service, DateTime? date, CancellationToken ct) =>
            Results.Ok(await service.PreviewAsync(date ?? DateTime.Today, ct)))
            .RequireAuthorization("StaffOnly");

        group.MapPost("/", async (CashClosingRequest request, CurrentUser current, CashClosingService service, CancellationToken ct) =>
        {
            CashClosingDto created = await service.CreateAsync(request, current, ct);
            return Results.Created($"/api/cash-closings/{created.Id}", created);
        }).RequireAuthorization("StaffOnly");

        group.MapPut("/{id:int}", async (int id, CashClosingRequest request, CurrentUser current, CashClosingService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateAsync(id, request, current, ct)))
            .RequireAuthorization("StaffOnly");

        group.MapPost("/{id:int}/validate", async (int id, CurrentUser current, CashClosingService service, CancellationToken ct) =>
            Results.Ok(await service.ValidateAsync(id, current, ct)))
            .RequireAuthorization("ManagerOrAdmin");

        group.MapGet("/", async (CashClosingService service, DateTime? from, DateTime? to, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(from, to, ct)))
            .RequireAuthorization("ManagerOrAdmin");

        group.MapGet("/{id:int}", async (int id, CashClosingService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(id, ct)))
            .RequireAuthorization("StaffOnly");

        group.MapGet("/{id:int}/pdf", async (int id, CashClosingService service, CancellationToken ct) =>
        {
            (byte[] bytes, string fileName) = await service.GetPdfAsync(id, ct);
            return Results.File(bytes, "application/pdf", fileName);
        }).RequireAuthorization("StaffOnly");

        return app;
    }
}
