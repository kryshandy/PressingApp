namespace Pressing.Core.Dtos;

public record StaffLoginRequest(string Username, string Password);

public record ClientLoginRequest(string Identifier, string Password);

public record AuthResponse(
    string Token,
    DateTime ExpiresAt,
    StaffUserDto? Staff,
    ClientDto? Client,
    bool MfaRequired = false,
    bool TwoFactorSetupRequired = false);

/// <summary>Second temps de la connexion : code à 6 chiffres de l'application d'authentification, ou code de secours.</summary>
public record StaffMfaRequest(string Code);

/// <summary>Début d'activation : secret à saisir à la main et URI « otpauth » à encoder en QR code.</summary>
public record TwoFactorSetupDto(string Secret, string OtpAuthUri, string Issuer, string Account);

public record TwoFactorCodeRequest(string Code);

/// <summary>Résultat de l'activation : les codes de secours ne sont affichés qu'une seule fois.</summary>
public record TwoFactorEnabledDto(IReadOnlyList<string> RecoveryCodes);

public record DisableTwoFactorRequest(string Password, string Code);

/// <param name="Available">La double authentification existe pour ce rôle (Manager uniquement).</param>
/// <param name="Required">Exigée à la connexion : Manager et réglage « exiger la double authentification » actif.</param>
public record TwoFactorStatusDto(bool Enabled, int RecoveryCodesLeft, bool Required, bool Available);

public record ForgotPasswordRequest(string Identifier);

public record ResetPasswordRequest(string Token, string NewPassword);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record ClientChangePasswordRequest(string CurrentPassword, string NewPassword);

public record MessageResponse(string Message);
