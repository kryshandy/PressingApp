using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pressing.Core.Data;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Core.Security;

namespace Pressing.Core.Services;

/// <summary>
/// Double authentification TOTP du personnel : activation (secret + code de contrôle), codes de secours à usage unique,
/// désactivation et réinitialisation par un administrateur. Le secret est chiffré avant d'aller en base
/// (<see cref="ISecretProtector"/>) ; les codes de secours ne sont conservés que sous forme d'empreintes.
/// </summary>
public class TwoFactorService
{
    public const string Issuer = "Pressing Éclat";
    public const int RecoveryCodeCount = 8;

    private const string RecoveryAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int RecoveryCodeLength = 10;

    private readonly PressingDbContext _db;
    private readonly ISecretProtector _protector;
    private readonly AuditService _audit;
    private readonly TimeProvider _time;
    private readonly SettingsService _settings;

    public TwoFactorService(PressingDbContext db, ISecretProtector protector, AuditService audit, TimeProvider time, SettingsService settings)
    {
        _db = db;
        _protector = protector;
        _audit = audit;
        _time = time;
        _settings = settings;
    }

    /// <summary>La double authentification ne concerne que les comptes Manager.</summary>
    public static bool Applies(StaffUser staff) => staff.Role == UserRole.Manager;

    /// <summary>Exigée à la connexion : Manager et réglage global actif (Paramètres → Sécurité).</summary>
    public async Task<bool> IsRequiredAsync(StaffUser staff, CancellationToken ct = default) =>
        Applies(staff) && await _settings.GetBoolAsync(SettingsKeys.RequireManagerTwoFactor, true, ct);


    public async Task<TwoFactorStatusDto> GetStatusAsync(int staffId, CancellationToken ct = default)
    {
        StaffUser staff = await FindAsync(staffId, ct);
        return new TwoFactorStatusDto(staff.TwoFactorEnabled, ReadHashes(staff).Count, await IsRequiredAsync(staff, ct), Applies(staff));
    }

    /// <summary>Génère un nouveau secret (non actif tant qu'un code valide n'a pas été saisi). Peut être rappelée pour repartir de zéro.</summary>
    public async Task<TwoFactorSetupDto> BeginSetupAsync(int staffId, CancellationToken ct = default)
    {
        StaffUser staff = await FindAsync(staffId, ct);
        EnsureApplies(staff);
        if (staff.TwoFactorEnabled)
        {
            throw new InvalidOperationException("La double authentification est déjà activée sur ce compte.");
        }

        string secret = Totp.GenerateSecret();
        staff.TwoFactorSecret = _protector.Protect(secret);
        staff.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync(ct);

        return new TwoFactorSetupDto(secret, Totp.BuildUri(Issuer, staff.Username, secret), Issuer, staff.Username);
    }

    /// <summary>Active la double authentification après vérification d'un premier code ; renvoie les codes de secours (une seule fois).</summary>
    public async Task<TwoFactorEnabledDto> EnableAsync(int staffId, string code, string? ip = null, CancellationToken ct = default)
    {
        StaffUser staff = await FindAsync(staffId, ct);
        EnsureApplies(staff);
        if (staff.TwoFactorEnabled)
        {
            throw new InvalidOperationException("La double authentification est déjà activée sur ce compte.");
        }

        string? secret = staff.TwoFactorSecret is null ? null : _protector.Unprotect(staff.TwoFactorSecret);
        if (secret is null)
        {
            throw new InvalidOperationException("Aucune activation en cours : demandez d'abord un nouveau QR code.");
        }

        if (Totp.Verify(secret, code, _time.GetUtcNow()) is null)
        {
            throw new InvalidOperationException("Code incorrect. Saisissez le code à 6 chiffres affiché par l'application (il change toutes les 30 secondes).");
        }

        List<string> codes = Enumerable.Range(0, RecoveryCodeCount).Select(_ => NewRecoveryCode()).ToList();
        staff.TwoFactorEnabled = true;
        staff.TwoFactorLastStep = 0;
        staff.RecoveryCodeHashes = JsonSerializer.Serialize(codes.Select(c => HashRecovery(staff.Id, c)));
        staff.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AsUser(staff), "2fa-enable", nameof(StaffUser), staff.Id.ToString(),
            "Double authentification activée", ip, ct);

