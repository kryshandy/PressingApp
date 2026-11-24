using Microsoft.EntityFrameworkCore;
using Pressing.Core.Data;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Core.Security;

namespace Pressing.Core.Services;

public class CatalogService
{
    private readonly PressingDbContext _db;
    private readonly AuditService _audit;

    public CatalogService(PressingDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IReadOnlyList<CategoryDto>> GetAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        IQueryable<Category> query = _db.Categories.AsNoTracking().Include(c => c.Prices);
        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        var categories = await query
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(ct);

        return categories.Select(c => c.ToDto()).ToList();
    }

    public async Task<CategoryPrice?> GetPriceAsync(
        int categoryId,
        ServiceType serviceType,
        CancellationToken ct = default) =>
        await _db.CategoryPrices.AsNoTracking()
            .FirstOrDefaultAsync(p => p.CategoryId == categoryId && p.ServiceType == serviceType && p.IsActive, ct);

    public async Task<CategoryDto> CreateAsync(
        CreateCategoryRequest request,
        CurrentUser user,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new InvalidOperationException("Le nom de la catégorie est obligatoire.");
        }

        if (await _db.Categories.AnyAsync(c => c.Name.ToLower() == request.Name.Trim().ToLower(), ct))
        {
            throw new InvalidOperationException("Une catégorie porte déjà ce nom.");
        }

        var category = new Category
        {
            Name = request.Name.Trim(),
            Icon = string.IsNullOrWhiteSpace(request.Icon) ? "\uE7C1" : request.Icon,
            SortOrder = request.SortOrder,
            CreatedAt = DateTime.Now
        };

        ApplyPrices(category, request.Prices);
        _db.Categories.Add(category);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(user, "create", nameof(Category), category.Id.ToString(), category.Name, ct: ct);
        return category.ToDto();
    }

    public async Task<CategoryDto> UpdateAsync(
        int id,
        UpdateCategoryRequest request,
        CurrentUser user,
        CancellationToken ct = default)
    {
        Category category = await _db.Categories.Include(c => c.Prices)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new KeyNotFoundException("Catégorie introuvable.");

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new InvalidOperationException("Le nom de la catégorie est obligatoire.");
        }

        if (await _db.Categories.AnyAsync(c => c.Id != id && c.Name.ToLower() == request.Name.Trim().ToLower(), ct))
        {
            throw new InvalidOperationException("Une catégorie porte déjà ce nom.");
        }

        category.Name = request.Name.Trim();
        category.Icon = string.IsNullOrWhiteSpace(request.Icon) ? category.Icon : request.Icon;
        category.SortOrder = request.SortOrder;
        category.IsActive = request.IsActive;
        ApplyPrices(category, request.Prices);

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(user, "update", nameof(Category), category.Id.ToString(), category.Name, ct: ct);
        return category.ToDto();
    }

    private static void ApplyPrices(Category category, IReadOnlyList<UpdateCategoryPriceRequest> prices)
    {
        foreach (UpdateCategoryPriceRequest request in prices)
        {
            if (request.Price < 0 || request.ExpressSupplement < 0 || request.EstimatedDays < 0)
            {
                throw new InvalidOperationException("Les prix et délais doivent être positifs.");
            }

            CategoryPrice? existing = category.Prices.FirstOrDefault(p => p.ServiceType == request.ServiceType);
            if (existing is null)
            {
                category.Prices.Add(new CategoryPrice
                {
                    ServiceType = request.ServiceType,
                    Price = request.Price,
                    ExpressSupplement = request.ExpressSupplement,
                    EstimatedDays = request.EstimatedDays,
                    IsActive = request.IsActive
                });
            }
            else
            {
                existing.Price = request.Price;
                existing.ExpressSupplement = request.ExpressSupplement;
                existing.EstimatedDays = request.EstimatedDays;
                existing.IsActive = request.IsActive;
            }
        }
    }
}
