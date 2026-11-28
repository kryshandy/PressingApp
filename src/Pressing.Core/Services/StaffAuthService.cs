using Microsoft.EntityFrameworkCore;
using Pressing.Core.Data;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Core.Security;

namespace Pressing.Core.Services;

/// <summary>Compte du personnel verrouillé après des échecs répétés (HTTP 423 côté API).</summary>
public sealed class StaffAccountLockedException : Exception
{
    public StaffAccountLockedException(DateTime until, string message) : base(message)
    {
        Until = until;
    }

    public DateTime Until { get; }
}

public class StaffAuthService
{
    /// <summary>Échecs consécutifs qui verrouillent le compte.</summary>
    public const int MaxFailedAttempts = 5;

    /// <summary>Durée du verrouillage, en minutes.</summary>
    public const int LockMinutes = 15;

    private const string BadCredentials = "Identifiant ou mot de passe incorrect.";

    private readonly PressingDbContext _db;
    private readonly TokenService _tokens;
    private readonly AuditService _audit;
    private readonly TwoFactorService _twoFactor;
    private readonly TimeProvider _time;

    public StaffAuthService(
        PressingDbContext db,
        TokenService tokens,
        AuditService audit,
        TwoFactorService twoFactor,
        TimeProvider time)
    {
        _db = db;
        _tokens = tokens;
        _audit = audit;
        _twoFactor = twoFactor;
        _time = time;
    }

    /// <summary>
    /// Premier temps de la connexion. Sans double authentification : jeton complet. Avec : jeton intermédiaire
    /// (<c>MfaRequired</c>, 5 minutes) à compléter par <see cref="LoginMfaAsync"/>.
    /// Un identifiant inconnu et un mot de passe faux donnent le même message.
    /// </summary>
    public async Task<AuthResponse> LoginAsync(StaffLoginRequest request, string? ip = null, CancellationToken ct = default)
    {
        DateTime now = _time.GetLocalNow().DateTime;
        string identifier = (request.Username ?? string.Empty).Trim().ToLowerInvariant();
        StaffUser? staff = await _db.StaffUsers.FirstOrDefaultAsync(
            s => s.Username.ToLower() == identifier || (s.Email != null && s.Email.ToLower() == identifier), ct);

        if (staff is null || !staff.IsActive)
        {
            throw new UnauthorizedAccessException(BadCredentials);
        }

        ThrowIfLocked(staff, now);
        await ReleaseExpiredLockAsync(staff, now, ct);

        if (!PasswordHasher.Verify(request.Password, staff.PasswordHash))
        {
            await RegisterFailureAsync(staff, now, ip, BadCredentials, ct);
        }

        if (staff.TwoFactorEnabled && await _twoFactor.IsRequiredAsync(staff, ct))
        {
            // Le compteur d'échecs n'est remis à zéro qu'à la fin du second temps.
            (string pending, DateTime pendingExpires) = _tokens.CreateMfaPendingToken(staff);
            return new AuthResponse(pending, pendingExpires, null, null, MfaRequired: true);
        }

        return await CompleteLoginAsync(staff, now, ip, "Connexion réussie", ct);
    }

    /// <summary>Second temps : jeton intermédiaire (« mfa-pending ») + code TOTP ou code de secours.</summary>
    public async Task<AuthResponse> LoginMfaAsync(string? pendingToken, string? code, string? ip = null, CancellationToken ct = default)
    {
        const string expired = "La vérification a expiré. Reconnectez-vous avec votre identifiant et votre mot de passe.";
        DateTime now = _time.GetLocalNow().DateTime;

        var principal = string.IsNullOrWhiteSpace(pendingToken) ? null : _tokens.Validate(pendingToken);
        if (principal?.FindFirst("token_type")?.Value != TokenService.MfaPendingType ||
            !int.TryParse(principal.FindFirst("staff_id")?.Value, out int staffId))
        {
            throw new UnauthorizedAccessException(expired);
        }

        StaffUser staff = await _db.StaffUsers.FirstOrDefaultAsync(s => s.Id == staffId, ct)
            ?? throw new UnauthorizedAccessException(expired);
        if (!staff.IsActive || !staff.TwoFactorEnabled)
        {
            throw new UnauthorizedAccessException(expired);
        }

        ThrowIfLocked(staff, now);
        await ReleaseExpiredLockAsync(staff, now, ct);

        if (!_twoFactor.TryVerify(staff, code, enforceReplay: true))
        {
            await RegisterFailureAsync(staff, now, ip, "Code de vérification incorrect ou déjà utilisé.", ct);
        }

        bool recovery = TwoFactorService.LooksLikeRecoveryCode(code);
        return await CompleteLoginAsync(staff, now, ip,
            recovery ? "Connexion réussie (code de secours utilisé)" : "Connexion réussie (double authentification)", ct);
    }

