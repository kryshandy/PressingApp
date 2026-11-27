using Microsoft.EntityFrameworkCore;
using Pressing.Core.Data;
using Pressing.Core.Dtos;
using Pressing.Core.Email;
using Pressing.Core.Entities;
using Pressing.Core.Security;

namespace Pressing.Core.Services;

public class ClientService
{
    private readonly PressingDbContext _db;
    private readonly EmailService _email;
    private readonly AuditService _audit;

    public ClientService(PressingDbContext db, EmailService email, AuditService audit)
    {
        _db = db;
        _email = email;
        _audit = audit;
    }

    public async Task<PagedResult<ClientDto>> SearchAsync(
        string? search = null,
        int page = 1,
        int pageSize = 25,
        bool includeInactive = false,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        IQueryable<Client> query = _db.Clients.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string raw = search.Trim();
            string term = $"%{raw}%";
            // Un texte sans chiffre (nom, ticket sans numéro…) donnerait le motif "%%" qui correspond à tout :
            // on ne filtre sur le téléphone normalisé que s'il y a des chiffres à chercher.
            string digits = PhoneNormalizer.Normalize(raw);
            bool searchPhone = digits.Length > 0;
            string normalized = $"%{digits}%";
            query = query.Where(c =>
                EF.Functions.Like(c.FirstName, term) ||
                EF.Functions.Like(c.LastName, term) ||
                EF.Functions.Like(c.Phone, term) ||
                (searchPhone && EF.Functions.Like(c.PhoneNormalized, normalized)) ||
                EF.Functions.Like(c.WhatsApp, term) ||
                EF.Functions.Like(c.Email ?? string.Empty, term) ||
                c.Orders.Any(o => EF.Functions.Like(o.TicketNumber, term)));
        }

        int total = await query.CountAsync(ct);
        var clients = await query
            .OrderBy(c => c.LastName)
            .ThenBy(c => c.FirstName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        Dictionary<int, ClientStats> stats = await LoadStatsAsync(clients.Select(c => c.Id).ToArray(), ct);
        var items = clients.Select(c =>
        {
            stats.TryGetValue(c.Id, out ClientStats? stat);
            return c.ToDto(stat?.OrderCount ?? 0, stat?.Balance ?? 0, stat?.TotalSpent ?? 0, stat?.LastOrderAt);
        }).ToList();

        return new PagedResult<ClientDto>(items, total, page, pageSize);
    }

    public async Task<ClientDto?> GetAsync(int id, CancellationToken ct = default)
    {
        Client? client = await _db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (client is null) return null;

        Dictionary<int, ClientStats> stats = await LoadStatsAsync([id], ct);
        stats.TryGetValue(id, out ClientStats? stat);
        return client.ToDto(stat?.OrderCount ?? 0, stat?.Balance ?? 0, stat?.TotalSpent ?? 0, stat?.LastOrderAt);
    }

    public async Task<ClientDetailDto?> GetDetailAsync(int id, CancellationToken ct = default)
    {
        Client? client = await _db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (client is null) return null;

        Dictionary<int, ClientStats> stats = await LoadStatsAsync([id], ct);
        stats.TryGetValue(id, out ClientStats? stat);

        var orders = await _db.Orders.AsNoTracking()
            .Include(o => o.Client)
            .Include(o => o.Items)
            .Where(o => o.ClientId == id)
            .OrderByDescending(o => o.CreatedAt)
            .Take(30)
            .ToListAsync(ct);

        var payments = await _db.Payments.AsNoTracking()
            .Include(p => p.Order)
            .Include(p => p.StaffUser)
            .Where(p => p.ClientId == id)
            .OrderByDescending(p => p.PaidAt)
            .Take(30)
            .ToListAsync(ct);

        var loyalty = await _db.LoyaltyTransactions.AsNoTracking()
            .Include(t => t.Order)
            .Where(t => t.ClientId == id)
            .OrderByDescending(t => t.CreatedAt)
            .Take(30)
            .ToListAsync(ct);

        return new ClientDetailDto(
            client.ToDto(stat?.OrderCount ?? 0, stat?.Balance ?? 0, stat?.TotalSpent ?? 0, stat?.LastOrderAt),
            orders.Select(o => o.ToSummary()).ToList(),
            payments.Select(p => p.ToDto()).ToList(),
            loyalty.Select(t => t.ToDto()).ToList());
    }

