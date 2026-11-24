using System.ComponentModel.DataAnnotations;

namespace Pressing.Core.Entities;

public class Promotion
{
    public int Id { get; set; }

    [MaxLength(40)]
    public string Code { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Description { get; set; } = string.Empty;

    public PromotionType Type { get; set; }

    public decimal Value { get; set; }

    public decimal MinOrderAmount { get; set; }

    public DateTime ValidFrom { get; set; } = DateTime.Now;

    public DateTime ValidTo { get; set; } = DateTime.Now.AddYears(1);

    public int MaxUses { get; set; }

    public int UsedCount { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class LoyaltyTransaction
{
    public int Id { get; set; }

    public int ClientId { get; set; }

    public Client Client { get; set; } = null!;

    public int? OrderId { get; set; }

    public Order? Order { get; set; }

    public int Points { get; set; }

    public LoyaltyTransactionType Type { get; set; }

    [MaxLength(300)]
    public string Note { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