    public async Task<StaffUserDto> GetAsync(int id, CancellationToken ct = default)
    {
        StaffUser staff = await _db.StaffUsers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new KeyNotFoundException("Utilisateur introuvable.");
        return staff.ToDto();
    }

    public async Task<MessageResponse> ChangePasswordAsync(
        int staffId,
        ChangeStaffPasswordRequest request,
        string? ip = null,
        CancellationToken ct = default)
    {
        PasswordPolicy.EnsureValid(request.NewPassword);

        StaffUser staff = await _db.StaffUsers.FirstOrDefaultAsync(s => s.Id == staffId, ct)
            ?? throw new KeyNotFoundException("Utilisateur introuvable.");

        if (!PasswordHasher.Verify(request.CurrentPassword, staff.PasswordHash))
        {
            throw new InvalidOperationException("Le mot de passe actuel est incorrect.");
        }

        staff.PasswordHash = PasswordHasher.Hash(request.NewPassword);
        staff.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(
            new CurrentUser { StaffId = staff.Id, Name = staff.FullName, Role = staff.Role.ToString() },
            "password", nameof(StaffUser), staff.Id.ToString(), "Mot de passe modifié", ip, ct);

        return new MessageResponse("Votre mot de passe a été modifié.");
    }

    private async Task<AuthResponse> CompleteLoginAsync(StaffUser staff, DateTime now, string? ip, string details, CancellationToken ct)
    {
        staff.FailedLoginCount = 0;
        staff.LockedUntil = null;
        staff.LastLoginAt = now;
        await _db.SaveChangesAsync(ct);
        (string token, DateTime expires) = _tokens.CreateStaffToken(staff);

        await _audit.LogAsync(AsUser(staff), "login", nameof(StaffUser), staff.Id.ToString(), details, ip, ct);
        // Manager sans 2FA alors que le réglage l'exige : la connexion aboutit, le Desktop impose l'activation avant la coque.
        bool setupRequired = !staff.TwoFactorEnabled && await _twoFactor.IsRequiredAsync(staff, ct);
        return new AuthResponse(token, expires, staff.ToDto(), null, TwoFactorSetupRequired: setupRequired);
    }

    private static void ThrowIfLocked(StaffUser staff, DateTime now)
    {
        if (staff.LockedUntil.HasValue && staff.LockedUntil.Value > now)
        {
            throw new StaffAccountLockedException(staff.LockedUntil.Value, LockedMessage(staff.LockedUntil.Value, now));
        }
    }

    /// <summary>Un verrouillage échu repart à zéro : le compteur d'échecs ne se « souvient » pas de la période précédente.</summary>
    private async Task ReleaseExpiredLockAsync(StaffUser staff, DateTime now, CancellationToken ct)
    {
        if (staff.LockedUntil.HasValue && staff.LockedUntil.Value <= now)
        {
            staff.LockedUntil = null;
            staff.FailedLoginCount = 0;
            await _db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Compte un échec ; au 5e consécutif, verrouille 15 minutes. Lève toujours une exception.</summary>
    private async Task RegisterFailureAsync(StaffUser staff, DateTime now, string? ip, string message, CancellationToken ct)
    {
        staff.FailedLoginCount++;
        if (staff.FailedLoginCount >= MaxFailedAttempts)
        {
            DateTime until = now.AddMinutes(LockMinutes);
            staff.LockedUntil = until;
            await _db.SaveChangesAsync(ct);
            await _audit.LogAsync(AsUser(staff), "lockout", nameof(StaffUser), staff.Id.ToString(),
                $"Compte verrouillé jusqu'à {until:HH:mm} après {MaxFailedAttempts} échecs de connexion consécutifs", ip, ct);
            throw new StaffAccountLockedException(until, LockedMessage(until, now));
        }

        await _db.SaveChangesAsync(ct);
        throw new UnauthorizedAccessException(message);
    }

    /// <summary>« Compte verrouillé … jusqu'à 14:35 » (heure locale du serveur, celle du poste de caisse).</summary>
    public static string LockedMessage(DateTime until, DateTime now)
    {
        int minutes = Math.Max(1, (int)Math.Ceiling((until - now).TotalMinutes));
        return $"Compte verrouillé après {MaxFailedAttempts} échecs de connexion. " +
               $"Il sera déverrouillé à {until:HH:mm} (dans {minutes} min) ; un administrateur peut aussi le déverrouiller.";
    }

    private static CurrentUser AsUser(StaffUser staff) =>
        new() { StaffId = staff.Id, Name = staff.FullName, Role = staff.Role.ToString() };
}
