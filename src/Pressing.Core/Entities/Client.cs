using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Pressing.Core.Entities;

public class Client
{
    public int Id { get; set; }

    [MaxLength(80)]
    public string FirstName { get; set; } = string.Empty;

    [MaxLength(80)]
    public string LastName { get; set; } = string.Empty;

    [MaxLength(30)]
    public string Phone { get; set; } = string.Empty;

    [MaxLength(30)]
    public string PhoneNormalized { get; set; } = string.Empty;

    [MaxLength(30)]
    public string WhatsApp { get; set; } = string.Empty;

    [MaxLength(160)]
    public string? Email { get; set; }

    [MaxLength(300)]
    public string? Address { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    [MaxLength(300)]
    public string? PasswordHash { get; set; }

    public bool WebAccessEnabled { get; set; }

    public bool IsActive { get; set; } = true;

    public int LoyaltyPoints { get; set; }

    public ClientNotificationPreference NotificationPreference { get; set; } = ClientNotificationPreference.None;

    public DateTime? ConsentGivenAt { get; set; }

    public DateTime? ConsentRevokedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public ICollection<Order> Orders { get; set; } = new List<Order>();

    public ICollection<Payment> Payments { get; set; } = new List<Payment>();

    public ICollection<LoyaltyTransaction> LoyaltyTransactions { get; set; } = new List<LoyaltyTransaction>();

    [NotMapped]
    public string FullName => $"{FirstName} {LastName}".Trim();

    [NotMapped]
    public string Initials
    {
        get
        {
            string first = string.IsNullOrWhiteSpace(FirstName) ? "?" : FirstName[..1];
            string last = string.IsNullOrWhiteSpace(LastName) ? string.Empty : LastName[..1];
            return (first + last).ToUpperInvariant();
        }
    }
}