    public async Task<IReadOnlyList<ClientDto>> SearchLightAsync(string? search, int take = 20, CancellationToken ct = default)
    {
        PagedResult<ClientDto> result = await SearchAsync(search, 1, take, false, ct);
        return result.Items;
    }

    public async Task<ClientDto> CreateAsync(
        CreateClientRequest request,
        CurrentUser user,
        CancellationToken ct = default)
    {
        ValidateClient(request.FirstName, request.LastName, request.Phone, request.WhatsApp, request.Email);
        ValidateNotificationPreference(request.NotificationPreference, request.Email, request.WhatsApp);

        string phoneNormalized = PhoneNormalizer.Normalize(request.Phone);
        if (await _db.Clients.AnyAsync(c => c.PhoneNormalized == phoneNormalized, ct))
        {
            throw new InvalidOperationException("Un client utilise déjà ce numéro de téléphone.");
        }

        if (!string.IsNullOrWhiteSpace(request.Email) &&
            await _db.Clients.AnyAsync(c => c.Email != null && c.Email.ToLower() == request.Email.Trim().ToLower(), ct))
        {
            throw new InvalidOperationException("Un client utilise déjà cette adresse e-mail.");
        }

        string? temporaryPassword = null;
        bool webAccess = false;
        string? passwordHash = null;
        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            if (request.Password.Length < 6)
            {
                throw new InvalidOperationException("Le mot de passe doit contenir au moins 6 caractères.");
            }

            passwordHash = PasswordHasher.Hash(request.Password);
            webAccess = true;
        }
        else if (request.SendWelcomeEmail)
        {
            temporaryPassword = PasswordHasher.GenerateTemporaryPassword();
            passwordHash = PasswordHasher.Hash(temporaryPassword);
            webAccess = true;
        }

        var client = new Client
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Phone = request.Phone.Trim(),
            PhoneNormalized = phoneNormalized,
            WhatsApp = request.WhatsApp.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            Address = request.Address?.Trim(),
            Notes = request.Notes?.Trim(),
            PasswordHash = passwordHash,
            WebAccessEnabled = webAccess,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        ApplyNotificationPreference(client, request.NotificationPreference);
        _db.Clients.Add(client);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(user, "create", nameof(Client), client.Id.ToString(), $"Client {client.FullName}", ct: ct);

        if (request.SendWelcomeEmail && client.Email is not null)
        {
            await _email.QueueWelcomeAsync(client, temporaryPassword, ct);
        }

