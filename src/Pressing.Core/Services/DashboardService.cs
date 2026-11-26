using Microsoft.EntityFrameworkCore;
using Pressing.Core.Data;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;

namespace Pressing.Core.Services;

public class DashboardService
{
    private readonly PressingDbContext _db;
    private readonly NotificationService _notifications;

    public DashboardService(PressingDbContext db, NotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    public async Task<DashboardDto> GetAsync(CancellationToken ct = default)
    {
        DateTime now = DateTime.Now;
        DateTime today = now.Date;
        DateTime monthStart = new(now.Year, now.Month, 1);
        DateTime chartStart = today.AddDays(-6);
        DateTime paymentsFrom = monthStart < chartStart ? monthStart : chartStart;

        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.PaidAt >= paymentsFrom)
            .Select(p => new { p.Amount, p.Type, p.PaidAt })
            .ToListAsync(ct);

        decimal revenueToday = payments
            .Where(p => p.PaidAt >= today)
            .Sum(p => p.Type == PaymentType.Payment ? p.Amount : -p.Amount);
        decimal revenueMonth = payments
            .Where(p => p.PaidAt >= monthStart)
            .Sum(p => p.Type == PaymentType.Payment ? p.Amount : -p.Amount);

        var orders = await _db.Orders.AsNoTracking()
            .Select(o => new { o.Status, o.DueDate, o.Total, o.PaidAmount, o.CreatedAt, o.ClientId })
            .ToListAsync(ct);

        int ordersToday = orders.Count(o => o.CreatedAt >= today);
        int activeOrders = orders.Count(o => o.Status is not (OrderStatus.Completed or OrderStatus.Cancelled));
        int readyOrders = orders.Count(o => o.Status == OrderStatus.Ready);
        int overdueOrders = orders.Count(o =>
            o.Status is not (OrderStatus.Completed or OrderStatus.Cancelled) && o.DueDate.Date < today);
        decimal outstanding = orders
            .Where(o => o.Status != OrderStatus.Cancelled)
            .Sum(o => Math.Max(0, o.Total - o.PaidAmount));

        int newClients = await _db.Clients.CountAsync(c => c.CreatedAt >= monthStart, ct);
        int pendingEmails = await _db.EmailMessages.CountAsync(
            m => m.Status == EmailStatus.Pending || m.Status == EmailStatus.Failed, ct);

        var recent = await _db.Orders.AsNoTracking()
            .Include(o => o.Client)
            .Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAt)
            .Take(8)
            .ToListAsync(ct);

        var ready = await _db.Orders.AsNoTracking()
            .Include(o => o.Client)
            .Include(o => o.Items)
            .Where(o => o.Status == OrderStatus.Ready || o.Status == OrderStatus.PartiallyPickedUp)
            .OrderBy(o => o.DueDate)
            .Take(6)
            .ToListAsync(ct);

        var daily = Enumerable.Range(0, 7)
            .Select(offset => today.AddDays(offset - 6))
            .Select(day =>
            {
                var dayPayments = payments.Where(p => p.PaidAt.Date == day).ToList();
                return new RevenuePointDto(
                    day.ToString("ddd dd/MM"),
                    day,
                    dayPayments.Sum(p => p.Type == PaymentType.Payment ? p.Amount : -p.Amount),
                    orders.Count(o => o.CreatedAt.Date == day));
            })
            .ToList();

        var statuses = new[]
        {
            OrderStatus.Deposited,
            OrderStatus.InProgress,
            OrderStatus.Ready,
            OrderStatus.PartiallyPickedUp,
            OrderStatus.Completed,
            OrderStatus.Cancelled
        };

        string[] colors =
        [
            "#3b82f6", "#f59e0b", "#10b981", "#8b5cf6", "#64748b", "#ef4444"
        ];

        var breakdown = statuses
            .Select((status, index) => new StatusCountDto(
                status.ToString(), status.Label(), orders.Count(o => o.Status == status), colors[index]))
            .ToList();

        var itemStats = await _db.OrderItems.AsNoTracking()
            .Include(i => i.Category)
            .Include(i => i.Order)
            .Where(i => i.Order.CreatedAt >= monthStart && i.Order.Status != OrderStatus.Cancelled)
            .Select(i => new { i.Category.Name, i.Quantity, i.UnitPrice, i.ExpressSupplement, i.IsExpress })
            .ToListAsync(ct);

        var topCategories = itemStats
            .GroupBy(i => i.Name)
            .Select(g => new CategoryStatDto(
                g.Key,
                g.Sum(i => i.Quantity),
                g.Sum(i => (i.UnitPrice + (i.IsExpress ? i.ExpressSupplement : 0)) * i.Quantity)))
            .OrderByDescending(c => c.Revenue)
            .Take(5)
            .ToList();

        var notifications = await _notifications.GetAsync(6, ct);

        return new DashboardDto(
            ordersToday,
            revenueToday,
            revenueMonth,
            activeOrders,
            readyOrders,
            overdueOrders,
            outstanding,
            newClients,
            pendingEmails,
            recent.Select(o => o.ToSummary()).ToList(),
            ready.Select(o => o.ToSummary()).ToList(),
            daily,
            breakdown,
            topCategories,
            notifications);
    }
}
