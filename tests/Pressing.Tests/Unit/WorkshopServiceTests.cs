using Microsoft.EntityFrameworkCore;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Core.Labels;
using Pressing.Core.Services;
using Pressing.Tests.Support;
using ClientEntity = Pressing.Core.Entities.Client;

namespace Pressing.Tests.Unit;

/// <summary>Scan d'atelier : avancement d'un cran par scan, refus, annulation et notification « prête ».</summary>
public class WorkshopServiceTests : IDisposable
{
    private readonly TestDb _t = new();
    private readonly WorkshopService _workshop;

    public WorkshopServiceTests()
    {
        _workshop = new WorkshopService(_t.Db, _t.Orders, _t.Get<AuditService>());
    }

    public void Dispose() => _t.Dispose();

    /// <summary>1 chemise (code -01) + 1 pantalon (code -02).</summary>
    private async Task<OrderDto> CreateOrderAsync(int chemises = 1)
    {
        ClientEntity client = await _t.AddClientAsync("Awa", "Koné");
        Category chemise = await _t.CategoryAsync("Chemise");
        Category pantalon = await _t.CategoryAsync("Pantalon");
        var items = new List<CreateOrderItemRequest>
        {
            new(chemise.Id, ServiceType.WashAndIron, chemises, null, false, null, null, null),
            new(pantalon.Id, ServiceType.WashAndIron, 1, null, false, null, null, null)
        };
        return await _t.Orders.CreateAsync(new CreateOrderRequest(
            client.Id, items, false, null, DeliveryType.StorePickup, null, 0, 0, null, 0, null, 0,
            PaymentMethod.Cash, null), _t.Cashier);
    }

    private async Task<ItemStatus> StatusOfAsync(int itemId) =>
        (await _t.Db.OrderItems.AsNoTracking().FirstAsync(i => i.Id == itemId)).Status;

