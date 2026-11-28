using Microsoft.EntityFrameworkCore;
using Pressing.Core.Data;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Core.Options;
using Pressing.Core.Reports;
using Pressing.Core.Security;

namespace Pressing.Core.Services;

/// <summary>
/// Clôture de caisse journalière (« Z de caisse ») : calcul des montants attendus depuis les paiements et
/// remboursements du jour, saisie des espèces comptées, validation par un manager. Une clôture validée est figée.
/// </summary>
public class CashClosingService
{
    private readonly PressingDbContext _db;
    private readonly AuditService _audit;
    private readonly SettingsService _settings;

    public CashClosingService(PressingDbContext db, AuditService audit, SettingsService settings)
    {
        _db = db;
        _audit = audit;
        _settings = settings;
    }

    /// <summary>Calcule les montants attendus du jour sans rien enregistrer.</summary>
    public async Task<CashClosingPreviewDto> PreviewAsync(DateTime date, CancellationToken ct = default)
    {
        DateTime day = date.Date;
        Expected expected = await ComputeExpectedAsync(day, ct);

        CashClosing? existing = await _db.CashClosings.AsNoTracking()
            .Where(c => c.BusinessDate == day)
            .OrderByDescending(c => c.Status)
            .ThenByDescending(c => c.Id)
            .FirstOrDefaultAsync(ct);

        return new CashClosingPreviewDto(
            day,
            expected.Cash, expected.MobileMoney, expected.Card, expected.Transfer, expected.Other,
            expected.Total,
            expected.PaymentsCount, expected.RefundsCount,
            existing?.Id, existing?.Status);
    }

    public async Task<CashClosingDto> CreateAsync(CashClosingRequest request, CurrentUser user, CancellationToken ct = default)
    {
        if (!user.StaffId.HasValue)
        {
            throw new InvalidOperationException("Seul le personnel peut enregistrer une clôture de caisse.");
        }

        DateTime day = request.Date.Date;
        ValidateInputs(request, day);

        CashClosing? existing = await _db.CashClosings
            .Where(c => c.BusinessDate == day)
            .OrderByDescending(c => c.Status)
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            throw new InvalidOperationException(existing.Status == CashClosingStatus.Validated
                ? $"La caisse du {day:dd/MM/yyyy} est déjà clôturée et validée."
                : $"Une clôture en brouillon existe déjà pour le {day:dd/MM/yyyy} : modifiez-la ou faites-la valider.");
        }

        Expected expected = await ComputeExpectedAsync(day, ct);
        var closing = new CashClosing
        {
            BusinessDate = day,
            Status = CashClosingStatus.Draft,
            CreatedByStaffId = user.StaffId,
            CreatedByName = string.IsNullOrWhiteSpace(user.Name) ? "Inconnu" : user.Name,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        Apply(closing, request, expected);

        _db.CashClosings.Add(closing);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(
            user, "cash-closing-create", nameof(CashClosing), closing.Id.ToString(),
            Describe(closing), ct: ct);
        return closing.ToDto();
    }

    /// <summary>Modifie un brouillon (jamais une clôture validée) ; les montants attendus sont recalculés.</summary>
    public async Task<CashClosingDto> UpdateAsync(int id, CashClosingRequest request, CurrentUser user, CancellationToken ct = default)
    {
        CashClosing closing = await FindAsync(id, ct);
        if (closing.Status == CashClosingStatus.Validated)
        {
            throw new InvalidOperationException("Une clôture validée n'est plus modifiable.");
        }

        if (request.Date.Date != closing.BusinessDate)
        {
            throw new InvalidOperationException("La date d'une clôture ne peut pas être modifiée.");
        }

        ValidateInputs(request, closing.BusinessDate);
        Expected expected = await ComputeExpectedAsync(closing.BusinessDate, ct);
        Apply(closing, request, expected);
        closing.UpdatedAt = DateTime.Now;

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(
            user, "cash-closing-update", nameof(CashClosing), closing.Id.ToString(),
            Describe(closing), ct: ct);
        return closing.ToDto();
    }

    /// <summary>Validation par un manager ou un administrateur : les attendus sont recalculés puis la clôture est figée.</summary>
    public async Task<CashClosingDto> ValidateAsync(int id, CurrentUser user, CancellationToken ct = default)
    {
        if (!user.StaffId.HasValue || (user.Role != nameof(UserRole.Manager) && user.Role != nameof(UserRole.Admin)))
        {
            throw new InvalidOperationException("Seul un manager ou un administrateur peut valider une clôture de caisse.");
        }

        CashClosing closing = await FindAsync(id, ct);
        if (closing.Status == CashClosingStatus.Validated)
        {
            throw new InvalidOperationException("Cette clôture est déjà validée.");
        }

        bool otherValidated = await _db.CashClosings.AnyAsync(
            c => c.BusinessDate == closing.BusinessDate && c.Status == CashClosingStatus.Validated && c.Id != closing.Id, ct);
        if (otherValidated)
        {
            throw new InvalidOperationException($"La caisse du {closing.BusinessDate:dd/MM/yyyy} est déjà clôturée et validée.");
        }

        // Des opérations ont pu être saisies depuis le brouillon : on fige les attendus à jour.
        Expected expected = await ComputeExpectedAsync(closing.BusinessDate, ct);
        ApplyExpected(closing, expected);
        RequireCommentIfVariance(closing.Variance, closing.Comment);

        closing.Status = CashClosingStatus.Validated;
        closing.ValidatedByStaffId = user.StaffId;
        closing.ValidatedByName = string.IsNullOrWhiteSpace(user.Name) ? "Inconnu" : user.Name;
        closing.ValidatedAt = DateTime.Now;
        closing.UpdatedAt = DateTime.Now;

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(
            user, "cash-closing-validate", nameof(CashClosing), closing.Id.ToString(),
            Describe(closing), ct: ct);
        return closing.ToDto();
    }

