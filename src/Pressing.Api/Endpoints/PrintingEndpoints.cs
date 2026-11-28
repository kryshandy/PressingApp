using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Core.Printing;
using Pressing.Core.Services;

namespace Pressing.Api.Endpoints;

/// <summary>Ticket thermique 80 mm : octets ESC/POS d'une commande et réglages d'imprimante.</summary>
public static class PrintingEndpoints
{
    /// <summary>Enregistre les services d'impression thermique.</summary>
    public static IServiceCollection AddPrintingServices(this IServiceCollection services)
    {
        services.AddScoped<ThermalTicketService>();
        return services;
    }

    public static IEndpointRouteBuilder MapPrintingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/orders/{id:int}/receipt/escpos", async (
            int id,
            OrderService orders,
            ThermalTicketService tickets,
            CancellationToken ct) =>
        {
            Order order = await orders.LoadFullAsync(id, ct);
            byte[] bytes = await tickets.BuildAsync(order, ct);
            return Results.File(bytes, "application/octet-stream", $"ticket-{order.TicketNumber}.bin");
        }).WithTags("Impression").RequireAuthorization("StaffOnly");

        app.MapGet("/api/printing/ticket-printer", async (ThermalTicketService tickets, CancellationToken ct) =>
            Results.Ok(await tickets.GetPrinterSettingsAsync(ct)))
            .WithTags("Impression").RequireAuthorization("StaffOnly");

        return app;
    }
}