    [Fact]
    public async Task Chaque_scan_fait_avancer_d_une_etape_puis_refuse()
    {
        OrderDto order = await CreateOrderAsync();
        string code = $"{order.TicketNumber}-02";

        WorkshopScanResultDto r = await _workshop.ScanAsync(code, _t.Cashier);
        Assert.Equal(ItemStatus.Deposited, r.PreviousStatus);
        Assert.Equal(ItemStatus.InProgress, r.NewStatus);
        Assert.Equal("En traitement", r.NewStatusLabel);
        Assert.Equal("Pantalon", r.ItemName);
        Assert.Equal("Awa Koné", r.ClientName);
        Assert.False(r.OrderReady);

        r = await _workshop.ScanAsync(code, _t.Cashier);
        Assert.Equal(ItemStatus.Ready, r.NewStatus);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _workshop.ScanAsync(code, _t.Cashier));
        Assert.Contains("Déjà prêt", ex.Message);
        Assert.Equal(ItemStatus.Ready, await StatusOfAsync(order.Items[1].Id));
    }

    [Fact]
    public async Task Article_retire_est_refuse_avec_un_message_clair()
    {
        OrderDto order = await CreateOrderAsync();
        await _t.Orders.UpdateAllItemsStatusAsync(order.Id, ItemStatus.Ready, _t.Cashier);
        await _t.Orders.PickupAsync(order.Id, new PickupRequest(
            [new PickupItemRequest(order.Items[0].Id, 1)], order.Total, PaymentMethod.Cash, null, null), _t.Cashier);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _workshop.ScanAsync($"{order.TicketNumber}-01", _t.Cashier));
        Assert.Contains("Déjà retiré", ex.Message);
    }

    [Fact]
    public async Task Tous_les_articles_prets_declenchent_la_notification_commande_prete()
    {
        OrderDto order = await CreateOrderAsync();
        string first = $"{order.TicketNumber}-01";
        string second = $"{order.TicketNumber}-02";

        // Premier article prêt : la commande n'est pas encore prête, pas de notification.
        await _workshop.ScanAsync(first, _t.Cashier);
        await _workshop.ScanAsync(first, _t.Cashier);
        Assert.Empty(await _t.Db.StaffNotifications.Where(n => n.Title == "Commande prête").ToListAsync());

        await _workshop.ScanAsync(second, _t.Cashier);
        WorkshopScanResultDto last = await _workshop.ScanAsync(second, _t.Cashier);

        Assert.True(last.OrderReady);
        Assert.Single(await _t.Db.StaffNotifications.AsNoTracking()
            .Where(n => n.Title == "Commande prête" && n.Message.StartsWith(order.TicketNumber)).ToListAsync());
        Order saved = await _t.Db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
        Assert.Equal(OrderStatus.Ready, saved.Status);
        Assert.NotNull(saved.ReadyNotifiedAt);
        Assert.Contains(await _t.Db.EmailMessages.AsNoTracking().ToListAsync(), m => m.Template == "order-ready");
    }

    [Fact]
    public async Task Annulation_recule_d_une_seule_etape()
    {
        OrderDto order = await CreateOrderAsync();
        string code = $"{order.TicketNumber}-01";
        int itemId = order.Items[0].Id;
        await _workshop.ScanAsync(code, _t.Cashier);
        await _workshop.ScanAsync(code, _t.Cashier);
        Assert.Equal(ItemStatus.Ready, await StatusOfAsync(itemId));

        WorkshopScanResultDto undo = await _workshop.UndoAsync(code, ItemStatus.Ready, _t.Cashier);
        Assert.Equal(ItemStatus.InProgress, undo.NewStatus);
        Assert.Equal(ItemStatus.InProgress, await StatusOfAsync(itemId));

        undo = await _workshop.UndoAsync(code, ItemStatus.InProgress, _t.Cashier);
        Assert.Equal(ItemStatus.Deposited, undo.NewStatus);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _workshop.UndoAsync(code, ItemStatus.Deposited, _t.Cashier));
    }

    [Fact]
    public async Task Annulation_refusee_si_le_statut_a_change_entre_temps()
    {
        OrderDto order = await CreateOrderAsync();
        string code = $"{order.TicketNumber}-01";
        await _workshop.ScanAsync(code, _t.Cashier);
        await _workshop.ScanAsync(code, _t.Cashier); // Prêt

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _workshop.UndoAsync(code, ItemStatus.InProgress, _t.Cashier));
        Assert.Contains("Annulation impossible", ex.Message);
        Assert.Equal(ItemStatus.Ready, await StatusOfAsync(order.Items[0].Id));
    }

    [Fact]
    public async Task Seule_la_piece_a_fait_avancer_une_ligne_de_plusieurs_pieces()
    {
        OrderDto order = await CreateOrderAsync(chemises: 3);
        int itemId = order.Items[0].Id;

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _workshop.ScanAsync($"{order.TicketNumber}-01-b", _t.Cashier));
        Assert.Contains("Aucun changement", ex.Message);
        Assert.Equal(ItemStatus.Deposited, await StatusOfAsync(itemId));

        WorkshopScanResultDto r = await _workshop.ScanAsync($"{order.TicketNumber}-01-a", _t.Cashier);
        Assert.Equal(ItemStatus.InProgress, r.NewStatus);

        // La pièce « d » n'existe pas : 3 pièces seulement.
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _workshop.ScanAsync($"{order.TicketNumber}-01-d", _t.Cashier));
    }

    [Fact]
    public async Task Codes_invalides_ou_inconnus()
    {
        OrderDto order = await CreateOrderAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => _workshop.ScanAsync("n'importe quoi", _t.Cashier));
        await Assert.ThrowsAsync<ArgumentException>(() => _workshop.ScanAsync(null, _t.Cashier));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _workshop.ScanAsync("PRESS-2026-99999-01", _t.Cashier));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _workshop.ScanAsync($"{order.TicketNumber}-07", _t.Cashier));
    }

    [Fact]
    public async Task Le_scan_est_journalise()
    {
        OrderDto order = await CreateOrderAsync();
        await _workshop.ScanAsync($"{order.TicketNumber}-01", _t.Cashier);

        Assert.Contains(await _t.Db.AuditLogs.AsNoTracking().ToListAsync(),
            a => a.Action == "scan" && a.Details!.Contains($"{order.TicketNumber}-01"));
    }
}
