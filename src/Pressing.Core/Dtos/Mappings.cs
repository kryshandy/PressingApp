using Pressing.Core.Entities;

namespace Pressing.Core.Dtos;

public static class Mappings
{
    public static ClientDto ToDto(
        this Client client,
        int orderCount = 0,
        decimal balance = 0,
        decimal totalSpent = 0,
        DateTime? lastOrderAt = null) => new(
        client.Id,
        client.FirstName,
        client.LastName,
        client.FullName,
        client.Phone,
        client.WhatsApp,
        client.Email,
        client.Address,
        client.Notes,
        client.IsActive,
        client.WebAccessEnabled,
        client.LoyaltyPoints,
        client.CreatedAt,
        client.UpdatedAt,
        orderCount,
        balance,
        totalSpent,
        lastOrderAt,
        client.NotificationPreference,
        client.ConsentGivenAt,
        client.ConsentRevokedAt);

    public static OrderSummaryDto ToSummary(this Order order)
    {
        int itemCount = order.Items.Sum(i => i.Quantity);
        int readyCount = order.Items.Where(i => i.Status is ItemStatus.Ready or ItemStatus.PickedUp).Sum(i => i.Quantity);
        int pickedUpCount = order.Items.Sum(i => i.PickedUpQuantity);
        int remaining = order.Items.Where(i => i.Status != ItemStatus.Cancelled).Sum(i => i.RemainingQuantity);

        return new OrderSummaryDto(
            order.Id,
            order.TicketNumber,
            order.ClientId,
            order.Client?.FullName ?? string.Empty,
            order.Client?.Phone ?? string.Empty,
            order.Client?.Email,
            order.CreatedAt,
            order.DueDate,
            order.Status,
            order.Status.Label(),
            order.Total,
            order.PaidAmount,
            order.Balance,
            order.IsExpress,
            itemCount,
            readyCount,
            pickedUpCount,
            remaining,
            order.DeliveryType,
            order.DeliveryType.Label());
    }

    public static OrderItemDto ToDto(this OrderItem item) => new(
        item.Id,
        item.CategoryId,
        item.Category?.Name ?? string.Empty,
        item.Category?.Icon ?? string.Empty,
        item.ServiceType,
        item.ServiceType.Label(),
        item.Description,
        item.Quantity,
        item.UnitPrice,
        item.ExpressSupplement,
        item.IsExpress,
        item.Status,
        item.Status.Label(),
        item.DueDate,
        item.PickedUpQuantity,
        item.RemainingQuantity,
        item.LineTotal,
        item.Notes);

    public static PaymentDto ToDto(this Payment payment) => new(
        payment.Id,
        payment.OrderId,
        payment.Order?.TicketNumber ?? string.Empty,
        payment.Amount,
        payment.Type,
        payment.Type.Label(),
        payment.Method,
        payment.Method.Label(),
        payment.PaidAt,
        payment.Reference,
        payment.Note,
        payment.StaffUser?.FullName);

    public static PickupDto ToDto(this Pickup pickup) => new(
        pickup.Id,
        pickup.PickedUpAt,
        pickup.StaffUser?.FullName,
        pickup.Note,
        pickup.Items.Select(i => new PickupItemDto(
            i.OrderItemId,
            i.OrderItem?.Category?.Name ?? string.Empty,
            i.OrderItem?.ServiceType.Label() ?? string.Empty,
            i.Quantity)).ToList());

    public static OrderDto ToDto(this Order order) => new(
        order.Id,
        order.TicketNumber,
        order.ClientId,
        order.Client?.FullName ?? string.Empty,
        order.Client?.Phone ?? string.Empty,
        order.Client?.Email,
        order.Client?.Address,
        order.CreatedAt,
        order.DueDate,
        order.Status,
        order.Status.Label(),
        order.DeliveryType,
        order.DeliveryType.Label(),
        order.DeliveryAddress,
        order.DeliveryFee,
        order.Subtotal,
        order.ExpressTotal,
        order.DiscountAmount,
        order.PromotionDiscount,
        order.PromotionCode,
        order.LoyaltyPointsRedeemed,
        order.LoyaltyDiscount,
        order.Total,
        order.PaidAmount,
        order.Balance,
        order.IsExpress,
        order.Notes,
        order.CreatedByStaff?.FullName,
        order.ReadyNotifiedAt,
        order.CompletedAt,
        order.CancelledAt,
        order.CancelReason,
        order.UpdatedAt,
        order.Items.Select(ToDto).ToList(),
        order.Payments.Select(ToDto).ToList(),
        order.Pickups.Select(ToDto).ToList());

    public static CategoryDto ToDto(this Category category) => new(
        category.Id,
        category.Name,
        category.Icon,
        category.SortOrder,
        category.IsActive,
        category.Prices
            .OrderBy(p => p.ServiceType)
            .Select(p => new CategoryPriceDto(
                p.Id, p.ServiceType, p.ServiceType.Label(), p.Price, p.ExpressSupplement, p.EstimatedDays, p.IsActive))
            .ToList());

    public static PromotionDto ToDto(this Promotion promotion) => new(
        promotion.Id,
        promotion.Code,
        promotion.Description,
        promotion.Type,
        promotion.Type.Label(),
        promotion.Value,
        promotion.MinOrderAmount,
        promotion.ValidFrom,
        promotion.ValidTo,
        promotion.MaxUses,
        promotion.UsedCount,
        promotion.IsActive);

    public static LoyaltyTransactionDto ToDto(this LoyaltyTransaction transaction) => new(
        transaction.Id,
        transaction.Points,
        transaction.Type,
        transaction.Type.Label(),
        transaction.Note,
        transaction.CreatedAt,
        transaction.OrderId,
        transaction.Order?.TicketNumber);

    public static StaffUserDto ToDto(this StaffUser staff) => new(
        staff.Id,
        staff.FullName,
        staff.Username,
        staff.Email,
        staff.Role,
        staff.Role.Label(),
        staff.IsActive,
        staff.CreatedAt,
        staff.LastLoginAt);
}