        return client.ToDto();
    }

    public async Task<ClientDto> UpdateAsync(
        int id,
        UpdateClientRequest request,
        CurrentUser user,
        CancellationToken ct = default)
    {
        ValidateClient(request.FirstName, request.LastName, request.Phone, request.WhatsApp, request.Email);
        if (request.NotificationPreference.HasValue)
        {
            ValidateNotificationPreference(request.NotificationPreference.Value, request.Email, request.WhatsApp);
        }

        Client client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException("Client introuvable.");

        string normalized = PhoneNormalizer.Normalize(request.Phone);
        if (await _db.Clients.AnyAsync(c => c.Id != id && c.PhoneNormalized == normalized, ct))
        {
            throw new InvalidOperationException("Un client utilise déjà ce numéro de téléphone.");
        }

        if (!string.IsNullOrWhiteSpace(request.Email) &&
            await _db.Clients.AnyAsync(c => c.Id != id && c.Email != null && c.Email.ToLower() == request.Email.Trim().ToLower(), ct))
        {
            throw new InvalidOperationException("Un client utilise déjà cette adresse e-mail.");
        }

        client.FirstName = request.FirstName.Trim();
        client.LastName = request.LastName.Trim();
        client.Phone = request.Phone.Trim();
        client.PhoneNormalized = normalized;
        client.WhatsApp = request.WhatsApp.Trim();
        client.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        if (request.NotificationPreference.HasValue)
        {
            ApplyNotificationPreference(client, request.NotificationPreference.Value);
        }
        client.Address = request.Address?.Trim();
        client.Notes = request.Notes?.Trim();
        client.IsActive = request.IsActive;
        client.UpdatedAt = DateTime.Now;

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(user, "update", nameof(Client), client.Id.ToString(), $"Client {client.FullName}", ct: ct);
        return await GetAsync(id, ct) ?? client.ToDto();
    }

    public async Task<ClientDto> UpdateProfileAsync(
        int id,
        UpdateProfileRequest request,
        CurrentUser user,
        CancellationToken ct = default)
    {
        ValidateClient(request.FirstName, request.LastName, request.Phone, request.WhatsApp, request.Email);
        if (request.NotificationPreference.HasValue)
        {
            ValidateNotificationPreference(request.NotificationPreference.Value, request.Email, request.WhatsApp);
        }
        Client client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException("Client introuvable.");

        string normalized = PhoneNormalizer.Normalize(request.Phone);
        if (await _db.Clients.AnyAsync(c => c.Id != id && c.PhoneNormalized == normalized, ct))
        {
            throw new InvalidOperationException("Ce numéro de téléphone est déjà utilisé.");
        }

        if (!string.IsNullOrWhiteSpace(request.Email) &&
            await _db.Clients.AnyAsync(c => c.Id != id && c.Email != null && c.Email.ToLower() == request.Email.Trim().ToLower(), ct))
        {
            throw new InvalidOperationException("Cette adresse e-mail est déjà utilisée.");
        }

        client.FirstName = request.FirstName.Trim();
        client.LastName = request.LastName.Trim();
        client.Phone = request.Phone.Trim();
        client.PhoneNormalized = normalized;
        client.WhatsApp = request.WhatsApp.Trim();
        client.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        if (request.NotificationPreference.HasValue)
        {
            ApplyNotificationPreference(client, request.NotificationPreference.Value);
        }
        client.Address = request.Address?.Trim();
        client.UpdatedAt = DateTime.Now;

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(user, "update-profile", nameof(Client), client.Id.ToString(), "Mise à jour du profil client", ct: ct);
        return await GetAsync(id, ct) ?? client.ToDto();
    }

    public async Task<ClientDto> SetPasswordAsync(
        int id,
        SetClientPasswordRequest request,
        CurrentUser user,
        CancellationToken ct = default)
    {
        Client client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException("Client introuvable.");

        if (!request.EnableWebAccess)
        {
            client.WebAccessEnabled = false;
            client.PasswordHash = null;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
            {
                throw new InvalidOperationException("Un mot de passe d'au moins 6 caractères est requis pour activer l'accès web.");
            }

            client.PasswordHash = PasswordHasher.Hash(request.Password);
            client.WebAccessEnabled = true;
            if (request.SendWelcomeEmail && client.Email is not null)
            {
                await _email.QueueWelcomeAsync(client, request.Password, ct);
            }
        }

        client.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(user, "password", nameof(Client), client.Id.ToString(), "Mise à jour de l'accès web", ct: ct);
        return await GetAsync(id, ct) ?? client.ToDto();
    }

    public async Task<ClientDto> AdjustLoyaltyAsync(
        int id,
        int points,
        string note,
        CurrentUser user,
        CancellationToken ct = default)
    {
        Client client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException("Client introuvable.");

        int newBalance = client.LoyaltyPoints + points;
        if (newBalance < 0)
        {
            throw new InvalidOperationException("Le solde de points ne peut pas devenir négatif.");
        }

        client.LoyaltyPoints = newBalance;
        client.UpdatedAt = DateTime.Now;
        _db.LoyaltyTransactions.Add(new LoyaltyTransaction
        {
            ClientId = client.Id,
            Points = points,
            Type = points >= 0 ? LoyaltyTransactionType.Adjusted : LoyaltyTransactionType.Adjusted,
            Note = note,
            CreatedAt = DateTime.Now
        });

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(user, "loyalty", nameof(Client), client.Id.ToString(), $"Ajustement de {points} points", ct: ct);
        return await GetAsync(id, ct) ?? client.ToDto();
    }

    public async Task<IReadOnlyList<LoyaltyTransactionDto>> GetLoyaltyAsync(int clientId, CancellationToken ct = default)
    {
        return await _db.LoyaltyTransactions.AsNoTracking()
            .Include(t => t.Order)
            .Where(t => t.ClientId == clientId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => t.ToDto())
            .ToListAsync(ct);
    }

    private async Task<Dictionary<int, ClientStats>> LoadStatsAsync(int[] clientIds, CancellationToken ct)
    {
        if (clientIds.Length == 0)
        {
            return new Dictionary<int, ClientStats>();
        }

        var orders = await _db.Orders.AsNoTracking()
            .Where(o => clientIds.Contains(o.ClientId))
            .Select(o => new { o.ClientId, o.Status, o.Total, o.PaidAmount, o.CreatedAt })
            .ToListAsync(ct);

        var payments = await _db.Payments.AsNoTracking()
            .Where(p => clientIds.Contains(p.ClientId))
            .Select(p => new { p.ClientId, p.Amount, p.Type })
            .ToListAsync(ct);

        return clientIds.ToDictionary(
            id => id,
            id =>
            {
                var clientOrders = orders.Where(o => o.ClientId == id).ToList();
                var activeOrders = clientOrders.Where(o => o.Status != OrderStatus.Cancelled).ToList();
                var clientPayments = payments.Where(p => p.ClientId == id).ToList();
                decimal spent = clientPayments.Where(p => p.Type == PaymentType.Payment).Sum(p => p.Amount)
                    - clientPayments.Where(p => p.Type == PaymentType.Refund).Sum(p => p.Amount);

                return new ClientStats(
                    activeOrders.Count,
                    activeOrders.Sum(o => o.Total - o.PaidAmount),
                    Math.Max(0, spent),
                    clientOrders.Count == 0 ? null : clientOrders.Max(o => o.CreatedAt));
            });
    }

    private static void ValidateNotificationPreference(
        ClientNotificationPreference preference,
        string? email,
        string? whatsApp)
    {
        if (!Enum.IsDefined(preference))
            throw new InvalidOperationException("Le canal de notification choisi est invalide.");

        if (preference == ClientNotificationPreference.Email && string.IsNullOrWhiteSpace(email))
            throw new InvalidOperationException("Une adresse e-mail est obligatoire pour choisir ce canal.");

        if (preference == ClientNotificationPreference.WhatsApp && string.IsNullOrWhiteSpace(whatsApp))
            throw new InvalidOperationException("Un numéro WhatsApp est obligatoire pour choisir ce canal.");
    }

    private static void ApplyNotificationPreference(
        Client client,
        ClientNotificationPreference preference)
    {
        if (client.NotificationPreference == preference)
            return;

        DateTime now = DateTime.Now;

        if (preference == ClientNotificationPreference.None)
        {
            if (client.NotificationPreference != ClientNotificationPreference.None)
                client.ConsentRevokedAt = now;
        }
        else
        {
            client.ConsentGivenAt = now;
            client.ConsentRevokedAt = null;
        }

        client.NotificationPreference = preference;
    }
    private static void ValidateClient(string firstName, string lastName, string phone, string whatsApp, string? email)
    {
        if (string.IsNullOrWhiteSpace(firstName)) throw new InvalidOperationException("Le prénom est obligatoire.");
        if (string.IsNullOrWhiteSpace(lastName)) throw new InvalidOperationException("Le nom est obligatoire.");
        if (string.IsNullOrWhiteSpace(phone)) throw new InvalidOperationException("Le téléphone est obligatoire.");
        if (string.IsNullOrWhiteSpace(whatsApp)) throw new InvalidOperationException("Le numéro WhatsApp est obligatoire.");
        if (string.IsNullOrWhiteSpace(PhoneNormalizer.Normalize(phone)))
        {
            throw new InvalidOperationException("Le téléphone doit contenir des chiffres.");
        }

        if (!string.IsNullOrWhiteSpace(email) && !email.Contains('@'))
        {
            throw new InvalidOperationException("L'adresse e-mail n'est pas valide.");
        }
    }

    private sealed record ClientStats(int OrderCount, decimal Balance, decimal TotalSpent, DateTime? LastOrderAt);
}
