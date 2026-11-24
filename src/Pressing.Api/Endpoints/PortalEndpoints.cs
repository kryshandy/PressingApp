using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Core.Reports;
using Pressing.Core.Security;
using Pressing.Core.Services;

namespace Pressing.Api.Endpoints;

public static class PortalEndpoints
{
    public static IEndpointRouteBuilder MapPortalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portal").WithTags("Espace client").RequireAuthorization("ClientOnly");

        group.MapGet("/dashboard", async (
            CurrentUser current,
            ClientService clients,
            OrderService orders,
            CancellationToken ct) =>
        {
            if (!current.ClientId.HasValue) return Results.Unauthorized();
            int clientId = current.ClientId.Value;

            ClientDto? client = await clients.GetAsync(clientId, ct);
            if (client is null) return Results.NotFound(new { message = "Client introuvable." });

            IReadOnlyList<OrderSummaryDto> all = await orders.GetForClientAsync(clientId, 50, ct);
            List<OrderSummaryDto> active = all
                .Where(o => o.Status != OrderStatus.Completed && o.Status != OrderStatus.Cancelled)
                .ToList();
            IReadOnlyList<LoyaltyTransactionDto> loyalty = await clients.GetLoyaltyAsync(clientId, ct);

            int readyItems = active.Sum(o => Math.Max(0, o.ReadyCount - o.PickedUpCount));
            decimal balance = all
                .Where(o => o.Status != OrderStatus.Cancelled && o.Balance > 0)
                .Sum(o => o.Balance);

            return Results.Ok(new PortalDashboardDto(
                client,
                active,
                all.Take(10).ToList(),
                loyalty.Take(10).ToList(),
                readyItems,
                balance));
        });

        group.MapGet("/orders", async (CurrentUser current, OrderService orders, CancellationToken ct) =>
        {
            if (!current.ClientId.HasValue) return Results.Unauthorized();
            return Results.Ok(await orders.GetForClientAsync(current.ClientId.Value, 50, ct));
        });

        group.MapGet("/orders/{id:int}", async (int id, CurrentUser current, OrderService orders, CancellationToken ct) =>
        {
            if (!current.ClientId.HasValue) return Results.Unauthorized();
            OrderDto? order = await orders.GetForClientOrderAsync(current.ClientId.Value, id, ct);
            return order is null
                ? Results.NotFound(new { message = "Commande introuvable." })
                : Results.Ok(order);
        });

        group.MapGet("/orders/{id:int}/receipt", async (
            int id,
            CurrentUser current,
            OrderService orders,
            ReceiptService receipts,
            CancellationToken ct) =>
        {
            if (!current.ClientId.HasValue) return Results.Unauthorized();
            OrderDto? owned = await orders.GetForClientOrderAsync(current.ClientId.Value, id, ct);
            if (owned is null) return Results.NotFound(new { message = "Commande introuvable." });
            Order order = await orders.LoadFullAsync(id, ct);
            byte[] bytes = await receipts.GeneratePdfAsync(order, ct);
            return Results.File(bytes, "application/pdf", $"recu-{order.TicketNumber}.pdf");
        });

        group.MapGet("/profile", async (CurrentUser current, ClientService clients, CancellationToken ct) =>
        {
            if (!current.ClientId.HasValue) return Results.Unauthorized();
            ClientDto? client = await clients.GetAsync(current.ClientId.Value, ct);
            return client is null
                ? Results.NotFound(new { message = "Client introuvable." })
                : Results.Ok(client);
        });

        group.MapPut("/profile", async (
            UpdateProfileRequest request,
            CurrentUser current,
            ClientService clients,
            CancellationToken ct) =>
        {
            if (!current.ClientId.HasValue) return Results.Unauthorized();
            return Results.Ok(await clients.UpdateProfileAsync(current.ClientId.Value, request, current, ct));
        });

        group.MapGet("/loyalty", async (CurrentUser current, ClientService clients, CancellationToken ct) =>
        {
            if (!current.ClientId.HasValue) return Results.Unauthorized();
            return Results.Ok(await clients.GetLoyaltyAsync(current.ClientId.Value, ct));
        });

        return app;
    }
}
