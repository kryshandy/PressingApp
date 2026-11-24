using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Pressing.Core.Entities;

public class Order
{
    public int Id { get; set; }

    [MaxLength(40)]
    public string TicketNumber { get; set; } = string.Empty;

    public int ClientId { get; set; }

    public Client Client { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime DueDate { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Deposited;

    public DeliveryType DeliveryType { get; set; }

    [MaxLength(300)]
    public string? DeliveryAddress { get; set; }

    public decimal DeliveryFee { get; set; }

    public decimal Subtotal { get; set; }

    public decimal ExpressTotal { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal PromotionDiscount { get; set; }

    [MaxLength(60)]
    public string? PromotionCode { get; set; }

    public int LoyaltyPointsRedeemed { get; set; }

    public decimal LoyaltyDiscount { get; set; }

    public decimal Total { get; set; }

    public decimal PaidAmount { get; set; }

    [MaxLength(1200)]
    public string? Notes { get; set; }

    public bool IsExpress { get; set; }

    public int? CreatedByStaffId { get; set; }

    public StaffUser? CreatedByStaff { get; set; }

    public DateTime? ReadyNotifiedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime? CancelledAt { get; set; }

    [MaxLength(500)]
    public string? CancelReason { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();

    public ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public ICollection<Pickup> Pickups { get; set; } = new List<Pickup>();

    [NotMapped]
    public decimal Balance => Total - PaidAmount;

    [NotMapped]
    public bool IsFullyPaid => Balance <= 0.005m;
}

public class OrderItem
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public Order Order { get; set; } = null!;

    public int CategoryId { get; set; }

    public Category Category { get; set; } = null!;

    public ServiceType ServiceType { get; set; }

    [MaxLength(300)]
    public string? Description { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal ExpressSupplement { get; set; }

    public bool IsExpress { get; set; }

    public ItemStatus Status { get; set; } = ItemStatus.Deposited;

    public DateTime? DueDate { get; set; }

    public int PickedUpQuantity { get; set; }

    public DateTime? PickedUpAt { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    [NotMapped]
    public decimal LineTotal => (UnitPrice + (IsExpress ? ExpressSupplement : 0m)) * Quantity;

    [NotMapped]
    public int RemainingQuantity => Quantity - PickedUpQuantity;
}

public class Payment
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public Order Order { get; set; } = null!;

    public int ClientId { get; set; }

    public Client Client { get; set; } = null!;

    public decimal Amount { get; set; }

    public PaymentType Type { get; set; } = PaymentType.Payment;

    public PaymentMethod Method { get; set; }

    public DateTime PaidAt { get; set; } = DateTime.Now;

    [MaxLength(120)]
    public string? Reference { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    public int? StaffUserId { get; set; }

    public StaffUser? StaffUser { get; set; }
}

public class Pickup
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public Order Order { get; set; } = null!;

    public int ClientId { get; set; }

    public Client Client { get; set; } = null!;

    public DateTime PickedUpAt { get; set; } = DateTime.Now;

    public int? StaffUserId { get; set; }

    public StaffUser? StaffUser { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    public ICollection<PickupItem> Items { get; set; } = new List<PickupItem>();
}

public class PickupItem
{
    public int Id { get; set; }

    public int PickupId { get; set; }

    public Pickup Pickup { get; set; } = null!;

    public int OrderItemId { get; set; }

    public OrderItem OrderItem { get; set; } = null!;

    public int Quantity { get; set; }
}
