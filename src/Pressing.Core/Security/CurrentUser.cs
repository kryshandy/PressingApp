namespace Pressing.Core.Security;

public class CurrentUser
{
    public int? StaffId { get; init; }
    public int? ClientId { get; init; }
    public string Name { get; init; } = "Système";
    public string Role { get; init; } = "System";
    public bool IsStaff => StaffId.HasValue;
    public bool IsClient => ClientId.HasValue;

    public static CurrentUser System => new() { Name = "Système", Role = "System" };
}
