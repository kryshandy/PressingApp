using Microsoft.EntityFrameworkCore;
using Pressing.Core;
using Pressing.Core.Data;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Core.Reports;
using Pressing.Core.Security;
using Pressing.Tests.Support;
using ClientEntity = Pressing.Core.Entities.Client;

namespace Pressing.Tests.Unit;

/// <summary>Correctifs de la revue de code : statuts, retrait, annulation, téléphones, validations, remises, reçu, retards.</summary>
public class ReviewFixesTests : IDisposable
{
    private readonly TestDb _t = new();

    public void Dispose() => _t.Dispose();

    private async Task<OrderDto> CreateAsync(
        ClientEntity client,
        int chemises = 2,
        int pantalons = 1,
        decimal discount = 0,
        decimal deposit = 0,
        PaymentMethod depositMethod = PaymentMethod.Cash,
        string? promotionCode = null,
        DateTime? dueDate = null,
        int quantityOverride = 0,
        decimal? unitPrice = null,
        decimal deliveryFee = 0)
    {
        Category chemise = await _t.CategoryAsync("Chemise");
        Category pantalon = await _t.CategoryAsync("Pantalon");
        var items = new List<CreateOrderItemRequest>();
        if (chemises > 0)
            items.Add(new(chemise.Id, ServiceType.WashAndIron, quantityOverride != 0 ? quantityOverride : chemises, unitPrice, false, null, null, null));
        if (pantalons > 0)
            items.Add(new(pantalon.Id, ServiceType.WashAndIron, pantalons, null, false, null, null, null));

        return await _t.Orders.CreateAsync(new CreateOrderRequest(
            client.Id, items, false, dueDate, deliveryFee > 0 ? DeliveryType.HomeDelivery : DeliveryType.StorePickup,
            deliveryFee > 0 ? "Cocody" : null, deliveryFee, discount, promotionCode, 0, null,
            deposit, depositMethod, null), _t.Cashier);
    }

    private static PickupRequest Pickup(params (int ItemId, int Quantity)[] lines) =>
        new(lines.Select(l => new PickupItemRequest(l.ItemId, l.Quantity)).ToList(), 0, PaymentMethod.Cash, null, null);

    // ---------- 1. Statuts manuels ----------

    [Theory]
    [InlineData(ItemStatus.PickedUp)]
    [InlineData(ItemStatus.Cancelled)]
    public async Task Statut_PickedUp_ou_Cancelled_refuse_sur_un_article_et_sur_tous(ItemStatus status)
    {
        ClientEntity c = await _t.AddClientAsync();
        OrderDto order = await CreateAsync(c, deposit: 3500);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _t.Orders.UpdateItemStatusAsync(order.Id, order.Items[0].Id, status, _t.Cashier));
        Assert.Contains("retrait ou l'annulation", ex.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _t.Orders.UpdateAllItemsStatusAsync(order.Id, status, _t.Cashier));

