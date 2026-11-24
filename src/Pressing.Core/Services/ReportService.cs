using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Pressing.Core.Data;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;

namespace Pressing.Core.Services;

public class ReportService
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    private readonly PressingDbContext _db;

    public ReportService(PressingDbContext db)
    {
        _db = db;
    }

    public async Task<ReportDto> GetAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        DateTime start = from.Date;
        DateTime end = to.Date.AddDays(1);
        if (end <= start)
        {
            throw new InvalidOperationException("La période est invalide.");
        }

        var payments = await _db.Payments.AsNoTracking()
            .Where(p => p.PaidAt >= start && p.PaidAt < end)
            .Select(p => new { p.Amount, p.Type, p.Method, p.PaidAt, p.StaffUserId })
            .ToListAsync(ct);

        var orders = await _db.Orders.AsNoTracking()
            .Include(o => o.Client)
            .Include(o => o.Items).ThenInclude(i => i.Category)
            .Where(o => o.CreatedAt >= start && o.CreatedAt < end)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(ct);

        var allActiveOrders = await _db.Orders.AsNoTracking()
            .Where(o => o.Status != OrderStatus.Cancelled)
            .Select(o => new { o.Total, o.PaidAmount })
            .ToListAsync(ct);

        decimal revenue = payments.Where(p => p.Type == PaymentType.Payment).Sum(p => p.Amount);
        decimal refunds = payments.Where(p => p.Type == PaymentType.Refund).Sum(p => p.Amount);
        decimal outstanding = allActiveOrders.Sum(o => Math.Max(0, o.Total - o.PaidAmount));

        int newClients = await _db.Clients.CountAsync(c => c.CreatedAt >= start && c.CreatedAt < end, ct);
        int itemsProcessed = orders.Sum(o => o.Items.Sum(i => i.Quantity));

        var daily = new List<RevenuePointDto>();
        for (DateTime day = start; day < end; day = day.AddDays(1))
        {
            var dayPayments = payments.Where(p => p.PaidAt.Date == day).ToList();
            daily.Add(new RevenuePointDto(
                day.ToString("ddd dd/MM", French),
                day,
                dayPayments.Sum(p => p.Type == PaymentType.Payment ? p.Amount : -p.Amount),
                orders.Count(o => o.CreatedAt.Date == day)));
        }

        var topCategories = orders
            .SelectMany(o => o.Items)
            .GroupBy(i => i.CategoryId)
            .Select(g =>
            {
                var first = g.First();
                return new CategoryStatDto(
                    first.Category?.Name ?? $"Catégorie {g.Key}",
                    g.Sum(i => i.Quantity),
                    g.Sum(i => (i.UnitPrice + (i.IsExpress ? i.ExpressSupplement : 0)) * i.Quantity));
            })
            .OrderByDescending(c => c.Revenue)
            .Take(10)
            .ToList();

        var paymentMethods = payments
            .GroupBy(p => p.Method)
            .Select(g => new PaymentMethodStatDto(
                g.Key.ToString(),
                g.Key.Label(),
                g.Sum(p => p.Type == PaymentType.Payment ? p.Amount : -p.Amount),
                g.Count()))
            .OrderByDescending(p => p.Amount)
            .ToList();

        var staffUsers = await _db.StaffUsers.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.FullName, ct);
        var staffStats = orders
            .Where(o => o.CreatedByStaffId.HasValue)
            .GroupBy(o => o.CreatedByStaffId!.Value)
            .Select(g => new StaffStatDto(
                staffUsers.TryGetValue(g.Key, out string? name) ? name : "Inconnu",
                g.Count(),
                payments.Where(p => p.StaffUserId == g.Key).Sum(p => p.Type == PaymentType.Payment ? p.Amount : -p.Amount)))
            .OrderByDescending(s => s.Orders)
            .ToList();

        return new ReportDto(
            start,
            end.AddDays(-1),
            revenue,
            refunds,
            revenue - refunds,
            outstanding,
            orders.Count,
            orders.Count(o => o.Status == OrderStatus.Completed),
            newClients,
            itemsProcessed,
            daily,
            topCategories,
            paymentMethods,
            staffStats,
            orders.Take(50).Select(o => o.ToSummary()).ToList());
    }

    public async Task<byte[]> ExportCsvAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        ReportDto report = await GetAsync(from, to, ct);
        var builder = new StringBuilder();
        builder.AppendLine("Rapport Pressing Éclat");
        builder.AppendLine($"Période;{report.From:dd/MM/yyyy};{report.To:dd/MM/yyyy}");
        builder.AppendLine();
        builder.AppendLine("Indicateur;Valeur");
        builder.AppendLine($"Chiffre d'affaires;{report.Revenue.ToString("F2", French)}");
        builder.AppendLine($"Remboursements;{report.Refunds.ToString("F2", French)}");
        builder.AppendLine($"Chiffre d'affaires net;{report.NetRevenue.ToString("F2", French)}");
        builder.AppendLine($"Encours clients;{report.Outstanding.ToString("F2", French)}");
        builder.AppendLine($"Commandes;{report.Orders}");
        builder.AppendLine($"Commandes terminées;{report.CompletedOrders}");
        builder.AppendLine($"Nouveaux clients;{report.NewClients}");
        builder.AppendLine($"Articles traités;{report.ItemsProcessed}");
        builder.AppendLine();

        builder.AppendLine("Jour;Recettes;Commandes");
        foreach (RevenuePointDto point in report.Daily)
        {
            builder.AppendLine($"{point.Date:dd/MM/yyyy};{point.Revenue.ToString("F2", French)};{point.Orders}");
        }

        builder.AppendLine();
        builder.AppendLine("Catégorie;Quantité;Chiffre d'affaires");
        foreach (CategoryStatDto category in report.TopCategories)
        {
            builder.AppendLine($"{Escape(category.Category)};{category.Quantity};{category.Revenue.ToString("F2", French)}");
        }

        builder.AppendLine();
        builder.AppendLine("Mode de paiement;Montant;Opérations");
        foreach (PaymentMethodStatDto method in report.PaymentMethods)
        {
            builder.AppendLine($"{Escape(method.Label)};{method.Amount.ToString("F2", French)};{method.Count}");
        }

        builder.AppendLine();
        builder.AppendLine("Ticket;Date;Client;Statut;Total;Payé;Solde");
        foreach (OrderSummaryDto order in report.RecentOrders)
        {
            builder.AppendLine(
                $"{order.TicketNumber};{order.CreatedAt:dd/MM/yyyy HH:mm};{Escape(order.ClientName)};{Escape(order.StatusLabel)};{order.Total.ToString("F2", French)};{order.PaidAmount.ToString("F2", French)};{order.Balance.ToString("F2", French)}");
        }

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(builder.ToString())).ToArray();
    }

    private static string Escape(string value) => value.Replace(';', ',').Replace('\n', ' ');
}
