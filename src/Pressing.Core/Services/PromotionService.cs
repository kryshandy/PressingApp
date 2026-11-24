using Microsoft.EntityFrameworkCore;
using Pressing.Core.Data;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Core.Security;

namespace Pressing.Core.Services;

public class PromotionService
{
    private readonly PressingDbContext _db;
    private readonly AuditService _audit;

    public PromotionService(PressingDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<PromotionDto>> GetAsync(bool includeInactive = true, CancellationToken ct = default)
    {
        IQueryable<Promotion> query = _db.Promotions.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(p => p.IsActive);
        }

        return await query
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => p.ToDto())
            .ToListAsync(ct);
    }

    public async Task<PromotionDto> CreateAsync(
        CreatePromotionRequest request,
        CurrentUser user,
        CancellationToken ct = default)
    {
        Validate(request.Code, request.Value, request.ValidFrom, request.ValidTo);
        string code = request.Code.Trim().ToUpperInvariant();
        if (await _db.Promotions.AnyAsync(p => p.Code == code, ct))
        {
            throw new InvalidOperationException("Ce code promotionnel existe déjà.");
        }

        var promotion = new Promotion
        {
            Code = code,
            Description = request.Description?.Trim() ?? string.Empty,
            Type = request.Type,
            Value = request.Value,
            MinOrderAmount = request.MinOrderAmount,
            ValidFrom = request.ValidFrom,
            ValidTo = request.ValidTo,
            MaxUses = request.MaxUses,
            IsActive = request.IsActive,
            CreatedAt = DateTime.Now
        };

        _db.Promotions.Add(promotion);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(user, "create", nameof(Promotion), promotion.Id.ToString(), promotion.Code, ct: ct);
        return promotion.ToDto();
    }

    public async Task<PromotionDto> UpdateAsync(
        int id,
        UpdatePromotionRequest request,
        CurrentUser user,
        CancellationToken ct = default)
    {
        Validate(request.Code, request.Value, request.ValidFrom, request.ValidTo);
        Promotion promotion = await _db.Promotions.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new KeyNotFoundException("Promotion introuvable.");

        string code = request.Code.Trim().ToUpperInvariant();
        if (await _db.Promotions.AnyAsync(p => p.Id != id && p.Code == code, ct))
        {
            throw new InvalidOperationException("Ce code promotionnel existe déjà.");
        }

        promotion.Code = code;
        promotion.Description = request.Description?.Trim() ?? string.Empty;
        promotion.Type = request.Type;
        promotion.Value = request.Value;
        promotion.MinOrderAmount = request.MinOrderAmount;
        promotion.ValidFrom = request.ValidFrom;
        promotion.ValidTo = request.ValidTo;
        promotion.MaxUses = request.MaxUses;
        promotion.IsActive = request.IsActive;

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(user, "update", nameof(Promotion), promotion.Id.ToString(), promotion.Code, ct: ct);
        return promotion.ToDto();
    }

    public async Task<PromotionValidationDto> ValidateAsync(
        string? code,
        decimal subtotal,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return new PromotionValidationDto(false, "Aucun code saisi.", 0, null);
        }

        string normalized = code.Trim().ToUpperInvariant();
        Promotion? promotion = await _db.Promotions.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == normalized, ct);

        if (promotion is null)
        {
            return new PromotionValidationDto(false, "Code promotionnel inconnu.", 0, null);
        }

        DateTime now = DateTime.Now;
        if (!promotion.IsActive)
        {
            return new PromotionValidationDto(false, "Ce code n'est plus actif.", 0, promotion.ToDto());
        }

        if (now < promotion.ValidFrom || now > promotion.ValidTo)
        {
            return new PromotionValidationDto(false, "Ce code n'est pas valide à cette date.", 0, promotion.ToDto());
        }

        if (promotion.MaxUses > 0 && promotion.UsedCount >= promotion.MaxUses)
        {
            return new PromotionValidationDto(false, "Ce code a atteint sa limite d'utilisation.", 0, promotion.ToDto());
        }

        if (subtotal < promotion.MinOrderAmount)
        {
            return new PromotionValidationDto(
                false,
                $"Montant minimum requis : {promotion.MinOrderAmount:N0}.",
                0,
                promotion.ToDto());
        }

        decimal discount = promotion.Type == PromotionType.Percentage
            ? Math.Round(subtotal * promotion.Value / 100m, 0, MidpointRounding.AwayFromZero)
            : promotion.Value;

        discount = Math.Min(Math.Max(0, discount), subtotal);
        return new PromotionValidationDto(true, $"Code appliqué : -{discount:N0}.", discount, promotion.ToDto());
    }

    public async Task<Promotion?> GetEntityAsync(string code, CancellationToken ct = default)
    {
        string normalized = code.Trim().ToUpperInvariant();
        return await _db.Promotions.FirstOrDefaultAsync(p => p.Code == normalized, ct);
    }

    public async Task IncrementUseAsync(Promotion promotion, CancellationToken ct = default)
    {
        promotion.UsedCount++;
        await _db.SaveChangesAsync(ct);
    }

    private static void Validate(string code, decimal value, DateTime from, DateTime to)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("Le code est obligatoire.");
        }

        if (value <= 0)
        {
            throw new InvalidOperationException("La valeur de la promotion doit être positive.");
        }

        if (to <= from)
        {
            throw new InvalidOperationException("La date de fin doit être postérieure à la date de début.");
        }
    }
}
