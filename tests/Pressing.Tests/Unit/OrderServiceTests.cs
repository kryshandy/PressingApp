using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Tests.Support;
using ClientEntity = Pressing.Core.Entities.Client;

namespace Pressing.Tests.Unit;

/// <summary>Logique métier des commandes, paiements et retraits sur une base SQLite temporaire.</summary>
public class OrderServiceTests : IDisposable
{
    // Tarifs du catalogue seed (WashAndIron) : Chemise 1000 (express +400), Pantalon 1500 (express +600).
    private readonly TestDb _t = new();

    public void Dispose() => _t.Dispose();

    private async Task<OrderDto> CreateAsync(
        ClientEntity client,
        int chemises = 2,
        int pantalons = 1,
        bool express = false,
        DeliveryType delivery = DeliveryType.StorePickup,
        decimal deliveryFee = 0,
        decimal discount = 0,
        decimal deposit = 0)
    {
        Category chemise = await _t.CategoryAsync("Chemise");
        Category pantalon = await _t.CategoryAsync("Pantalon");
        var items = new List<CreateOrderItemRequest>();
        if (chemises > 0)
            items.Add(new(chemise.Id, ServiceType.WashAndIron, chemises, null, false, null, null, null));
        if (pantalons > 0)
            items.Add(new(pantalon.Id, ServiceType.WashAndIron, pantalons, null, false, null, null, null));

        return await _t.Orders.CreateAsync(new CreateOrderRequest(
            client.Id, items, express, null, delivery,
            delivery == DeliveryType.HomeDelivery ? "Cocody" : null,
            deliveryFee, discount, null, 0, null, deposit, PaymentMethod.Cash, null), _t.Cashier);
    }

    private async Task<OrderDto> ReadyAsync(OrderDto order) =>
        await _t.Orders.UpdateAllItemsStatusAsync(order.Id, ItemStatus.Ready, _t.Cashier);

    // ---------- Calcul du total ----------

    [Fact]
    public async Task Total_simple_est_la_somme_des_lignes()
    {
        ClientEntity c = await _t.AddClientAsync();
        OrderDto order = await CreateAsync(c);

        Assert.Equal(3500m, order.Subtotal); // 2 x 1000 + 1 x 1500
        Assert.Equal(0m, order.ExpressTotal);
        Assert.Equal(3500m, order.Total);
        Assert.Equal(0m, order.PaidAmount);
        Assert.Equal(3500m, order.Balance);
        Assert.Equal(OrderStatus.Deposited, order.Status);
        Assert.Equal(2, order.Items.Count);
        Assert.All(order.Items, i => Assert.Equal(ItemStatus.Deposited, i.Status));
    }

    [Fact]
    public async Task Total_combine_express_livraison_et_remise()
    {
        ClientEntity c = await _t.AddClientAsync();
        OrderDto order = await CreateAsync(
            c, express: true, delivery: DeliveryType.HomeDelivery, deliveryFee: 500, discount: 300);

        // sous-total 3500 + express (2 x 400 + 600 = 1400) + livraison 500 - remise 300
        Assert.Equal(3500m, order.Subtotal);
        Assert.Equal(1400m, order.ExpressTotal);
        Assert.Equal(500m, order.DeliveryFee);
        Assert.Equal(300m, order.DiscountAmount);
        Assert.Equal(5100m, order.Total);
        Assert.True(order.IsExpress);
        Assert.Equal(5100m, order.Balance);
    }

    [Fact]
    public async Task Frais_de_livraison_ignores_en_retrait_boutique()
    {
        ClientEntity c = await _t.AddClientAsync();
        OrderDto order = await CreateAsync(c, delivery: DeliveryType.StorePickup, deliveryFee: 800);

        Assert.Equal(0m, order.DeliveryFee);
        Assert.Equal(3500m, order.Total);
    }

    [Fact]
    public async Task Devis_calcule_le_meme_total_que_la_commande()
    {
        ClientEntity c = await _t.AddClientAsync();
        Category chemise = await _t.CategoryAsync("Chemise");
        var items = new List<CreateOrderItemRequest>
        {
            new(chemise.Id, ServiceType.WashAndIron, 3, null, false, null, null, null)
        };

        OrderQuoteDto quote = await _t.Orders.QuoteAsync(new OrderQuoteRequest(
            items, true, DeliveryType.HomeDelivery, 700, 200, null, 0, c.Id));

        Assert.Equal(3000m, quote.Subtotal);
        Assert.Equal(1200m, quote.ExpressTotal);
        Assert.Equal(700m, quote.DeliveryFee);
        Assert.Equal(3000m + 1200m + 700m - 200m, quote.Total);
    }

