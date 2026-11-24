using Microsoft.EntityFrameworkCore;
using Pressing.Core.Data;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;

namespace Pressing.Core.Services;

public class NotificationService
{
    private readonly PressingDbContext _db;

    public NotificationService(PressingDbContext db)
    {
        _db = db;
    }

    public async Task NotifyAsync(
        string title,
        string message,
        NotificationKind kind = NotificationKind.Info,
        string? link = null,
        CancellationToken ct = default)
    {
        _db.StaffNotifications.Add(new StaffNotification
        {
            Title = title,
            Message = message,
            Kind = kind,
            Link = link,
            CreatedAt = DateTime.Now
        });

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<NotificationDto>> GetAsync(int take = 20, CancellationToken ct = default)
    {
        return await _db.StaffNotifications.AsNoTracking()
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .Select(n => new NotificationDto(
                n.Id, n.Title, n.Message, n.Kind, n.Kind.ToString(), n.Link, n.IsRead, n.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<int> GetUnreadCountAsync(CancellationToken ct = default) =>
        await _db.StaffNotifications.CountAsync(n => !n.IsRead, ct);

    public async Task MarkReadAsync(int id, CancellationToken ct = default)
    {
        StaffNotification? notification = await _db.StaffNotifications.FindAsync([id], ct);
        if (notification is null) return;
        notification.IsRead = true;
        await _db.SaveChangesAsync(ct);
    }

    public async Task MarkAllReadAsync(CancellationToken ct = default)
    {
        await _db.StaffNotifications
            .Where(n => !n.IsRead)
            .ExecuteUpdateAsync(setter => setter.SetProperty(n => n.IsRead, true), ct);
    }
}
