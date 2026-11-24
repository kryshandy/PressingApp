using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Pressing.Core.Entities;

public class Category
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(40)]
    public string Icon { get; set; } = "\uE7C1";

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ICollection<CategoryPrice> Prices { get; set; } = new List<CategoryPrice>();

    [NotMapped]
    public decimal DefaultPrice => Prices
        .Where(p => p.ServiceType == ServiceType.WashAndIron && p.IsActive)
        .Select(p => p.Price)
        .FirstOrDefault();
}

public class CategoryPrice
{
    public int Id { get; set; }

    public int CategoryId { get; set; }

    public Category Category { get; set; } = null!;

    public ServiceType ServiceType { get; set; }

    public decimal Price { get; set; }

    public decimal ExpressSupplement { get; set; }

    public int EstimatedDays { get; set; } = 3;

    public bool IsActive { get; set; } = true;
}