    [Fact]
    public async Task Remise_manuelle_au_dela_du_maximum_est_refusee()
    {
        ClientEntity c = await _t.AddClientAsync();
        // maximum par défaut 30 % de 3500 = 1050
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateAsync(c, discount: 1200));
        Assert.Contains("remise", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Commande_vide_ou_quantite_nulle_est_refusee()
    {
        ClientEntity c = await _t.AddClientAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateAsync(c, chemises: 0, pantalons: 0));

        Category chemise = await _t.CategoryAsync("Chemise");
        await Assert.ThrowsAsync<ArgumentException>(() => _t.Orders.CreateAsync(new CreateOrderRequest(
            c.Id,
            [new CreateOrderItemRequest(chemise.Id, ServiceType.WashAndIron, 0, null, false, null, null, null)],
            false, null, DeliveryType.StorePickup, null, 0, 0, null, 0, null, 0, PaymentMethod.Cash, null), _t.Cashier));
    }

    [Fact]
    public async Task Client_inconnu_donne_KeyNotFound()
    {
        Category chemise = await _t.CategoryAsync("Chemise");
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _t.Orders.CreateAsync(new CreateOrderRequest(
            9999,
            [new CreateOrderItemRequest(chemise.Id, ServiceType.WashAndIron, 1, null, false, null, null, null)],
            false, null, DeliveryType.StorePickup, null, 0, 0, null, 0, null, 0, PaymentMethod.Cash, null), _t.Cashier));
    }

    // ---------- Ticket ----------

    [Fact]
    public async Task Ticket_au_format_PRESS_annee_id_et_unique()
    {
        ClientEntity c = await _t.AddClientAsync();
        OrderDto a = await CreateAsync(c);
        OrderDto b = await CreateAsync(c);
        OrderDto d = await CreateAsync(c);

        foreach (OrderDto o in new[] { a, b, d })
        {
            Assert.Matches(new Regex(@"^PRESS-\d{4}-\d{5}$"), o.TicketNumber);
            Assert.Equal($"PRESS-{o.CreatedAt:yyyy}-{o.Id:00000}", o.TicketNumber);
        }

        Assert.Equal(3, new[] { a.TicketNumber, b.TicketNumber, d.TicketNumber }.Distinct().Count());
    }

    // ---------- Paiements ----------

    [Fact]
    public async Task Acompte_puis_paiement_partiel_mettent_le_solde_a_jour()
    {
        ClientEntity c = await _t.AddClientAsync();
        OrderDto order = await CreateAsync(c, deposit: 1000);

        Assert.Equal(1000m, order.PaidAmount);
        Assert.Equal(2500m, order.Balance);
        Assert.Single(order.Payments);
        Assert.Equal(PaymentType.Payment, order.Payments[0].Type);

        await _t.Payments.AddPaymentAsync(order.Id,
            new AddPaymentRequest(500, PaymentMethod.MobileMoney, "REF-1", null, null), _t.Cashier);

        OrderDto reloaded = (await _t.Orders.GetAsync(order.Id))!;
        Assert.Equal(1500m, reloaded.PaidAmount);
        Assert.Equal(2000m, reloaded.Balance);
        Assert.Equal(2, reloaded.Payments.Count);
    }

    [Fact]
    public async Task Paiement_du_solde_complet_met_le_solde_a_zero()
    {
        ClientEntity c = await _t.AddClientAsync();
        OrderDto order = await CreateAsync(c, deposit: 1000);

        await _t.Payments.AddPaymentAsync(order.Id,
            new AddPaymentRequest(2500, PaymentMethod.Cash, null, null, null), _t.Cashier);

        OrderDto reloaded = (await _t.Orders.GetAsync(order.Id))!;
        Assert.Equal(3500m, reloaded.PaidAmount);
        Assert.Equal(0m, reloaded.Balance);
    }

    [Fact]
    public async Task Trop_percu_est_refuse_et_rien_n_est_enregistre()
    {
        ClientEntity c = await _t.AddClientAsync();
        OrderDto order = await CreateAsync(c, deposit: 1000);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _t.Payments.AddPaymentAsync(order.Id,
            new AddPaymentRequest(2501, PaymentMethod.Cash, null, null, null), _t.Cashier));
        Assert.Contains("solde", ex.Message, StringComparison.OrdinalIgnoreCase);

        OrderDto reloaded = (await _t.Orders.GetAsync(order.Id))!;
        Assert.Equal(1000m, reloaded.PaidAmount);
        Assert.Single(reloaded.Payments);
    }

    [Fact]
    public async Task Acompte_superieur_au_total_est_refuse()
    {
        ClientEntity c = await _t.AddClientAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateAsync(c, deposit: 4000));
        Assert.Empty(await _t.Db.Orders.ToListAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public async Task Montant_de_paiement_non_positif_est_refuse(int amount)
    {
        ClientEntity c = await _t.AddClientAsync();
        OrderDto order = await CreateAsync(c);
        await Assert.ThrowsAsync<ArgumentException>(() => _t.Payments.AddPaymentAsync(order.Id,
            new AddPaymentRequest(amount, PaymentMethod.Cash, null, null, null), _t.Cashier));
    }

    [Fact]
    public async Task Remboursement_diminue_le_montant_paye_et_ne_peut_depasser_l_encaisse()
    {
        ClientEntity c = await _t.AddClientAsync();
        OrderDto order = await CreateAsync(c, deposit: 1000);

        PaymentDto refund = await _t.Payments.RefundAsync(order.Id, 400, PaymentMethod.Cash, "geste", _t.Cashier);
        Assert.Equal(PaymentType.Refund, refund.Type);
        Assert.Equal(600m, (await _t.Orders.GetAsync(order.Id))!.PaidAmount);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _t.Payments.RefundAsync(order.Id, 700, PaymentMethod.Cash, null, _t.Cashier));
    }

    // ---------- Statuts ----------

    [Fact]
    public async Task Statut_de_commande_suit_les_statuts_des_articles()
    {
        ClientEntity c = await _t.AddClientAsync();
        OrderDto order = await CreateAsync(c);
        int first = order.Items[0].Id;
        int second = order.Items[1].Id;

        OrderDto o = await _t.Orders.UpdateItemStatusAsync(order.Id, first, ItemStatus.InProgress, _t.Cashier);
        Assert.Equal(OrderStatus.InProgress, o.Status);

        o = await _t.Orders.UpdateItemStatusAsync(order.Id, first, ItemStatus.Ready, _t.Cashier);
        Assert.Equal(OrderStatus.InProgress, o.Status); // un seul article prêt sur deux

        o = await _t.Orders.UpdateItemStatusAsync(order.Id, second, ItemStatus.Ready, _t.Cashier);
        Assert.Equal(OrderStatus.Ready, o.Status);
        Assert.NotNull(o.ReadyNotifiedAt);
    }

    [Fact]
    public async Task Tout_passer_en_cours_puis_pret()
    {
        ClientEntity c = await _t.AddClientAsync();
        OrderDto order = await CreateAsync(c);

        OrderDto o = await _t.Orders.UpdateAllItemsStatusAsync(order.Id, ItemStatus.InProgress, _t.Cashier);
        Assert.Equal(OrderStatus.InProgress, o.Status);
        Assert.All(o.Items, i => Assert.Equal(ItemStatus.InProgress, i.Status));

        o = await _t.Orders.UpdateAllItemsStatusAsync(order.Id, ItemStatus.Ready, _t.Cashier);
        Assert.Equal(OrderStatus.Ready, o.Status);
    }

    [Fact]
    public async Task Article_inconnu_donne_KeyNotFound()
    {
        ClientEntity c = await _t.AddClientAsync();
        OrderDto order = await CreateAsync(c);
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _t.Orders.UpdateItemStatusAsync(order.Id, 99999, ItemStatus.Ready, _t.Cashier));
    }

    // ---------- Retrait ----------

    [Fact]
    public async Task Retrait_partiel_puis_total_termine_la_commande()
    {
        ClientEntity c = await _t.AddClientAsync();
        OrderDto order = await CreateAsync(c, deposit: 1000);
        order = await ReadyAsync(order);
        OrderItemDto chemises = order.Items.Single(i => i.CategoryName == "Chemise");
        OrderItemDto pantalon = order.Items.Single(i => i.CategoryName == "Pantalon");

        // Premier retrait : le pantalon, avec règlement du solde au comptoir.
        OrderDto o = await _t.Orders.PickupAsync(order.Id, new PickupRequest(
            [new PickupItemRequest(pantalon.Id, 1)], 2500, PaymentMethod.Cash, null, null), _t.Cashier);

        Assert.Equal(OrderStatus.PartiallyPickedUp, o.Status);
        Assert.Equal(0m, o.Balance);
        Assert.Equal(ItemStatus.PickedUp, o.Items.Single(i => i.Id == pantalon.Id).Status);
        Assert.Single(o.Pickups);

        // Second retrait : les deux chemises.
        o = await _t.Orders.PickupAsync(order.Id, new PickupRequest(
            [new PickupItemRequest(chemises.Id, 2)], 0, PaymentMethod.Cash, null, null), _t.Cashier);

        Assert.Equal(OrderStatus.Completed, o.Status);
        Assert.NotNull(o.CompletedAt);
        Assert.Equal(2, o.Pickups.Count);
        Assert.All(o.Items, i => Assert.Equal(0, i.RemainingQuantity));
    }

    [Fact]
    public async Task Retrait_d_une_partie_de_la_quantite_garde_l_article_pret()
    {
        ClientEntity c = await _t.AddClientAsync();
        OrderDto order = await CreateAsync(c, deposit: 3500);
        order = await ReadyAsync(order);
        OrderItemDto chemises = order.Items.Single(i => i.CategoryName == "Chemise");

        OrderDto o = await _t.Orders.PickupAsync(order.Id, new PickupRequest(
            [new PickupItemRequest(chemises.Id, 1)], 0, PaymentMethod.Cash, null, null), _t.Cashier);

        OrderItemDto item = o.Items.Single(i => i.Id == chemises.Id);
        Assert.Equal(1, item.PickedUpQuantity);
        Assert.Equal(1, item.RemainingQuantity);
        Assert.Equal(ItemStatus.Ready, item.Status);
        Assert.NotEqual(OrderStatus.Completed, o.Status);
    }

    [Fact]
    public async Task Retrait_avec_solde_est_refuse_par_defaut()
    {
        ClientEntity c = await _t.AddClientAsync();
        OrderDto order = await CreateAsync(c, deposit: 1000);
        order = await ReadyAsync(order);

        Assert.False(await _t.Settings.GetBoolAsync(SettingsKeys.AllowPickupWithBalance, true));

        var all = order.Items.Select(i => new PickupItemRequest(i.Id, i.Quantity)).ToList();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _t.Orders.PickupAsync(
            order.Id, new PickupRequest(all, 0, PaymentMethod.Cash, null, null), _t.Cashier));
        Assert.Contains("solde", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Aucun effet de bord : ni retrait ni encaissement conservés.
        _t.Db.ChangeTracker.Clear();
        OrderDto reloaded = (await _t.Orders.GetAsync(order.Id))!;
        Assert.Equal(OrderStatus.Ready, reloaded.Status);
        Assert.Empty(reloaded.Pickups);
        Assert.Equal(1000m, reloaded.PaidAmount);
    }

    [Fact]
    public async Task Retrait_avec_solde_autorise_quand_le_parametre_est_active()
    {
        await _t.SetSettingAsync(SettingsKeys.AllowPickupWithBalance, "true");
        ClientEntity c = await _t.AddClientAsync();
        OrderDto order = await CreateAsync(c, deposit: 1000);
        order = await ReadyAsync(order);

        var all = order.Items.Select(i => new PickupItemRequest(i.Id, i.Quantity)).ToList();
        OrderDto o = await _t.Orders.PickupAsync(
            order.Id, new PickupRequest(all, 0, PaymentMethod.Cash, null, null), _t.Cashier);

        Assert.Equal(OrderStatus.Completed, o.Status);
        Assert.Equal(2500m, o.Balance); // le solde reste dû
    }

    [Fact]
    public async Task Retrait_d_un_article_non_pret_est_refuse()
    {
        ClientEntity c = await _t.AddClientAsync();
        OrderDto order = await CreateAsync(c, deposit: 3500);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _t.Orders.PickupAsync(
            order.Id,
            new PickupRequest([new PickupItemRequest(order.Items[0].Id, 1)], 0, PaymentMethod.Cash, null, null),
            _t.Cashier));
        Assert.Contains("prêt", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Retrait_d_une_quantite_superieure_au_restant_est_refuse()
    {
        ClientEntity c = await _t.AddClientAsync();
        OrderDto order = await CreateAsync(c, deposit: 3500);
        order = await ReadyAsync(order);
        OrderItemDto chemises = order.Items.Single(i => i.CategoryName == "Chemise");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _t.Orders.PickupAsync(
            order.Id,
            new PickupRequest([new PickupItemRequest(chemises.Id, 3)], 0, PaymentMethod.Cash, null, null),
            _t.Cashier));
    }

    // ---------- Annulation ----------

    [Fact]
    public async Task Annulation_rembourse_l_encaisse_et_annule_les_articles()
    {
        ClientEntity c = await _t.AddClientAsync();
        OrderDto order = await CreateAsync(c, deposit: 1000);

        OrderDto o = await _t.Orders.CancelAsync(order.Id, "Client injoignable", _t.Cashier);

        Assert.Equal(OrderStatus.Cancelled, o.Status);
        Assert.Equal("Client injoignable", o.CancelReason);
        Assert.NotNull(o.CancelledAt);
        Assert.Equal(0m, o.PaidAmount);
        Assert.All(o.Items, i => Assert.Equal(ItemStatus.Cancelled, i.Status));
        Assert.Contains(o.Payments, p => p.Type == PaymentType.Refund && p.Amount == 1000m);
    }

    [Fact]
    public async Task Annulation_exige_un_motif()
    {
        ClientEntity c = await _t.AddClientAsync();
        OrderDto order = await CreateAsync(c);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _t.Orders.CancelAsync(order.Id, "  ", _t.Cashier));
    }

    [Fact]
    public async Task Commande_annulee_ne_peut_plus_etre_encaissee_ni_annulee_ni_modifiee()
    {
        ClientEntity c = await _t.AddClientAsync();
        OrderDto order = await CreateAsync(c);
        await _t.Orders.CancelAsync(order.Id, "Erreur de saisie", _t.Cashier);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _t.Payments.AddPaymentAsync(order.Id,
            new AddPaymentRequest(100, PaymentMethod.Cash, null, null, null), _t.Cashier));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _t.Orders.CancelAsync(order.Id, "encore", _t.Cashier));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _t.Orders.UpdateAllItemsStatusAsync(order.Id, ItemStatus.Ready, _t.Cashier));
    }

    [Fact]
    public async Task Annulation_impossible_apres_un_retrait()
    {
        ClientEntity c = await _t.AddClientAsync();
        OrderDto order = await CreateAsync(c, deposit: 3500);
        order = await ReadyAsync(order);
        OrderItemDto pantalon = order.Items.Single(i => i.CategoryName == "Pantalon");
        await _t.Orders.PickupAsync(order.Id, new PickupRequest(
            [new PickupItemRequest(pantalon.Id, 1)], 0, PaymentMethod.Cash, null, null), _t.Cashier);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _t.Orders.CancelAsync(order.Id, "trop tard", _t.Cashier));
    }

    // ---------- Suivi et recherche ----------

    [Fact]
    public async Task Suivi_public_exige_ticket_et_telephone_corrects()
    {
        ClientEntity c = await _t.AddClientAsync(phone: "+225 07 99 88 77 66");
        OrderDto order = await CreateAsync(c);

        Assert.NotNull(await _t.Orders.TrackAsync(order.TicketNumber, "+225 07 99 88 77 66"));
        Assert.NotNull(await _t.Orders.TrackAsync(order.TicketNumber, "+2250799887766"));
        Assert.Null(await _t.Orders.TrackAsync(order.TicketNumber, "+225 01 00 00 00 00"));
        Assert.Null(await _t.Orders.TrackAsync("PRESS-2000-00000", "+225 07 99 88 77 66"));
        Assert.Null(await _t.Orders.TrackAsync(order.TicketNumber, ""));
    }

    [Fact]
    public async Task Impayes_listent_les_commandes_avec_solde_non_annulees()
    {
        ClientEntity c = await _t.AddClientAsync();
        OrderDto unpaid = await CreateAsync(c);
        OrderDto paid = await CreateAsync(c, deposit: 3500);
        OrderDto cancelled = await CreateAsync(c);
        await _t.Orders.CancelAsync(cancelled.Id, "test", _t.Cashier);

        IReadOnlyList<OrderSummaryDto> outstanding = await _t.Orders.GetOutstandingAsync();

        Assert.Contains(outstanding, o => o.Id == unpaid.Id);
        Assert.DoesNotContain(outstanding, o => o.Id == paid.Id);
        Assert.DoesNotContain(outstanding, o => o.Id == cancelled.Id);
    }
}
