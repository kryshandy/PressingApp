using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Core.Labels;
using Pressing.Core.Security;
using Pressing.Core.Services;

namespace Pressing.Api.Endpoints;

/// <summary>Atelier : étiquettes QR par article et scan pour faire avancer le statut d'un article.</summary>
public static class WorkshopEndpoints
{
    /// <summary>Enregistre les services de l'atelier (étiquettes et scan).</summary>
    public static IServiceCollection AddWorkshopServices(this IServiceCollection services)
    {
        services.AddScoped<LabelService>();
        services.AddScoped<WorkshopService>();
        return services;
    }

    public static IEndpointRouteBuilder MapWorkshopEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/orders/{id:int}/labels", async (
            int id,
            OrderService orders,
            LabelService labels,
            CancellationToken ct) =>
        {
            Order order = await orders.LoadFullAsync(id, ct);
            byte[] bytes = labels.GeneratePdf(order);
            return Results.File(bytes, "application/pdf", $"etiquettes-{order.TicketNumber}.pdf");
        }).WithTags("Atelier").RequireAuthorization("StaffOnly");

        var group = app.MapGroup("/api/workshop").WithTags("Atelier");

        group.MapPost("/scan", async (
            WorkshopScanRequest request,
            CurrentUser current,
            WorkshopService workshop,
            CancellationToken ct) =>
            Results.Ok(await workshop.ScanAsync(request.Code, current, ct)))
            .RequireAuthorization("StaffOnly");

        group.MapPost("/undo", async (
            WorkshopUndoRequest request,
            CurrentUser current,
            WorkshopService workshop,
            CancellationToken ct) =>
            Results.Ok(await workshop.UndoAsync(request.Code, request.FromStatus, current, ct)))
            .RequireAuthorization("StaffOnly");

        return app;
    }
}
