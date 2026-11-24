using Microsoft.EntityFrameworkCore;
using Pressing.Core.Data;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Core.Security;

namespace Pressing.Core.Services;

public class AuditService
{
    private readonly PressingDbContext _db;

    public AuditService(PressingDbContext db)
    {
        _db = db;
    }

    public async Task LogAsync(
        CurrentUser user,
        string action,
        string entityType,
        string? entityId = null,
        string? details = null,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Actor = string.IsNullOrWhiteSpace(user.Name) ? "Système" : user.Name,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            IpAddress = ipAddress,
            CreatedAt = DateTime.Now
        });

        await _db.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<AuditLogDto>> GetAsync(
        int page = 1,
        int pageSize = 50,
        string? search = null,
        CancellationToken ct = default)
    {
        IQueryable<AuditLog> query = _db.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = $"%{search.Trim()}%";
            query = query.Where(a =>
                EF.Functions.Like(a.Actor, term) ||
                EF.Functions.Like(a.Action, term) ||
                EF.Functions.Like(a.EntityType, term) ||
                EF.Functions.Like(a.Details ?? string.Empty, term));
        }

        int total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((Math.Max(1, page) - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditLogDto(
                a.Id, a.Actor, a.Action, a.EntityType, a.EntityId, a.Details, a.IpAddress, a.CreatedAt))
            .ToListAsync(ct);

        return new PagedResult<AuditLogDto>(items, total, page, pageSize);
    }
}