    public async Task<CashClosingDto> GetAsync(int id, CancellationToken ct = default) =>
        (await FindAsync(id, ct, track: false)).ToDto();

    public async Task<IReadOnlyList<CashClosingDto>> ListAsync(DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        DateTime end = (to ?? DateTime.Today).Date;
        DateTime start = (from ?? end.AddDays(-89)).Date;
        if (end < start)
        {
            throw new InvalidOperationException("La période est invalide.");
        }

        List<CashClosing> items = await _db.CashClosings.AsNoTracking()
            .Where(c => c.BusinessDate >= start && c.BusinessDate <= end)
            .OrderByDescending(c => c.BusinessDate)
            .ThenByDescending(c => c.Id)
            .ToListAsync(ct);
        return items.Select(c => c.ToDto()).ToList();
    }

    public async Task<(byte[] Bytes, string FileName)> GetPdfAsync(int id, CancellationToken ct = default)
    {
        CashClosing closing = await FindAsync(id, ct, track: false);
        ShopInfo shop = await _settings.GetShopInfoAsync(ct);
        return (CashClosingPdf.Generate(closing, shop), $"cloture-caisse-{closing.BusinessDate:yyyyMMdd}.pdf");
    }

    // ------------------------------------------------------------------ interne

    private async Task<CashClosing> FindAsync(int id, CancellationToken ct, bool track = true)
    {
        IQueryable<CashClosing> query = track ? _db.CashClosings : _db.CashClosings.AsNoTracking();
        return await query.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException("Clôture de caisse introuvable.");
    }

    private static void ValidateInputs(CashClosingRequest request, DateTime day)
    {
        if (day > DateTime.Today)
        {
            throw new InvalidOperationException("On ne peut pas clôturer la caisse d'un jour futur.");
        }

        InputLimits.RequireAmountInRange(request.OpeningFloat, "Le fond de caisse");
        InputLimits.RequireAmountInRange(request.CountedCash, "Le montant d'espèces compté");
        if (request.Comment is { Length: > 500 })
        {
            throw new ArgumentException("Le commentaire ne peut pas dépasser 500 caractères.");
        }
    }

    private static void Apply(CashClosing closing, CashClosingRequest request, Expected expected)
    {
        closing.OpeningFloat = request.OpeningFloat;
        closing.CountedCash = request.CountedCash;
        closing.Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        ApplyExpected(closing, expected);
        RequireCommentIfVariance(closing.Variance, closing.Comment);
    }

    private static void ApplyExpected(CashClosing closing, Expected expected)
    {
        closing.ExpectedCash = expected.Cash;
        closing.ExpectedMobileMoney = expected.MobileMoney;
        closing.ExpectedCard = expected.Card;
        closing.ExpectedTransfer = expected.Transfer;
        closing.ExpectedOther = expected.Other;
        closing.Variance = Math.Round(closing.CountedCash - closing.ExpectedCashInDrawer, 2, MidpointRounding.AwayFromZero);
    }

    private static void RequireCommentIfVariance(decimal variance, string? comment)
    {
        if (variance != 0 && string.IsNullOrWhiteSpace(comment))
        {
            throw new InvalidOperationException(
                $"Un écart de {variance:N0} a été constaté : un commentaire expliquant l'écart est obligatoire.");
        }
    }

    private static string Describe(CashClosing c) =>
        $"Caisse du {c.BusinessDate:dd/MM/yyyy} : attendu en tiroir {c.ExpectedCashInDrawer:N0}, compté {c.CountedCash:N0}, écart {c.Variance:N0}";

    private async Task<Expected> ComputeExpectedAsync(DateTime day, CancellationToken ct)
    {
        DateTime end = day.AddDays(1);
        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.PaidAt >= day && p.PaidAt < end)
            .Select(p => new { p.Amount, p.Type, p.Method })
            .ToListAsync(ct);

        decimal Net(PaymentMethod method) => payments
            .Where(p => p.Method == method)
            .Sum(p => p.Type == PaymentType.Refund ? -p.Amount : p.Amount);

        return new Expected(
            Net(PaymentMethod.Cash),
            Net(PaymentMethod.MobileMoney),
            Net(PaymentMethod.Card),
            Net(PaymentMethod.BankTransfer),
            Net(PaymentMethod.Other),
            payments.Count(p => p.Type == PaymentType.Payment),
            payments.Count(p => p.Type == PaymentType.Refund));
    }

    private sealed record Expected(
        decimal Cash, decimal MobileMoney, decimal Card, decimal Transfer, decimal Other,
        int PaymentsCount, int RefundsCount)
    {
        public decimal Total => Cash + MobileMoney + Card + Transfer + Other;
    }
}
