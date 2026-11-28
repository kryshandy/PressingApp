namespace Pressing.Core.Dtos;

public record StaffUserDto(
    int Id,
    string FullName,
    string Username,
    string? Email,
    UserRole Role,
    string RoleLabel,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    bool TwoFactorEnabled = false,
    DateTime? LockedUntil = null)
{
    /// <summary>Compte verrouillé à cet instant (échecs de connexion répétés).</summary>
    public bool IsLocked => LockedUntil.HasValue && LockedUntil.Value > DateTime.Now;
}

public record CreateStaffRequest(
    string FullName,
    string Username,
    string? Email,
    UserRole Role,
    string Password);

public record UpdateStaffRequest(
    string FullName,
    string Username,
    string? Email,
    UserRole Role,
    bool IsActive);

public record StaffPasswordRequest(string Password);

public record ChangeStaffPasswordRequest(string CurrentPassword, string NewPassword);
