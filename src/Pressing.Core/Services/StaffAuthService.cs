using Microsoft.EntityFrameworkCore;
using Pressing.Core.Data;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Core.Security;

namespace Pressing.Core.Services;

public class StaffAuthService
{
    private readonly PressingDbContext _db;
    private readonly TokenService _tokens;
    private readonly AuditService _audit;

    public StaffAuthService(PressingDbContext db, TokenService tokens, AuditService audit)
    {
        _db = db;
        _tokens = tokens;
        _audit = audit;
    }

    public async Task<AuthResponse> LoginAsync(StaffLoginRequest request, string? ip = null, CancellationToken ct = default)
    {
        string identifier = request.Username.Trim().ToLowerInvariant();
        StaffUser? staff = await _db.StaffUsers.FirstOrDefaultAsync(
            s => s.Username.ToLower() == identifier || (s.Email != null && s.Email.ToLower() == identifier), ct);

        if (staff is null || !staff.IsActive || !PasswordHasher.Verify(request.Password, staff.PasswordHash))
        {
            throw new UnauthorizedAccessException("Identifiant ou mot de passe incorrect.");
        }

        staff.LastLoginAt = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        (string token, DateTime expires) = _tokens.CreateStaffToken(staff);

        await _audit.LogAsync(
            new CurrentUser { StaffId = staff.Id, Name = staff.FullName, Role = staff.Role.ToString() },
            "login", nameof(StaffUser), staff.Id.ToString(), "Connexion réussie", ip, ct);

        return new AuthResponse(token, expires, staff.ToDto(), null);
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
        if (request.NewPassword.Length < 6)
        {
            throw new InvalidOperationException("Le nouveau mot de passe doit contenir au moins 6 caractères.");
        }

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
}
