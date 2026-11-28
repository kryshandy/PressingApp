using Microsoft.EntityFrameworkCore;
using Pressing.Core.Data;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Core.Security;

namespace Pressing.Core.Services;

public class StaffService
{
    private readonly PressingDbContext _db;
    private readonly AuditService _audit;

    public StaffService(PressingDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<StaffUserDto>> GetAsync(CancellationToken ct = default)
    {
        return await _db.StaffUsers.AsNoTracking()
            .OrderBy(s => s.FullName)
            .Select(s => s.ToDto())
            .ToListAsync(ct);
    }

    public async Task<StaffUserDto> CreateAsync(
        CreateStaffRequest request,
        CurrentUser user,
        CancellationToken ct = default)
    {
        Validate(request.FullName, request.Username, request.Password);
        string username = request.Username.Trim().ToLowerInvariant();

        if (await _db.StaffUsers.AnyAsync(s => s.Username.ToLower() == username, ct))
        {
            throw new InvalidOperationException("Ce nom d'utilisateur est déjà utilisé.");
        }

        var staff = new StaffUser
        {
            FullName = request.FullName.Trim(),
            Username = username,
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            Role = request.Role,
            PasswordHash = PasswordHasher.Hash(request.Password),
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        _db.StaffUsers.Add(staff);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(user, "create", nameof(StaffUser), staff.Id.ToString(), staff.FullName, ct: ct);
        return staff.ToDto();
    }

    public async Task<StaffUserDto> UpdateAsync(
        int id,
        UpdateStaffRequest request,
        CurrentUser user,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.Username))
        {
            throw new InvalidOperationException("Le nom complet et le nom d'utilisateur sont obligatoires.");
        }

        StaffUser staff = await _db.StaffUsers.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new KeyNotFoundException("Utilisateur introuvable.");

        string username = request.Username.Trim().ToLowerInvariant();
        if (await _db.StaffUsers.AnyAsync(s => s.Id != id && s.Username.ToLower() == username, ct))
        {
            throw new InvalidOperationException("Ce nom d'utilisateur est déjà utilisé.");
        }

        bool losesAdmin = staff.Role == UserRole.Admin && staff.IsActive &&
            (request.Role != UserRole.Admin || !request.IsActive);
        if (losesAdmin && await CountActiveAdminsAsync(ct) <= 1)
        {
            throw new InvalidOperationException("Il doit rester au moins un administrateur actif.");
        }

        staff.FullName = request.FullName.Trim();
        staff.Username = username;
        staff.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        staff.Role = request.Role;
        staff.IsActive = request.IsActive;
        staff.UpdatedAt = DateTime.Now;

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(user, "update", nameof(StaffUser), staff.Id.ToString(), staff.FullName, ct: ct);
        return staff.ToDto();
    }

    public async Task<MessageResponse> SetPasswordAsync(
        int id,
        string password,
        CurrentUser user,
        CancellationToken ct = default)
    {
        PasswordPolicy.EnsureValid(password);

        StaffUser staff = await _db.StaffUsers.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new KeyNotFoundException("Utilisateur introuvable.");

        staff.PasswordHash = PasswordHasher.Hash(password);
        staff.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(user, "password", nameof(StaffUser), staff.Id.ToString(), "Mot de passe réinitialisé", ct: ct);
        return new MessageResponse("Le mot de passe a été mis à jour.");
    }

    /// <summary>Déverrouille un compte bloqué par des échecs de connexion (administrateur) ; l'action est auditée.</summary>
    public async Task<MessageResponse> UnlockAsync(int id, CurrentUser user, string? ip = null, CancellationToken ct = default)
    {
        StaffUser staff = await _db.StaffUsers.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new KeyNotFoundException("Utilisateur introuvable.");

        bool wasLocked = staff.LockedUntil.HasValue && staff.LockedUntil.Value > DateTime.Now;
        staff.FailedLoginCount = 0;
        staff.LockedUntil = null;
        staff.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(user, "unlock", nameof(StaffUser), staff.Id.ToString(),
            wasLocked ? $"Compte de {staff.FullName} déverrouillé" : $"Échecs de connexion de {staff.FullName} remis à zéro", ip, ct);

        return new MessageResponse(wasLocked
            ? $"Le compte de {staff.FullName} est déverrouillé."
            : $"Le compte de {staff.FullName} n'était pas verrouillé ; ses échecs de connexion ont été remis à zéro.");
    }

    private async Task<int> CountActiveAdminsAsync(CancellationToken ct) =>
        await _db.StaffUsers.CountAsync(s => s.Role == UserRole.Admin && s.IsActive, ct);

    private static void Validate(string fullName, string username, string password)
    {
        if (string.IsNullOrWhiteSpace(fullName)) throw new InvalidOperationException("Le nom complet est obligatoire.");
        if (string.IsNullOrWhiteSpace(username)) throw new InvalidOperationException("Le nom d'utilisateur est obligatoire.");
        PasswordPolicy.EnsureValid(password);
    }
}
