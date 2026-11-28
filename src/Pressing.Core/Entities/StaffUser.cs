using System.ComponentModel.DataAnnotations;

namespace Pressing.Core.Entities;

public class StaffUser
{
    public int Id { get; set; }

    [MaxLength(120)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(60)]
    public string Username { get; set; } = string.Empty;

    [MaxLength(160)]
    public string? Email { get; set; }

    [MaxLength(300)]
    public string PasswordHash { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.Cashier;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public DateTime? LastLoginAt { get; set; }

    /// <summary>Échecs de connexion consécutifs (remis à 0 par une connexion réussie ou à l'expiration du verrouillage).</summary>
    public int FailedLoginCount { get; set; }

    /// <summary>Fin du verrouillage (5 échecs consécutifs = 15 minutes) ; null ou passée = compte utilisable.</summary>
    public DateTime? LockedUntil { get; set; }

    /// <summary>Double authentification TOTP activée (code demandé après le mot de passe).</summary>
    public bool TwoFactorEnabled { get; set; }

    /// <summary>Secret TOTP chiffré (Data Protection) ; en cours d'activation tant que <see cref="TwoFactorEnabled"/> est faux.</summary>
    [MaxLength(600)]
    public string? TwoFactorSecret { get; set; }

    /// <summary>Dernier pas TOTP accepté à la connexion (refuse le rejeu d'un même code).</summary>
    public long TwoFactorLastStep { get; set; }

    /// <summary>Codes de secours non utilisés : tableau JSON d'empreintes SHA-256 (jamais les codes).</summary>
    [MaxLength(2000)]
    public string? RecoveryCodeHashes { get; set; }

    public ICollection<Order> Orders { get; set; } = new List<Order>();
}
