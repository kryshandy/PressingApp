namespace Pressing.Core.Dtos;

public record StaffLoginRequest(string Username, string Password);

public record ClientLoginRequest(string Identifier, string Password);

public record AuthResponse(
    string Token,
    DateTime ExpiresAt,
    StaffUserDto? Staff,
    ClientDto? Client);

public record ForgotPasswordRequest(string Identifier);

public record ResetPasswordRequest(string Token, string NewPassword);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record ClientChangePasswordRequest(string CurrentPassword, string NewPassword);

public record MessageResponse(string Message);