        _t.Db.ChangeTracker.Clear();
        OrderDto reloaded = (await _t.Orders.GetAsync(order.Id))!;
        Assert.All(reloaded.Items, i => Assert.Equal(ItemStatus.Deposited, i.Status));
        Assert.Equal(3500m, reloaded.PaidAmount);
        Assert.Equal(OrderStatus.Deposited, reloaded.Status);
    }

    [Fact]
    public async Task Retour_Ready_vers_InProgress_autorise_tant_que_rien_n_est_retire()
    {
        ClientEntity c = await _t.AddClientAsync();
        OrderDto order = await CreateAsync(c);
        order = await _t.Orders.UpdateAllItemsStatusAsync(order.Id, ItemStatus.Ready, _t.Cashier);
        Assert.Equal(OrderStatus.Ready, order.Status);

        order = await _t.Orders.UpdateItemStatusAsync(order.Id, order.Items[0].Id, ItemStatus.InProgress, _t.Cashier);
        Assert.Equal(ItemStatus.InProgress, order.Items[0].Status);
        Assert.Equal(OrderStatus.InProgress, order.Status);
    }

    [Fact]
    public async Task Article_deja_retire_ou_partiellement_retire_ne_change_plus_de_statut()
    {
        await _t.SetSettingAsync(SettingsKeys.AllowPickupWithBalance, "true");
        ClientEntity c = await _t.AddClientAsync();
        OrderDto order = await CreateAsync(c);
        order = await _t.Orders.UpdateAllItemsStatusAsync(order.Id, ItemStatus.Ready, _t.Cashier);
        OrderItemDto chemises = order.Items.Single(i => i.CategoryName == "Chemise");
        OrderItemDto pantalon = order.Items.Single(i => i.CategoryName == "Pantalon");

        // chemises : retrait partiel (1 sur 2) ; pantalon : retrait complet.
        order = await _t.Orders.PickupAsync(order.Id, Pickup((chemises.Id, 1), (pantalon.Id, 1)), _t.Cashier);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _t.Orders.UpdateItemStatusAsync(order.Id, chemises.Id, ItemStatus.InProgress, _t.Cashier));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _t.Orders.UpdateItemStatusAsync(order.Id, pantalon.Id, ItemStatus.Ready, _t.Cashier));

        // La route « tous les articles » ignore silencieusement les articles verrouillés.
        order = await _t.Orders.UpdateAllItemsStatusAsync(order.Id, ItemStatus.InProgress, _t.Cashier);
        Assert.Equal(ItemStatus.Ready, order.Items.Single(i => i.Id == chemises.Id).Status);
        Assert.Equal(ItemStatus.PickedUp, order.Items.Single(i => i.Id == pantalon.Id).Status);
        Assert.Equal(1, order.Items.Single(i => i.Id == chemises.Id).PickedUpQuantity);
    }

    // ---------- 2. Retrait : regroupement des lignes ----------

    [Fact]
    public async Task Retrait_additionne_les_lignes_d_un_meme_article()
    {
        await _t.SetSettingAsync(SettingsKeys.AllowPickupWithBalance, "true");
        ClientEntity c = await _t.AddClientAsync();
        OrderDto order = await CreateAsync(c, chemises: 2, pantalons: 0);
        order = await _t.Orders.UpdateAllItemsStatusAsync(order.Id, ItemStatus.Ready, _t.Cashier);
        int id = order.Items[0].Id;

        // 2 + 1 = 3 > restant (2) : refusé, rien n'est retiré.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _t.Orders.PickupAsync(order.Id, Pickup((id, 2), (id, 1)), _t.Cashier));
        _t.Db.ChangeTracker.Clear();
        Assert.Equal(0, (await _t.Orders.GetAsync(order.Id))!.Items[0].PickedUpQuantity);

        // 1 + 1 = 2 : accepté, une seule ligne de retrait.
        order = await _t.Orders.PickupAsync(order.Id, Pickup((id, 1), (id, 1)), _t.Cashier);
        Assert.Equal(2, order.Items[0].PickedUpQuantity);
        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.Single(order.Pickups.Single().Items);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Retrait_refuse_une_quantite_nulle_ou_negative_meme_compensee_par_une_autre_ligne(int quantity)
    {
        ClientEntity c = await _t.AddClientAsync();
        OrderDto order = await CreateAsync(c, chemises: 2, pantalons: 0);
        order = await _t.Orders.UpdateAllItemsStatusAsync(order.Id, ItemStatus.Ready, _t.Cashier);
        int id = order.Items[0].Id;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _t.Orders.PickupAsync(order.Id, Pickup((id, 2), (id, quantity)), _t.Cashier));
    }

    // ---------- 5. Annulation : un remboursement par mode ----------

    [Fact]
    public async Task Annulation_rembourse_chaque_mode_d_origine()
    {
        ClientEntity c = await _t.AddClientAsync();
        OrderDto order = await CreateAsync(c, deposit: 1000, depositMethod: PaymentMethod.MobileMoney);
        await _t.Payments.AddPaymentAsync(order.Id,
            new AddPaymentRequest(1500, PaymentMethod.Cash, null, null, null), _t.Cashier);
        await _t.Payments.RefundAsync(order.Id, 300, PaymentMethod.Cash, "geste", _t.Cashier);

        OrderDto o = await _t.Orders.CancelAsync(order.Id, "Erreur", _t.Cashier);

        Assert.Equal(0m, o.PaidAmount);
        var refunds = o.Payments.Where(p => p.Type == PaymentType.Refund).ToList();
        Assert.Equal(1000m, refunds.Single(p => p.Method == PaymentMethod.MobileMoney).Amount);
        // Espèces : 1500 encaissés - 300 déjà remboursés = 1200 (le geste de 300 reste distinct).
        Assert.Equal(new[] { 300m, 1200m }, refunds.Where(p => p.Method == PaymentMethod.Cash).Select(p => p.Amount).OrderBy(a => a));
        Assert.Equal(1000m + 300m + 1200m, refunds.Sum(p => p.Amount));
    }

    // ---------- 6. Téléphones ----------

    [Fact]
    public async Task Les_quatre_formats_d_un_meme_numero_sont_un_seul_client()
    {
        await _t.AddClientAsync(phone: "05 55 66 77 88");
        string[] variants = ["+225 05 55 66 77 88", "00225 0555667788", "2250555667788"];
        foreach (string phone in variants)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _t.Clients.CreateAsync(
                new CreateClientRequest("Autre", "Client", phone, phone, null, null, null, null, false), _t.Cashier));
            Assert.Contains("déjà", ex.Message);
        }

        var found = await _t.Clients.SearchAsync("+225 05 55 66 77 88", 1, 10, false);
        Assert.Single(found.Items);
    }

    [Fact]
    public async Task Demarrage_recalcule_PhoneNormalized_selon_la_regle_courante_et_reste_idempotent()
    {
        ClientEntity c = await _t.AddClientAsync(phone: "+225 05 55 66 77 88");
        c.PhoneNormalized = "+2250555667788"; // ancienne règle
        await _t.Db.SaveChangesAsync();

        for (int run = 0; run < 2; run++)
        {
            await new DatabaseInitializer(_t.Db, _t.DbOptions).InitializeAsync();
            _t.Db.ChangeTracker.Clear();
            Assert.Equal("0555667788", (await _t.Db.Clients.AsNoTracking().SingleAsync(x => x.Id == c.Id)).PhoneNormalized);
        }
    }

    // ---------- 7. Retards (tableau de bord) ----------

    [Fact]
    public async Task Commande_due_aujourd_hui_n_est_pas_en_retard_mais_celle_d_hier_oui()
    {
        ClientEntity c = await _t.AddClientAsync();
        OrderDto dueToday = await CreateAsync(c, dueDate: DateTime.Today);
        OrderDto late = await CreateAsync(c);

        Assert.Equal(0, (await _t.Dashboard.GetAsync()).OverdueOrders);

        Order entity = await _t.Db.Orders.FirstAsync(o => o.Id == late.Id);
        entity.DueDate = DateTime.Today.AddDays(-1);
        await _t.Db.SaveChangesAsync();
        Assert.Equal(1, (await _t.Dashboard.GetAsync()).OverdueOrders);

        // Une commande annulée n'est jamais en retard.
        await _t.Orders.CancelAsync(late.Id, "test", _t.Cashier);
        Assert.Equal(0, (await _t.Dashboard.GetAsync()).OverdueOrders);
        Assert.NotEqual(dueToday.Id, late.Id);
    }

    // ---------- 8. Reçu : remboursements ----------

    [Fact]
    public async Task Recu_affiche_les_remboursements_en_negatif_et_le_total_paye_net()
    {
        ClientEntity c = await _t.AddClientAsync();
        OrderDto dto = await CreateAsync(c, deposit: 2000);
        await _t.Payments.RefundAsync(dto.Id, 500, PaymentMethod.Cash, "geste", _t.Cashier);

        Order order = await _t.Orders.LoadFullAsync(dto.Id);
        Payment refund = order.Payments.Single(p => p.Type == PaymentType.Refund);
        Payment payment = order.Payments.Single(p => p.Type == PaymentType.Payment);

        string refundLine = ReceiptService.PaymentLine(refund, "FCFA");
        Assert.Contains("Remboursement", refundLine);
        Assert.Contains("-500", refundLine.Replace('−', '-'));
        Assert.DoesNotContain("Remboursement", ReceiptService.PaymentLine(payment, "FCFA"));
        Assert.Equal(1500m, ReceiptService.NetPaid(order));
        Assert.Equal(order.PaidAmount, ReceiptService.NetPaid(order));

        byte[] pdf = await _t.Receipts.GeneratePdfAsync(order);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    // ---------- 9. Validation des entrées ----------

    [Fact]
    public async Task Quantite_hors_bornes_donne_ArgumentException()
    {
        ClientEntity c = await _t.AddClientAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => CreateAsync(c, chemises: 1, quantityOverride: 1001));
        await Assert.ThrowsAsync<ArgumentException>(() => CreateAsync(c, chemises: 1, quantityOverride: -3));
        OrderDto ok = await CreateAsync(c, chemises: 1, pantalons: 0, quantityOverride: 1000);
        Assert.Equal(1000, ok.Items.Single().Quantity);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10_000_001)]
    public async Task Prix_unitaire_hors_bornes_donne_ArgumentException(int price)
    {
        ClientEntity c = await _t.AddClientAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => CreateAsync(c, chemises: 1, pantalons: 0, unitPrice: price));
    }

    [Fact]
    public async Task Frais_remise_et_acompte_hors_bornes_donnent_ArgumentException()
    {
        ClientEntity c = await _t.AddClientAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => CreateAsync(c, deliveryFee: 10_000_001));
        await Assert.ThrowsAsync<ArgumentException>(() => CreateAsync(c, discount: -1));
        await Assert.ThrowsAsync<ArgumentException>(() => CreateAsync(c, discount: 10_000_001));
        await Assert.ThrowsAsync<ArgumentException>(() => CreateAsync(c, deposit: -1));
        await Assert.ThrowsAsync<ArgumentException>(() => CreateAsync(c, deposit: 10_000_001));
        Assert.Empty(await _t.Db.Orders.ToListAsync());
    }

    [Fact]
    public async Task Paiement_et_remboursement_au_dela_du_plafond_donnent_ArgumentException()
    {
        ClientEntity c = await _t.AddClientAsync();
        OrderDto order = await CreateAsync(c, deposit: 1000);
        await Assert.ThrowsAsync<ArgumentException>(() => _t.Payments.AddPaymentAsync(order.Id,
            new AddPaymentRequest(10_000_001, PaymentMethod.Cash, null, null, null), _t.Cashier));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _t.Payments.RefundAsync(order.Id, 10_000_001, PaymentMethod.Cash, null, _t.Cashier));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _t.Payments.RefundAsync(order.Id, 0, PaymentMethod.Cash, null, _t.Cashier));
    }

    // ---------- 10. Remises et promotions ----------

    private async Task AddPromotionAsync(string code, decimal percent)
    {
        _t.Db.Promotions.Add(new Promotion
        {
            Code = code,
            Description = "test",
            Type = PromotionType.Percentage,
            Value = percent,
            ValidFrom = DateTime.Now.AddDays(-1),
            ValidTo = DateTime.Now.AddDays(30),
            IsActive = true
        });
        await _t.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Remise_manuelle_limitee_au_montant_restant_apres_promotion()
    {
        await _t.SetSettingAsync(SettingsKeys.MaxDiscountPercent, "100");
        await AddPromotionAsync("MOITIE", 50);
        ClientEntity c = await _t.AddClientAsync();

        // Base 3500, promotion 50 % = 1750 : la remise manuelle ne peut dépasser 1750.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateAsync(c, discount: 2000, promotionCode: "MOITIE"));
        Assert.Contains("remise", ex.Message, StringComparison.OrdinalIgnoreCase);

        OrderDto order = await CreateAsync(c, discount: 1750, promotionCode: "MOITIE");
        Assert.Equal(0m, order.Total);
        Assert.Equal(3500m, order.Subtotal);
        Assert.Equal(1750m, order.DiscountAmount);
        Assert.Equal(1750m, order.PromotionDiscount);
    }

    [Fact]
    public async Task Annulation_libere_une_utilisation_du_code_promotionnel_sans_descendre_sous_zero()
    {
        await AddPromotionAsync("LIBERE", 10);
        ClientEntity c = await _t.AddClientAsync();
        OrderDto order = await CreateAsync(c, promotionCode: "LIBERE");
        Assert.Equal(1, (await _t.Db.Promotions.AsNoTracking().SingleAsync(p => p.Code == "LIBERE")).UsedCount);

        await _t.Orders.CancelAsync(order.Id, "test", _t.Cashier);
        Assert.Equal(0, (await _t.Db.Promotions.AsNoTracking().SingleAsync(p => p.Code == "LIBERE")).UsedCount);

        // Compteur déjà à 0 (données anciennes) : jamais négatif.
        OrderDto second = await CreateAsync(c, promotionCode: "LIBERE");
        Promotion promotion = await _t.Db.Promotions.SingleAsync(p => p.Code == "LIBERE");
        promotion.UsedCount = 0;
        await _t.Db.SaveChangesAsync();
        await _t.Orders.CancelAsync(second.Id, "test", _t.Cashier);
        Assert.Equal(0, (await _t.Db.Promotions.AsNoTracking().SingleAsync(p => p.Code == "LIBERE")).UsedCount);
    }

    // ---------- 12. Ticket insensible à la casse ----------

    [Fact]
    public async Task Suivi_accepte_un_ticket_en_minuscules_avec_espaces()
    {
        ClientEntity c = await _t.AddClientAsync(phone: "+225 07 99 88 77 66");
        OrderDto order = await CreateAsync(c);

        TrackingDto? tracking = await _t.Orders.TrackAsync($"  {order.TicketNumber.ToLowerInvariant()}  ", "0799887766");
        Assert.NotNull(tracking);
        Assert.Equal(order.TicketNumber, tracking!.TicketNumber);
    }
}
