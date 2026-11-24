namespace Pressing.Core.Dtos;

public record CreateOrderItemRequest(
    int CategoryId,
    ServiceType ServiceType,
    int Quantity,
    decimal? UnitPrice,
    bool IsExpress,
    DateTime? DueDate,
    string? Description,
    string? Notes);

public record CreateOrderRequest(
    int ClientId,
    IReadOnlyList<CreateOrderItemRequest> Items,
    bool IsExpress,
    DateTime? DueDate,
    DeliveryType DeliveryType,
    string? DeliveryAddress,
    decimal DeliveryFee,
    decimal DiscountAmount,
    string? PromotionCode,
    int LoyaltyPointsToRedeem,
    string? Notes,
    decimal DepositAmount,
    PaymentMethod DepositMethod,
    string? DepositReference);

public record OrderQuoteRequest(
    IReadOnlyList<CreateOrderItemRequest> Items,
    bool IsExpress,
    DeliveryType DeliveryType,
    decimal DeliveryFee,
    decimal DiscountAmount,
    string? PromotionCode,
    int LoyaltyPointsToRedeem,
    int? ClientId);

public record OrderQuoteDto(
    decimal Subtotal,
    decimal ExpressTotal,
    decimal DeliveryFee,
    decimal DiscountAmount,
    decimal PromotionDiscount,
    decimal LoyaltyDiscount,
    decimal Total,
    int LoyaltyPointsUsed,
    string? PromotionMessage,
    bool PromotionValid);

public record OrderItemDto(
    int Id,
    int CategoryId,
    string CategoryName,
    string CategoryIcon,
    ServiceType ServiceType,
    string ServiceLabel,
    string? Description,
    int Quantity,
    decimal UnitPrice,
    decimal ExpressSupplement,
    bool IsExpress,
    ItemStatus Status,
    string StatusLabel,
    DateTime? DueDate,
    int PickedUpQuantity,
    int RemainingQuantity,
    decimal LineTotal,
    string? Notes);

public record PaymentDto(
    int Id,
    int OrderId,
    string TicketNumber,
    decimal Amount,
    PaymentType Type,
    string TypeLabel,
    PaymentMethod Method,
    string MethodLabel,
    DateTime PaidAt,
    string? Reference,
    string? Note,
    string? StaffName);

public record PickupItemDto(int OrderItemId, string CategoryName, string ServiceLabel, int Quantity);

public record PickupDto(
    int Id,
    DateTime PickedUpAt,
    string? StaffName,
    string? Note,
    IReadOnlyList<PickupItemDto> Items);

public record OrderDto(
    int Id,
    string TicketNumber,
    int ClientId,
    string ClientName,
    string ClientPhone,
    string? ClientEmail,
    string? ClientAddress,
    DateTime CreatedAt,
    DateTime DueDate,
    OrderStatus Status,
    string StatusLabel,
    DeliveryType DeliveryType,
    string DeliveryTypeLabel,
    string? DeliveryAddress,
    decimal DeliveryFee,
    decimal Subtotal,
    decimal ExpressTotal,
    decimal DiscountAmount,
    decimal PromotionDiscount,
    string? PromotionCode,
    int LoyaltyPointsRedeemed,
    decimal LoyaltyDiscount,
    decimal Total,
    decimal PaidAmount,
    decimal Balance,
    bool IsExpress,
    string? Notes,
    string? CreatedByStaffName,
    DateTime? ReadyNotifiedAt,
    DateTime? CompletedAt,
    DateTime? CancelledAt,
    string? CancelReason,
    DateTime UpdatedAt,
    IReadOnlyList<OrderItemDto> Items,
    IReadOnlyList<PaymentDto> Payments,
    IReadOnlyList<PickupDto> Pickups);

public record OrderSummaryDto(
    int Id,
    string TicketNumber,
    int ClientId,
    string ClientName,
    string ClientPhone,
    string? ClientEmail,
    DateTime CreatedAt,
    DateTime DueDate,
    OrderStatus Status,
    string StatusLabel,
    decimal Total,
    decimal PaidAmount,
    decimal Balance,
    bool IsExpress,
    int ItemCount,
    int ReadyCount,
    int PickedUpCount,
    int RemainingItems,
    DeliveryType DeliveryType,
    string DeliveryTypeLabel);

public record AddPaymentRequest(
    decimal Amount,
    PaymentMethod Method,
    string? Reference,
    string? Note,
    DateTime? PaidAt);

public record RefundRequest(
    decimal Amount,
    PaymentMethod Method,
    string? Note);

public record PickupItemRequest(int OrderItemId, int Quantity);

public record PickupRequest(
    IReadOnlyList<PickupItemRequest> Items,
    decimal PaymentAmount,
    PaymentMethod PaymentMethod,
    string? Reference,
    string? Note);

public record UpdateItemStatusRequest(ItemStatus Status);

public record CancelOrderRequest(string Reason);

public record TrackingItemDto(string CategoryName, string ServiceLabel, int Quantity, string StatusLabel);

public record TrackingDto(
    string TicketNumber,
    string Status,
    string StatusLabel,
    DateTime CreatedAt,
    DateTime DueDate,
    bool IsExpress,
    string ShopName,
    string ShopPhone,
    string? ShopAddress,
    IReadOnlyList<TrackingItemDto> Items);
