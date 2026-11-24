using Microsoft.EntityFrameworkCore;
using Pressing.Core.Data;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Core.Security;

namespace Pressing.Core.Services;

public class PaymentService
{
    private readonly PressingDbContext _db;
    private readonly SettingsService _settings;
    private readonly NotificationService _notifications;
    private readonly AuditService _audit;

    public PaymentService(
        PressingDbContext db,
        SettingsService settings,
        NotificationService notifications,
        AuditService audit)
    {
        _db = db;
        _settings = settings;
        _notifications = notifications;
        _audit = audit;
    }

    public async Task<Payment> AddPaymentCoreAsync(
        Order order,
        decimal amount,
        PaymentMethod method,
        string? reference,
        string? note,
        DateTime? paidAt,
        CurrentUser user,
        CancellationToken ct = default)
    {
        if (amount <= 0)
        {
            throw new InvalidOperationException("Le montant doit être supérieur à zéro.");
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new InvalidOperationException("Impossible d'encaisser une commande annulée.");
        }

        decimal balance = order.Total - order.PaidAmount;
        if (amount > balance + 0.005m)
        {
            throw new InvalidOperationException(
                $"Le montant dépasse le solde restant ({balance:N0} {await GetCurrencyAsync(ct)}).");
        }

        DateTime when = paidAt ?? DateTime.Now;
        if (when > DateTime.Now.AddMinutes(5))
        {
            throw new InvalidOperationException("La date de paiement ne peut pas être dans le futur.");
        }

        var payment = new Payment
        {
            OrderId = order.Id,
            Order = order,
            ClientId = order.ClientId,
            Client = order.Client,
            Amount = amount,
            Type = PaymentType.Payment,
            Method = method,
            PaidAt = when,
            Reference = reference?.Trim(),
            Note = note?.Trim(),
            StaffUserId = user.StaffId
        };

        _db.Payments.Add(payment);
        order.PaidAmount += amount;
        order.UpdatedAt = DateTime.Now;

        await CreditLoyaltyAsync(order, amount, ct);
        return payment;
    }

    public async Task<Payment> RefundCoreAsync(
        Order order,
        decimal amount,
        PaymentMethod method,
        string? reference,
        string? note,
        CurrentUser user,
        CancellationToken ct = default)
    {
        if (amount <= 0)
        {
            throw new InvalidOperationException("Le montant du remboursement doit être supérieur à zéro.");
        }

        if (amount > order.PaidAmount + 0.005m)
        {
            throw new InvalidOperationException("Le remboursement dépasse le montant déjà encaissé.");
        }

        var payment = new Payment
        {
            OrderId = order.Id,
            Order = order,
            ClientId = order.ClientId,
            Client = order.Client,
            Amount = amount,
            Type = PaymentType.Refund,
            Method = method,
            PaidAt = DateTime.Now,
            Reference = reference?.Trim(),
            Note = note?.Trim(),
            StaffUserId = user.StaffId
        };

        _db.Payments.Add(payment);
        order.PaidAmount = Math.Max(0, order.PaidAmount - amount);
        order.UpdatedAt = DateTime.Now;

        await DebitLoyaltyAsync(order, amount, ct);
        return payment;
    }

    public async Task<PaymentDto> AddPaymentAsync(
        int orderId,
        AddPaymentRequest request,
        CurrentUser user,
        CancellationToken ct = default)
    {
        Order order = await _db.Orders
            .Include(o => o.Client)
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new KeyNotFoundException("Commande introuvable.");

        Payment payment = await AddPaymentCoreAsync(
            order,
            request.Amount,
            request.Method,
            request.Reference,
            request.Note,
            request.PaidAt,
            user,
            ct);

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(
            user,
            "payment",
            nameof(Order),
            order.Id.ToString(),
            $"{request.Amount:N0} encaissés sur {order.TicketNumber}",
            ct: ct);
        await _notifications.NotifyAsync(
            "Paiement enregistré",
            $"{order.TicketNumber} — {request.Amount:N0} {await GetCurrencyAsync(ct)}",
            NotificationKind.Success,
            $"/orders/{order.Id}",
            ct);

        payment.Order = order;
        return payment.ToDto();
    }

