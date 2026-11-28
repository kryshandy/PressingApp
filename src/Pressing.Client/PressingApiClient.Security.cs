using Pressing.Core.Dtos;

namespace Pressing.Client;

// Sécurité des comptes du personnel : double authentification (TOTP) et déverrouillage.
public sealed partial class PressingApiClient
{
    /// <summary>
    /// Second temps de la connexion quand <see cref="AuthResponse.MfaRequired"/> est vrai : le jeton intermédiaire
    /// (déjà mémorisé par <c>LoginStaffAsync</c>) est envoyé avec le code à 6 chiffres ou un code de secours ;
    /// le vrai jeton remplace alors le jeton intermédiaire.
    /// </summary>
    public async Task<AuthResponse?> LoginStaffMfaAsync(string code, CancellationToken ct = default)
    {
        var r = await PostAsync<AuthResponse>("api/auth/staff/login/mfa", new StaffMfaRequest(code), ct);
        if (r is not null) Token = r.Token;
        return r;
    }

    public Task<TwoFactorStatusDto?> GetTwoFactorStatusAsync(CancellationToken ct = default) =>
        GetAsync<TwoFactorStatusDto>("api/auth/staff/2fa", ct);

    /// <summary>Génère le secret et l'URI « otpauth » à afficher en QR code (la double authentification reste inactive).</summary>
    public Task<TwoFactorSetupDto?> BeginTwoFactorSetupAsync(CancellationToken ct = default) =>
        PostAsync<TwoFactorSetupDto>("api/auth/staff/2fa/setup", null, ct);

    /// <summary>Active la double authentification avec un premier code ; renvoie les codes de secours (affichés une seule fois).</summary>
    public Task<TwoFactorEnabledDto?> EnableTwoFactorAsync(string code, CancellationToken ct = default) =>
        PostAsync<TwoFactorEnabledDto>("api/auth/staff/2fa/enable", new TwoFactorCodeRequest(code), ct);

    public Task<MessageResponse?> DisableTwoFactorAsync(string password, string code, CancellationToken ct = default) =>
        PostAsync<MessageResponse>("api/auth/staff/2fa/disable", new DisableTwoFactorRequest(password, code), ct);

    /// <summary>Administrateur : déverrouille un compte bloqué par des échecs de connexion.</summary>
    public Task<MessageResponse?> UnlockStaffAsync(int id, CancellationToken ct = default) =>
        PostAsync<MessageResponse>($"api/staff/{id}/unlock", null, ct);

    /// <summary>Administrateur : réinitialise la double authentification d'un membre (téléphone perdu).</summary>
    public Task<MessageResponse?> ResetStaffTwoFactorAsync(int id, CancellationToken ct = default) =>
        PostAsync<MessageResponse>($"api/staff/{id}/two-factor/reset", null, ct);
}
