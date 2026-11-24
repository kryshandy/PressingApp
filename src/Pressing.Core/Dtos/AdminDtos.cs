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
    DateTime? LastLoginAt);

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