    public async Task<PaymentDto> RefundAsync(
        int orderId,
        decimal amount,
        PaymentMethod method,
        string? note,
        CurrentUser user,
        CancellationToken ct = default)
    {
        Order order = await _db.Orders
            .Include(o => o.Client)
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new KeyNotFoundException("Commande introuvable.");

        Payment payment = await RefundCoreAsync(order, amount, method, null, note, user, ct);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(
            user, "refund", nameof(Order), order.Id.ToString(),
            $"Remboursement de {amount:N0} sur {order.TicketNumber}", ct: ct);

        payment.Order = order;
        return payment.ToDto();
    }

    public async Task<PagedResult<PaymentDto>> GetAsync(
        DateTime? from = null,
        DateTime? to = null,
        PaymentMethod? method = null,
        string? search = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        IQueryable<Payment> query = _db.Payments.AsNoTracking()
            .Include(p => p.Order)
            .Include(p => p.StaffUser);

        if (from.HasValue) query = query.Where(p => p.PaidAt >= from.Value);
        if (to.HasValue) query = query.Where(p => p.PaidAt < to.Value.Date.AddDays(1));
        if (method.HasValue) query = query.Where(p => p.Method == method.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = $"%{search.Trim()}%";
            query = query.Where(p =>
                EF.Functions.Like(p.Order.TicketNumber, term) ||
                EF.Functions.Like(p.Reference ?? string.Empty, term) ||
                EF.Functions.Like(p.Client.FirstName, term) ||
                EF.Functions.Like(p.Client.LastName, term));
        }

        int total = await query.CountAsync(ct);
        var payments = await query
            .OrderByDescending(p => p.PaidAt)
            .Skip((Math.Max(1, page) - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<PaymentDto>(payments.Select(p => p.ToDto()).ToList(), total, page, pageSize);
    }

    private async Task CreditLoyaltyAsync(Order order, decimal amount, CancellationToken ct)
    {
        if (!await _settings.GetBoolAsync(SettingsKeys.LoyaltyEnabled, true, ct))
        {
            return;
        }

        decimal pointsPerAmount = await _settings.GetDecimalAsync(SettingsKeys.LoyaltyPointsPerAmount, 1000, ct);
        if (pointsPerAmount <= 0) return;

        int points = (int)Math.Floor(amount / pointsPerAmount);
        if (points <= 0) return;

        Client client = order.Client ?? await _db.Clients.FirstAsync(c => c.Id == order.ClientId, ct);
        client.LoyaltyPoints += points;
        _db.LoyaltyTransactions.Add(new LoyaltyTransaction
        {
            ClientId = client.Id,
            OrderId = order.Id,
            Points = points,
            Type = LoyaltyTransactionType.Earned,
            Note = $"Points gagnés — {order.TicketNumber}",
            CreatedAt = DateTime.Now
        });
    }

    private async Task DebitLoyaltyAsync(Order order, decimal amount, CancellationToken ct)
    {
        if (!await _settings.GetBoolAsync(SettingsKeys.LoyaltyEnabled, true, ct))
        {
            return;
        }

        decimal pointsPerAmount = await _settings.GetDecimalAsync(SettingsKeys.LoyaltyPointsPerAmount, 1000, ct);
        if (pointsPerAmount <= 0) return;

        int points = (int)Math.Floor(amount / pointsPerAmount);
        if (points <= 0) return;

        Client client = order.Client ?? await _db.Clients.FirstAsync(c => c.Id == order.ClientId, ct);
        int applied = Math.Min(points, client.LoyaltyPoints);
        if (applied <= 0) return;

        client.LoyaltyPoints -= applied;
        _db.LoyaltyTransactions.Add(new LoyaltyTransaction
        {
            ClientId = client.Id,
            OrderId = order.Id,
            Points = -applied,
            Type = LoyaltyTransactionType.Adjusted,
            Note = $"Ajustement après remboursement — {order.TicketNumber}",
            CreatedAt = DateTime.Now
        });
    }

    private async Task<string> GetCurrencyAsync(CancellationToken ct) =>
        await _settings.GetAsync(SettingsKeys.Currency, "FCFA", ct);
}
