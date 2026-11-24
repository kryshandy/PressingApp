using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Core.Reports;
using Pressing.Core.Security;
using Pressing.Core.Services;

namespace Pressing.Api.Endpoints;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orders").WithTags("Commandes");

        group.MapGet("/", async (
            OrderService orders,
            OrderStatus? status,
            string? search,
            DateTime? from,
            DateTime? to,
            int? clientId,
            bool? onlyReady,
            bool? onlyOutstanding,
            bool? includeCancelled,
            int? page,
            int? pageSize,
            CancellationToken ct) =>
            Results.Ok(await orders.SearchAsync(
                status, search, from, to, clientId,
                onlyReady ?? false, onlyOutstanding ?? false, includeCancelled ?? true,
                page ?? 1, pageSize ?? 30, ct)))
            .RequireAuthorization("StaffOnly");

        group.MapGet("/ready", async (OrderService orders, int? take, CancellationToken ct) =>
            Results.Ok(await orders.GetReadyAsync(take ?? 30, ct)))
            .RequireAuthorization("StaffOnly");

        group.MapGet("/outstanding", async (OrderService orders, int? take, CancellationToken ct) =>
            Results.Ok(await orders.GetOutstandingAsync(take ?? 30, ct)))
            .RequireAuthorization("StaffOnly");

        group.MapGet("/ticket/{ticket}", async (string ticket, OrderService orders, CancellationToken ct) =>
        {
            PagedResult<OrderSummaryDto> found = await orders.SearchAsync(search: ticket, pageSize: 5, ct: ct);
            OrderSummaryDto? match = found.Items.FirstOrDefault(o =>
                string.Equals(o.TicketNumber, ticket, StringComparison.OrdinalIgnoreCase));
            if (match is null) return Results.NotFound(new { message = "Commande introuvable." });
            OrderDto? order = await orders.GetAsync(match.Id, ct);
            return order is null
                ? Results.NotFound(new { message = "Commande introuvable." })
                : Results.Ok(order);
        }).RequireAuthorization("StaffOnly");

        group.MapGet("/{id:int}", async (int id, OrderService orders, CancellationToken ct) =>
        {
            OrderDto? order = await orders.GetAsync(id, ct);
            return order is null
                ? Results.NotFound(new { message = "Commande introuvable." })
                : Results.Ok(order);
        }).RequireAuthorization("StaffOnly");

        group.MapPost("/quote", async (OrderQuoteRequest request, OrderService orders, CancellationToken ct) =>
            Results.Ok(await orders.QuoteAsync(request, ct)))
            .RequireAuthorization("StaffOnly");

        group.MapPost("/", async (
            CreateOrderRequest request,
            CurrentUser current,
            OrderService orders,
            CancellationToken ct) =>
        {
            OrderDto dto = await orders.CreateAsync(request, current, ct);
            return Results.Created($"/api/orders/{dto.Id}", dto);
        }).RequireAuthorization("StaffOnly");

        group.MapPut("/{id:int}/items/{itemId:int}/status", async (
            int id,
            int itemId,
            UpdateItemStatusRequest request,
            CurrentUser current,
            OrderService orders,
            CancellationToken ct) =>
            Results.Ok(await orders.UpdateItemStatusAsync(id, itemId, request.Status, current, ct)))
            .RequireAuthorization("StaffOnly");

        group.MapPut("/{id:int}/status", async (
            int id,
            UpdateItemStatusRequest request,
            CurrentUser current,
            OrderService orders,
            CancellationToken ct) =>
            Results.Ok(await orders.UpdateAllItemsStatusAsync(id, request.Status, current, ct)))
            .RequireAuthorization("StaffOnly");

        group.MapPost("/{id:int}/payments", async (
            int id,
            AddPaymentRequest request,
            CurrentUser current,
            PaymentService payments,
            CancellationToken ct) =>
            Results.Ok(await payments.AddPaymentAsync(id, request, current, ct)))
            .RequireAuthorization("StaffOnly");

        group.MapPost("/{id:int}/refunds", async (
            int id,
            RefundRequest request,
            CurrentUser current,
            PaymentService payments,
            CancellationToken ct) =>
            Results.Ok(await payments.RefundAsync(id, request.Amount, request.Method, request.Note, current, ct)))
            .RequireAuthorization("ManagerOrAdmin");

        group.MapPost("/{id:int}/pickup", async (
            int id,
            PickupRequest request,
            CurrentUser current,
            OrderService orders,
            CancellationToken ct) =>
            Results.Ok(await orders.PickupAsync(id, request, current, ct)))
            .RequireAuthorization("StaffOnly");

        group.MapPost("/{id:int}/cancel", async (
            int id,
            CancelOrderRequest request,
            CurrentUser current,
            OrderService orders,
            CancellationToken ct) =>
            Results.Ok(await orders.CancelAsync(id, request.Reason, current, ct)))
            .RequireAuthorization("ManagerOrAdmin");

        group.MapGet("/{id:int}/receipt", async (
            int id,
            OrderService orders,
            ReceiptService receipts,
            CancellationToken ct) =>
        {
            Order order = await orders.LoadFullAsync(id, ct);
            byte[] bytes = await receipts.GeneratePdfAsync(order, ct);
            return Results.File(bytes, "application/pdf", $"recu-{order.TicketNumber}.pdf");
        }).RequireAuthorization("StaffOnly");

        group.MapPost("/{id:int}/receipt/email", async (
            int id,
            CurrentUser current,
            OrderService orders,
            CancellationToken ct) =>
            Results.Ok(await orders.SendReceiptAsync(id, current, ct)))
            .RequireAuthorization("StaffOnly");

        app.MapGet("/api/payments", async (
            PaymentService payments,
            DateTime? from,
            DateTime? to,
            PaymentMethod? method,
            string? search,
            int? page,
            int? pageSize,
            CancellationToken ct) =>
            Results.Ok(await payments.GetAsync(from, to, method, search, page ?? 1, pageSize ?? 50, ct)))
            .WithTags("Commandes")
            .RequireAuthorization("StaffOnly");

        return app;
    }
}