        return new TwoFactorEnabledDto(codes);
    }

    /// <summary>Désactive la double authentification : exige le mot de passe ET un code (application ou secours).</summary>
    public async Task<MessageResponse> DisableAsync(int staffId, DisableTwoFactorRequest request, string? ip = null, CancellationToken ct = default)
    {
        StaffUser staff = await FindAsync(staffId, ct);
        if (!staff.TwoFactorEnabled)
        {
            throw new InvalidOperationException("La double authentification n'est pas activée sur ce compte.");
        }

        if (await IsRequiredAsync(staff, ct))
        {
            throw new InvalidOperationException(
                "La double authentification est exigée des managers : seul un administrateur peut la désactiver (Paramètres ou écran Personnel).");
        }

        if (!PasswordHasher.Verify(request.Password, staff.PasswordHash))
        {
            throw new InvalidOperationException("Le mot de passe est incorrect.");
        }

        if (!TryVerify(staff, request.Code, enforceReplay: false))
        {
            throw new InvalidOperationException("Code incorrect : saisissez un code de l'application ou un code de secours.");
        }

        Clear(staff);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AsUser(staff), "2fa-disable", nameof(StaffUser), staff.Id.ToString(),
            "Double authentification désactivée", ip, ct);
        return new MessageResponse("La double authentification est désactivée.");
    }

    /// <summary>Réinitialisation par un administrateur (téléphone perdu, codes de secours épuisés) : le membre devra la réactiver.</summary>
    public async Task<MessageResponse> ResetAsync(int targetId, CurrentUser admin, string? ip = null, CancellationToken ct = default)
    {
        StaffUser staff = await FindAsync(targetId, ct);
        Clear(staff);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(admin, "2fa-reset", nameof(StaffUser), staff.Id.ToString(),
            $"Double authentification réinitialisée pour {staff.FullName}", ip, ct);
        return new MessageResponse($"La double authentification de {staff.FullName} a été réinitialisée.");
    }

    /// <summary>
    /// Contrôle un code de connexion : TOTP (6 chiffres, ±30 s, un pas ne sert qu'une fois) ou code de secours (usage unique).
    /// Modifie <paramref name="staff"/> (dernier pas, codes restants) : l'appelant enregistre.
    /// </summary>
    public bool TryVerify(StaffUser staff, string? code, bool enforceReplay)
    {
        if (string.IsNullOrWhiteSpace(code) || !staff.TwoFactorEnabled)
        {
            return false;
        }

        if (Totp.Normalize(code) is not null && staff.TwoFactorSecret is not null)
        {
            string? secret = _protector.Unprotect(staff.TwoFactorSecret);
            long? step = secret is null ? null : Totp.Verify(secret, code, _time.GetUtcNow());
            if (step is null)
            {
                return false;
            }

            if (enforceReplay)
            {
                if (step <= staff.TwoFactorLastStep)
                {
                    return false; // même code déjà utilisé : rejeu refusé
                }

                staff.TwoFactorLastStep = step.Value;
            }

            return true;
        }

        return TryConsumeRecovery(staff, code);
    }

    /// <summary>Vrai si le code de la dernière vérification était un code de secours (pour l'audit).</summary>
    public static bool LooksLikeRecoveryCode(string? code) => !string.IsNullOrWhiteSpace(code) && Totp.Normalize(code) is null;

    private bool TryConsumeRecovery(StaffUser staff, string code)
    {
        string normalized = NormalizeRecovery(code);
        if (normalized.Length != RecoveryCodeLength)
        {
            return false;
        }

        List<string> hashes = ReadHashes(staff);
        string wanted = HashRecovery(staff.Id, normalized);
        string? found = null;
        foreach (string hash in hashes)
        {
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(wanted)))
            {
                found = hash;
            }
        }

        if (found is null)
        {
            return false;
        }

        hashes.Remove(found);
        staff.RecoveryCodeHashes = JsonSerializer.Serialize(hashes);
        return true;
    }

    private static void EnsureApplies(StaffUser staff)
    {
        if (!Applies(staff))
        {
            throw new InvalidOperationException("La double authentification est réservée aux comptes responsables (Manager).");
        }
    }

    private static void Clear(StaffUser staff)
    {
        staff.TwoFactorEnabled = false;
        staff.TwoFactorSecret = null;
        staff.TwoFactorLastStep = 0;
        staff.RecoveryCodeHashes = null;
        staff.UpdatedAt = DateTime.Now;
    }

    private static List<string> ReadHashes(StaffUser staff)
    {
        if (string.IsNullOrWhiteSpace(staff.RecoveryCodeHashes))
        {
            return new List<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(staff.RecoveryCodeHashes) ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private static string NewRecoveryCode()
    {
        Span<char> chars = stackalloc char[RecoveryCodeLength + 1];
        for (int i = 0; i < RecoveryCodeLength; i++)
        {
            chars[i < 5 ? i : i + 1] = RecoveryAlphabet[RandomNumberGenerator.GetInt32(RecoveryAlphabet.Length)];
        }

        chars[5] = '-';
        return new string(chars);
    }

    private static string NormalizeRecovery(string code) =>
        new(code.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>Empreinte SHA-256 salée par le compte (les codes ont ~50 bits d'entropie : inutile d'allonger le calcul).</summary>
    private static string HashRecovery(int staffId, string code) =>
        PasswordHasher.Sha256($"pressing-recovery:{staffId}:{NormalizeRecovery(code)}");

    private async Task<StaffUser> FindAsync(int id, CancellationToken ct) =>
        await _db.StaffUsers.FirstOrDefaultAsync(s => s.Id == id, ct)
        ?? throw new KeyNotFoundException("Utilisateur introuvable.");

    private static CurrentUser AsUser(StaffUser staff) =>
        new() { StaffId = staff.Id, Name = staff.FullName, Role = staff.Role.ToString() };
}
