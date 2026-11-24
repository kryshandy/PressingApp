using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Pressing.Core.Data;
using Pressing.Core.Dtos;
using Pressing.Core.Email;
using Pressing.Core.Entities;
using Pressing.Core.Security;

namespace Pressing.Core.Services;

public class CustomerAuthService
{
    private readonly PressingDbContext _db;
    private readonly TokenService _tokens;
    private readonly EmailService _email;
    private readonly AuditService _audit;

    public CustomerAuthService(
        PressingDbContext db,
        TokenService tokens,
        EmailService email,
        AuditService audit)
    {
        _db = db;
        _tokens = tokens;
        _email = email;
        _audit = audit;
    }

    public async Task<AuthResponse> LoginAsync(ClientLoginRequest request, string? ip = null, CancellationToken ct = default)
    {
        string identifier = request.Identifier.Trim();
        string normalizedPhone = PhoneNormalizer.Normalize(identifier);
        string lower = identifier.ToLowerInvariant();

        Client? client = await _db.Clients.FirstOrDefaultAsync(c =>
            c.PhoneNormalized == normalizedPhone ||
            (c.Email != null && c.Email.ToLower() == lower), ct);

        if (client is null ||
            !client.IsActive ||
            !client.WebAccessEnabled ||
            !PasswordHasher.Verify(request.Password, client.PasswordHash))
        {
            throw new UnauthorizedAccessException("Identifiant ou mot de passe incorrect.");
        }

        (string token, DateTime expires) = _tokens.CreateClientToken(client);
        await _audit.LogAsync(
            new CurrentUser { ClientId = client.Id, Name = client.FullName, Role = "Client" },
            "login", nameof(Client), client.Id.ToString(), "Connexion espace client", ip, ct);

        return new AuthResponse(token, expires, null, await BuildClientDtoAsync(client, ct));
    }

    public async Task<ClientDto?> GetAsync(int clientId, CancellationToken ct = default)
    {
        Client? client = await _db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientId, ct);
        return client is null ? null : await BuildClientDtoAsync(client, ct);
    }

    public async Task<MessageResponse> ForgotPasswordAsync(string identifier, string? ip = null, CancellationToken ct = default)
    {
        string normalized = PhoneNormalizer.Normalize(identifier);
        string lower = identifier.Trim().ToLowerInvariant();
        Client? client = await _db.Clients.FirstOrDefaultAsync(c =>
            c.PhoneNormalized == normalized ||
            (c.Email != null && c.Email.ToLower() == lower), ct);

        if (client is not null && !string.IsNullOrWhiteSpace(client.Email) && client.IsActive)
        {
            string rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace("+", "-").Replace("/", "_").Replace("=", string.Empty);

            _db.PasswordResetTokens.Add(new PasswordResetToken
            {
                ClientId = client.Id,
                TokenHash = PasswordHasher.Sha256(rawToken),
                CreatedAt = DateTime.Now,
                ExpiresAt = DateTime.Now.AddHours(2),
                RequestedIp = ip
            });

            await _db.SaveChangesAsync(ct);
            await _email.QueuePasswordResetAsync(client, rawToken, ct);
            await _audit.LogAsync(
                new CurrentUser { ClientId = client.Id, Name = client.FullName, Role = "Client" },
                "forgot-password", nameof(Client), client.Id.ToString(), "Demande de réinitialisation", ip, ct);
        }

        return new MessageResponse(
            "Si un compte correspond à ces informations, un e-mail de réinitialisation vient d'être envoyé.");
    }

    public async Task<MessageResponse> ResetPasswordAsync(string token, string newPassword, string? ip = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || newPassword.Length < 6)
        {
            throw new InvalidOperationException("Le mot de passe doit contenir au moins 6 caractères.");
        }

        string hash = PasswordHasher.Sha256(token);
        PasswordResetToken? reset = await _db.PasswordResetTokens
            .Include(t => t.Client)
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.UsedAt == null && t.ExpiresAt > DateTime.Now, ct);

        if (reset is null)
        {
            throw new InvalidOperationException("Ce lien de réinitialisation est invalide ou expiré.");
        }

        reset.Client.PasswordHash = PasswordHasher.Hash(newPassword);
        reset.Client.WebAccessEnabled = true;
        reset.Client.UpdatedAt = DateTime.Now;
        reset.UsedAt = DateTime.Now;

        var otherTokens = await _db.PasswordResetTokens
            .Where(t => t.ClientId == reset.ClientId && t.UsedAt == null && t.Id != reset.Id)
            .ToListAsync(ct);
        foreach (PasswordResetToken other in otherTokens)
        {
            other.UsedAt = DateTime.Now;
        }

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(
            new CurrentUser { ClientId = reset.ClientId, Name = reset.Client.FullName, Role = "Client" },
            "reset-password", nameof(Client), reset.ClientId.ToString(), "Mot de passe réinitialisé", ip, ct);

        return new MessageResponse("Votre mot de passe a été réinitialisé. Vous pouvez vous connecter.");
    }

    public async Task<MessageResponse> ChangePasswordAsync(
        int clientId,
        ClientChangePasswordRequest request,
        string? ip = null,
        CancellationToken ct = default)
    {
        if (request.NewPassword.Length < 6)
        {
            throw new InvalidOperationException("Le nouveau mot de passe doit contenir au moins 6 caractères.");
        }

        Client client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == clientId, ct)
            ?? throw new KeyNotFoundException("Client introuvable.");

        if (!PasswordHasher.Verify(request.CurrentPassword, client.PasswordHash))
        {
            throw new InvalidOperationException("Le mot de passe actuel est incorrect.");
        }

        client.PasswordHash = PasswordHasher.Hash(request.NewPassword);
        client.WebAccessEnabled = true;
        client.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(
            new CurrentUser { ClientId = client.Id, Name = client.FullName, Role = "Client" },
            "password", nameof(Client), client.Id.ToString(), "Mot de passe modifié", ip, ct);

        return new MessageResponse("Votre mot de passe a été modifié.");
    }

    private async Task<ClientDto> BuildClientDtoAsync(Client client, CancellationToken ct)
    {
        var orders = await _db.Orders.AsNoTracking()
            .Where(o => o.ClientId == client.Id)
            .Select(o => new { o.Status, o.Total, o.PaidAmount, o.CreatedAt })
            .ToListAsync(ct);
        var active = orders.Where(o => o.Status != OrderStatus.Cancelled).ToList();
        decimal balance = active.Sum(o => o.Total - o.PaidAmount);
        DateTime? lastOrder = orders.Count == 0 ? null : orders.Max(o => o.CreatedAt);

        return client.ToDto(active.Count, balance, 0, lastOrder);
    }
}
