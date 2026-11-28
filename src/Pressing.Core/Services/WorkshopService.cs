using Microsoft.EntityFrameworkCore;
using Pressing.Core.Data;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Core.Labels;
using Pressing.Core.Security;

namespace Pressing.Core.Services;

/// <summary>
/// Atelier : fait avancer un article d'une étape à chaque scan de son étiquette
/// (Déposé → En traitement → Prêt). Le changement de statut, la notification « commande prête » et le journal
/// passent par <see cref="OrderService.UpdateItemStatusAsync"/> : aucune règle n'est dupliquée.
/// </summary>
public class WorkshopService
{
    private readonly PressingDbContext _db;
    private readonly OrderService _orders;
    private readonly AuditService _audit;

    public WorkshopService(PressingDbContext db, OrderService orders, AuditService audit)
    {
        _db = db;
        _orders = orders;
        _audit = audit;
    }

    /// <summary>Étape suivante d'un article ; <c>null</c> si l'article ne peut plus avancer à l'atelier.</summary>
    public static ItemStatus? NextStatus(ItemStatus status) => status switch
    {
        ItemStatus.Deposited => ItemStatus.InProgress,
        ItemStatus.InProgress => ItemStatus.Ready,
        _ => null
    };

    /// <summary>Étape précédente (annulation d'un scan) ; <c>null</c> si l'article est déjà au début.</summary>
    public static ItemStatus? PreviousStatus(ItemStatus status) => status switch
    {
        ItemStatus.Ready => ItemStatus.InProgress,
        ItemStatus.InProgress => ItemStatus.Deposited,
        _ => null
    };

    public async Task<WorkshopScanResultDto> ScanAsync(string? rawCode, CurrentUser user, CancellationToken ct = default)
    {
        (ItemCode code, Order order, OrderItem item) = await ResolveAsync(rawCode, ct);

        ItemStatus previous = item.Status;
        ItemStatus next = NextStatus(previous) ?? throw new InvalidOperationException(RefusalMessage(order, item));

        OrderDto updated = await _orders.UpdateItemStatusAsync(order.Id, item.Id, next, user, ct);
        await _audit.LogAsync(user, "scan", nameof(OrderItem), item.Id.ToString(),
            $"{code.FullCode} : {previous.Label()} → {next.Label()}", ct: ct);

        return ToResult(code, order, item, previous, next, updated,
            updated.Status == OrderStatus.Ready
                ? $"{order.TicketNumber} : tous les articles sont prêts, le client est prévenu."
                : $"{item.Category.Name} : {next.Label()}.");
    }

    /// <summary>
    /// Annule le dernier scan en revenant d'<b>une seule</b> étape. N'agit que si l'article est toujours au statut
    /// obtenu par ce scan (<paramref name="fromStatus"/>) : un changement fait entre-temps n'est jamais écrasé.
    /// </summary>
    public async Task<WorkshopScanResultDto> UndoAsync(
        string? rawCode, ItemStatus fromStatus, CurrentUser user, CancellationToken ct = default)
    {
        (ItemCode code, Order order, OrderItem item) = await ResolveAsync(rawCode, ct);

        if (item.Status != fromStatus)
        {
            throw new InvalidOperationException(
                $"Annulation impossible : l'article est maintenant « {item.Status.Label()} » (et non « {fromStatus.Label()} »).");
        }

        ItemStatus previous = item.Status;
        ItemStatus back = PreviousStatus(previous)
            ?? throw new InvalidOperationException($"Rien à annuler : l'article est « {previous.Label()} ».");

        OrderDto updated = await _orders.UpdateItemStatusAsync(order.Id, item.Id, back, user, ct);
        await _audit.LogAsync(user, "scan-undo", nameof(OrderItem), item.Id.ToString(),
            $"{code.FullCode} : {previous.Label()} → {back.Label()} (annulation)", ct: ct);

        return ToResult(code, order, item, previous, back, updated,
            $"Scan annulé : {item.Category.Name} revient à « {back.Label()} ».");
    }

    private async Task<(ItemCode Code, Order Order, OrderItem Item)> ResolveAsync(string? rawCode, CancellationToken ct)
    {
        if (!ItemCodes.TryParseItemCode(rawCode, out ItemCode? code) || code is null)
        {
            throw new ArgumentException(
                "Code illisible : attendu PRESS-AAAA-NNNNN-II (ex. PRESS-2026-00042-01). Scannez l'étiquette de l'article.");
        }

        Order order = await _db.Orders.AsNoTracking()
            .Include(o => o.Client)
            .Include(o => o.Items).ThenInclude(i => i.Category)
            .FirstOrDefaultAsync(o => o.TicketNumber == code.TicketNumber, ct)
            ?? throw new KeyNotFoundException($"Aucune commande ne correspond au ticket {code.TicketNumber}.");

        OrderItem item = order.Items.OrderBy(i => i.Id).Skip(code.Rank - 1).FirstOrDefault()
            ?? throw new KeyNotFoundException($"Le ticket {code.TicketNumber} n'a pas d'article n° {code.Rank:00}.");

        if (code.Unit is not null)
        {
            int piece = ItemCodes.UnitSuffix(1) == code.Unit ? 1 : PieceIndex(code.Unit);
            if (piece > item.Quantity)
            {
                throw new KeyNotFoundException($"L'article n° {code.Rank:00} n'a que {item.Quantity} pièce(s) : étiquette inconnue.");
            }
        }

        if (!code.IsPrimary)
        {
            throw new InvalidOperationException(
                $"Étiquette de la pièce « {code.Unit} » : seule l'étiquette « {code.LineCode}-a » fait avancer cette ligne "
                + $"({item.Quantity} pièces, un seul statut). Aucun changement.");
        }

        return (code, order, item);
    }

    private static int PieceIndex(string unit)
    {
        int index = 0;
        foreach (char c in unit) index = index * 26 + (c - 'a' + 1);
        return index;
    }

    private static string RefusalMessage(Order order, OrderItem item) => item.Status switch
    {
        ItemStatus.Ready => $"Déjà prêt : {item.Category.Name} ({order.TicketNumber}) n'a plus d'étape à l'atelier.",
        ItemStatus.PickedUp => $"Déjà retiré : {item.Category.Name} ({order.TicketNumber}) a été remis au client.",
        ItemStatus.Cancelled => $"Article annulé : {item.Category.Name} ({order.TicketNumber}).",
        _ => $"Cet article ne peut pas avancer ({item.Status.Label()})."
    };

    private static WorkshopScanResultDto ToResult(
        ItemCode code, Order order, OrderItem item, ItemStatus previous, ItemStatus now, OrderDto updated, string message) =>
        new(code.FullCode, order.Id, order.TicketNumber, order.Client.FullName, item.Id, item.Category.Name,
            item.ServiceType.Label() + (item.IsExpress ? " · Express" : string.Empty), code.Rank, order.Items.Count,
            previous, now, now.Label(), updated.Status == OrderStatus.Ready, message);
}
