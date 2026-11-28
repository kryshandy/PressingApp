using Microsoft.EntityFrameworkCore;
using Pressing.Core;
using Pressing.Core.Assistant;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Core.Services;
using Pressing.Tests.Support;
using ClientEntity = Pressing.Core.Entities.Client;

namespace Pressing.Tests.Unit;

/// <summary>
/// AssistantService : intention → brouillon validé, sur une base SQLite temporaire avec le catalogue seed
/// (Chemise 1000 FCFA, express +400 ; Pantalon 1500 FCFA, express +600 ; deux « Costume … pièces »).
/// </summary>
public class AssistantServiceTests : IDisposable
{
    private readonly TestDb _t = new();

    public void Dispose() => _t.Dispose();

    // ---------- Outils ----------

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private AssistantService Service(TimeProvider? time = null) =>
        new(_t.Db, new CatalogService(_t.Db, new AuditService(_t.Db)), _t.Orders, time);

    private async Task<OrderDto> OrderAsync(ClientEntity client, int chemises = 2, decimal deposit = 0)
    {
        Category chemise = await _t.CategoryAsync("Chemise");
        var items = new List<CreateOrderItemRequest>
        {
            new(chemise.Id, ServiceType.WashAndIron, chemises, null, false, null, null, null)
        };
        return await _t.Orders.CreateAsync(new CreateOrderRequest(
            client.Id, items, false, null, DeliveryType.StorePickup, null, 0, 0, null, 0, null,
            deposit, PaymentMethod.Cash, null), _t.Cashier);
    }

    private static AssistantIntentDto Create(
        string? name = null, string? phone = null, IReadOnlyList<AssistantItemDto>? items = null,
        decimal? deposit = null, DateOnly? pickup = null, double confidence = 0.9) =>
        new(Intent: AssistantIntents.CreateOrder, Client: new AssistantClientDto(name, phone), Items: items,
            Ticket: null, Amount: null, PaymentMethod: null, Deposit: deposit, PickupDate: pickup,
            ItemStatus: null, Confidence: confidence, Transcript: "test");

    private static AssistantIntentDto ForTicket(
        string intent, string? ticket, decimal? amount = null, string? method = null,
        string? status = null, string? clientName = null) =>
        new(Intent: intent, Client: clientName is null ? null : new AssistantClientDto(clientName, null),
            Items: null, Ticket: ticket, Amount: amount, PaymentMethod: method, Deposit: null, PickupDate: null,
            ItemStatus: status, Confidence: 0.9, Transcript: "test");

    private static AssistantItemDto Item(string category, string service, int quantity = 1, bool express = false) =>
        new(category, service, quantity, express);

    // ---------- Compréhension ----------

    [Fact]
    public async Task Confiance_sous_le_seuil_donne_je_n_ai_pas_compris()
    {
        OrderDraftDto draft = await Service().BuildDraftAsync(Create("Ngo", items: [Item("Chemise", "lavage repassage")], confidence: 0.4));

        Assert.Equal(AssistantTarget.NotUnderstood, draft.Target);
        Assert.Empty(draft.Items);
        Assert.Null(draft.Client);
        Assert.Contains(draft.Warnings, w => w.Contains("pas compris"));
    }

    [Fact]
    public async Task Intention_inconnue_ou_invalide_donne_je_n_ai_pas_compris()
    {
        var unknown = Create() with { Intent = AssistantIntents.Unknown };
        var invalid = Create() with { Intent = "tout_supprimer" };

        Assert.Equal(AssistantTarget.NotUnderstood, (await Service().BuildDraftAsync(unknown)).Target);
        OrderDraftDto draft = await Service().BuildDraftAsync(invalid);
        Assert.Equal(AssistantTarget.NotUnderstood, draft.Target);
        Assert.Equal(AssistantIntents.Unknown, draft.Intent);
    }

    // ---------- Client ----------

    [Fact]
    public async Task Client_trouve_par_telephone_normalise()
    {
        ClientEntity c = await _t.AddClientAsync("Marie", "Ngo", "+225 07 00 11 22 33");

        OrderDraftDto draft = await Service().BuildDraftAsync(Create(phone: "07-00-11-22-33", items: [Item("Chemise", "lavage repassage")]));

        Assert.Equal(c.Id, draft.Client!.ClientId);
        Assert.False(draft.Client.IsNew);
        Assert.DoesNotContain("client", draft.UncertainFields);
    }

    [Fact]
    public async Task Client_trouve_par_nom_approche_sans_civilite()
    {
        ClientEntity c = await _t.AddClientAsync("Marie", "Ngo", "0700000001");

        OrderDraftDto draft = await Service().BuildDraftAsync(Create("Mme Ngo", items: [Item("Chemise", "lavage repassage")]));

        Assert.Equal(c.Id, draft.Client!.ClientId);
        Assert.Equal("Marie Ngo", draft.Client.FullName);
    }

    [Fact]
    public async Task Homonymes_sont_proposes_sans_choisir()
    {
        await _t.AddClientAsync("Marie", "Ngo", "0700000001");
        await _t.AddClientAsync("Paul", "Ngo", "0700000002");

        OrderDraftDto draft = await Service().BuildDraftAsync(Create("Mme Ngo", items: [Item("Chemise", "lavage repassage")]));

        Assert.Null(draft.Client!.ClientId);
        Assert.False(draft.Client.IsNew);
        Assert.Equal(2, draft.Client.Candidates.Count);
        Assert.Contains("client", draft.UncertainFields);
        Assert.Contains(draft.Warnings, w => w.Contains("Plusieurs clients"));
    }

    [Fact]
    public async Task Client_inconnu_propose_la_creation()
    {
        OrderDraftDto draft = await Service().BuildDraftAsync(Create("Mme Zorro", "0799999999", [Item("Chemise", "lavage repassage")]));

        Assert.Null(draft.Client!.ClientId);
        Assert.True(draft.Client.IsNew);
        Assert.Contains("client", draft.UncertainFields);
        Assert.Contains(draft.Warnings, w => w.Contains("Client inconnu : création proposée"));
    }

    [Fact]
    public async Task Telephone_et_nom_contradictoires_sont_signales()
    {
        await _t.AddClientAsync("Marie", "Ngo", "0700000001");

        OrderDraftDto draft = await Service().BuildDraftAsync(Create("Awa Koné", "0700000001", [Item("Chemise", "lavage repassage")]));

        Assert.NotNull(draft.Client!.ClientId);
        Assert.Contains("client", draft.UncertainFields);
        Assert.Contains(draft.Warnings, w => w.Contains("correspond à Marie Ngo"));
    }

    // ---------- Articles, catalogue et prix ----------

    [Fact]
    public async Task Articles_rapproches_du_catalogue_avec_les_prix_du_catalogue()
    {
        await _t.AddClientAsync("Marie", "Ngo", "0700000001");

        OrderDraftDto draft = await Service().BuildDraftAsync(Create("Ngo", items:
            [Item("chemises", "Lavage + repassage", 3), Item("pantalon", "lavage et repassage", 2)]));

        Assert.Equal(AssistantTarget.NewOrder, draft.Target);
        Assert.Equal(2, draft.Items.Count);
        Assert.Equal("Chemise", draft.Items[0].CategoryName);
        Assert.Equal(ServiceType.WashAndIron, draft.Items[0].ServiceType);
        Assert.Equal(1000m, draft.Items[0].UnitPrice);
        Assert.Equal(3000m, draft.Items[0].LineTotal);
        Assert.Equal(3000m, draft.Items[1].LineTotal); // 2 x 1500
        Assert.All(draft.Items, i => Assert.False(i.IsUncertain));
        Assert.Equal(6000m, draft.Quote!.Total);
        Assert.Empty(draft.Warnings);
    }

    [Fact]
    public async Task Express_ajoute_le_supplement_du_catalogue()
    {
        await _t.AddClientAsync("Marie", "Ngo", "0700000001");

        OrderDraftDto draft = await Service().BuildDraftAsync(Create("Ngo", items: [Item("Chemise", "lavage repassage", 1, express: true)]));

        Assert.True(draft.Items[0].IsExpress);
        Assert.Equal(1400m, draft.Items[0].LineTotal); // 1000 + 400 d'express
        Assert.Equal(400m, draft.Quote!.ExpressTotal);
        Assert.Equal(1400m, draft.Quote.Total);
    }

    [Fact]
    public async Task Categorie_absente_du_catalogue_n_est_pas_devinee()
    {
        await _t.AddClientAsync("Marie", "Ngo", "0700000001");

        OrderDraftDto draft = await Service().BuildDraftAsync(Create("Ngo", items: [Item("hélicoptère", "lavage")]));

        Assert.Null(draft.Items[0].CategoryId);
        Assert.True(draft.Items[0].IsUncertain);
        Assert.Contains("items[0].category", draft.UncertainFields);
        Assert.Contains(draft.Warnings, w => w.Contains("n'existe pas au catalogue"));
        Assert.Null(draft.Quote);
    }

    [Fact]
    public async Task Categorie_ambigue_est_signalee()
    {
        await _t.AddClientAsync("Marie", "Ngo", "0700000001");

        OrderDraftDto draft = await Service().BuildDraftAsync(Create("Ngo", items: [Item("costume", "nettoyage à sec")]));

        Assert.Null(draft.Items[0].CategoryId);
        Assert.Contains(draft.Warnings, w => w.Contains("plusieurs catégories"));
        Assert.Contains("items[0].category", draft.UncertainFields);
    }

    [Fact]
    public async Task Service_inconnu_est_signale_et_non_chiffre()
    {
        await _t.AddClientAsync("Marie", "Ngo", "0700000001");

        OrderDraftDto draft = await Service().BuildDraftAsync(Create("Ngo", items: [Item("Chemise", "teinture")]));

        Assert.Equal("Chemise", draft.Items[0].CategoryName);
        Assert.Null(draft.Items[0].ServiceType);
        Assert.Null(draft.Items[0].UnitPrice);
        Assert.Contains("items[0].service", draft.UncertainFields);
        Assert.Contains(draft.Warnings, w => w.Contains("non reconnu"));
        Assert.Null(draft.Quote);
    }

    [Fact]
    public async Task Quantite_invalide_est_ramenee_dans_les_bornes_et_signalee()
    {
        await _t.AddClientAsync("Marie", "Ngo", "0700000001");

        OrderDraftDto draft = await Service().BuildDraftAsync(Create("Ngo", items: [Item("Chemise", "lavage repassage", 0)]));

        Assert.Equal(1, draft.Items[0].Quantity);
        Assert.Contains("items[0].quantity", draft.UncertainFields);
    }

    [Fact]
    public async Task Total_partiel_si_un_article_n_est_pas_reconnu()
    {
        await _t.AddClientAsync("Marie", "Ngo", "0700000001");

        OrderDraftDto draft = await Service().BuildDraftAsync(Create("Ngo", items:
            [Item("Chemise", "lavage repassage", 2), Item("hélicoptère", "lavage")]));

        Assert.Equal(2000m, draft.Quote!.Total);
        Assert.Contains(draft.Warnings, w => w.Contains("ne comprend que les articles reconnus"));
    }

    // ---------- Acompte et date ----------

    [Fact]
    public async Task Acompte_superieur_au_total_est_signale()
    {
        await _t.AddClientAsync("Marie", "Ngo", "0700000001");

        OrderDraftDto draft = await Service().BuildDraftAsync(Create("Ngo", items: [Item("Chemise", "lavage repassage")], deposit: 5000m));

        Assert.Equal(5000m, draft.DepositAmount);
        Assert.Contains("deposit", draft.UncertainFields);
        Assert.Contains(draft.Warnings, w => w.Contains("dépasse le total"));
    }

    [Fact]
    public async Task Acompte_negatif_est_refuse()
    {
        await _t.AddClientAsync("Marie", "Ngo", "0700000001");

        OrderDraftDto draft = await Service().BuildDraftAsync(Create("Ngo", items: [Item("Chemise", "lavage repassage")], deposit: -100m));

        Assert.Null(draft.DepositAmount);
        Assert.Contains("deposit", draft.UncertainFields);
    }

    [Fact]
    public async Task Date_de_retrait_passee_est_refusee_et_future_acceptee()
    {
        await _t.AddClientAsync("Marie", "Ngo", "0700000001");
        var time = new FixedTime(new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero));
        var items = new[] { Item("Chemise", "lavage repassage") };

        OrderDraftDto past = await Service(time).BuildDraftAsync(Create("Ngo", items: items, pickup: new DateOnly(2026, 11, 20)));
        OrderDraftDto future = await Service(time).BuildDraftAsync(Create("Ngo", items: items, pickup: new DateOnly(2026, 12, 2)));

        Assert.Null(past.DueDate);
        Assert.Contains("dueDate", past.UncertainFields);
        Assert.Equal(new DateTime(2026, 12, 2), future.DueDate);
        Assert.DoesNotContain("dueDate", future.UncertainFields);
    }

    // ---------- Ticket, paiement, retrait, statut ----------

    [Fact]
    public async Task Paiement_normal_sur_un_ticket_existant()
    {
        ClientEntity c = await _t.AddClientAsync("Marie", "Ngo", "0700000001");
        OrderDto order = await OrderAsync(c);

        OrderDraftDto draft = await Service().BuildDraftAsync(ForTicket(AssistantIntents.AddPayment, order.TicketNumber, 1500m, "card"));

        Assert.Equal(AssistantTarget.Payment, draft.Target);
        Assert.Equal(order.Id, draft.OrderId);
        Assert.Equal(order.TicketNumber, draft.Ticket);
        Assert.Equal(2000m, draft.Balance);
        Assert.Equal(1500m, draft.PaymentAmount);
        Assert.Equal(PaymentMethod.Card, draft.PaymentMethod);
        Assert.Equal(c.Id, draft.Client!.ClientId);
        Assert.Empty(draft.Warnings);
    }

    [Fact]
    public async Task Montant_superieur_au_solde_est_signale()
    {
        ClientEntity c = await _t.AddClientAsync("Marie", "Ngo", "0700000001");
        OrderDto order = await OrderAsync(c);

        OrderDraftDto draft = await Service().BuildDraftAsync(ForTicket(AssistantIntents.AddPayment, order.TicketNumber, 9000m, "cash"));

        Assert.Equal(9000m, draft.PaymentAmount);
        Assert.Contains("amount", draft.UncertainFields);
        Assert.Contains(draft.Warnings, w => w.Contains("dépasse le solde"));
    }

    [Fact]
    public async Task Commande_deja_soldee_est_signalee()
    {
        ClientEntity c = await _t.AddClientAsync("Marie", "Ngo", "0700000001");
        OrderDto order = await OrderAsync(c, deposit: 2000m);

        OrderDraftDto draft = await Service().BuildDraftAsync(ForTicket(AssistantIntents.AddPayment, order.TicketNumber, 500m, "cash"));

        Assert.Equal(0m, draft.Balance);
        Assert.Contains(draft.Warnings, w => w.Contains("déjà soldée"));
    }

    [Fact]
    public async Task Montant_manquant_ou_mode_inconnu_sont_signales()
    {
        ClientEntity c = await _t.AddClientAsync("Marie", "Ngo", "0700000001");
        OrderDto order = await OrderAsync(c);

        OrderDraftDto noAmount = await Service().BuildDraftAsync(ForTicket(AssistantIntents.AddPayment, order.TicketNumber, null, "cash"));
        OrderDraftDto badMethod = await Service().BuildDraftAsync(ForTicket(AssistantIntents.AddPayment, order.TicketNumber, 500m, "bitcoin"));

        Assert.Null(noAmount.PaymentAmount);
        Assert.Contains("amount", noAmount.UncertainFields);
        Assert.Null(badMethod.PaymentMethod);
        Assert.Contains("paymentMethod", badMethod.UncertainFields);
    }

    [Fact]
    public async Task Ticket_inexistant_est_signale()
    {
        OrderDraftDto draft = await Service().BuildDraftAsync(ForTicket(AssistantIntents.AddPayment, "PRESS-2026-99999", 500m, "cash"));

        Assert.Null(draft.OrderId);
        Assert.Null(draft.Ticket);
        Assert.Contains("ticket", draft.UncertainFields);
        Assert.Contains(draft.Warnings, w => w.Contains("Ticket inexistant"));
    }

    [Fact]
    public async Task Ticket_non_precise_ou_illisible_est_signale()
    {
        OrderDraftDto none = await Service().BuildDraftAsync(ForTicket(AssistantIntents.RegisterPickup, null));
        OrderDraftDto unreadable = await Service().BuildDraftAsync(ForTicket(AssistantIntents.RegisterPickup, "le ticket de Marie"));

        Assert.Contains(none.Warnings, w => w.Contains("Ticket non précisé"));
        Assert.Contains(unreadable.Warnings, w => w.Contains("Ticket illisible"));
    }

    [Fact]
    public async Task Numero_court_retrouve_le_ticket_complet()
    {
        ClientEntity c = await _t.AddClientAsync("Marie", "Ngo", "0700000001");
        OrderDto order = await OrderAsync(c);

        // Le ticket est PRESS-AAAA-NNNNN avec NNNNN = identifiant de la commande.
        OrderDraftDto draft = await Service().BuildDraftAsync(ForTicket(AssistantIntents.FindOrder, order.Id.ToString()));

        Assert.Equal(AssistantTarget.FindOrder, draft.Target);
        Assert.Equal(order.Id, draft.OrderId);
        Assert.Equal(order.TicketNumber, draft.Ticket);
    }

    [Fact]
    public async Task Ticket_d_un_autre_client_est_signale()
    {
        ClientEntity marie = await _t.AddClientAsync("Marie", "Ngo", "0700000001");
        await _t.AddClientAsync("Awa", "Koné", "0700000002");
        OrderDto order = await OrderAsync(marie);

        OrderDraftDto draft = await Service().BuildDraftAsync(
            ForTicket(AssistantIntents.AddPayment, order.TicketNumber, 500m, "cash", clientName: "Awa Koné"));

        Assert.Equal(marie.Id, draft.Client!.ClientId);
        Assert.Contains("client", draft.UncertainFields);
        Assert.Contains(draft.Warnings, w => w.Contains("appartient à Marie Ngo"));
    }

    [Fact]
    public async Task Retrait_sans_article_pret_puis_avec_article_pret()
    {
        ClientEntity c = await _t.AddClientAsync("Marie", "Ngo", "0700000001");
        OrderDto order = await OrderAsync(c, deposit: 500m);

        OrderDraftDto before = await Service().BuildDraftAsync(ForTicket(AssistantIntents.RegisterPickup, order.TicketNumber));
        Assert.Equal(AssistantTarget.Pickup, before.Target);
        Assert.Contains(before.Warnings, w => w.Contains("Aucun article prêt"));

        await _t.Orders.UpdateAllItemsStatusAsync(order.Id, ItemStatus.Ready, _t.Cashier);
        OrderDraftDto after = await Service().BuildDraftAsync(ForTicket(AssistantIntents.RegisterPickup, order.TicketNumber));

        Assert.DoesNotContain(after.Warnings, w => w.Contains("Aucun article prêt"));
        Assert.Contains(after.Warnings, w => w.Contains("Solde restant à payer : 1 500 FCFA"));
    }

    [Fact]
    public async Task Changement_de_statut_deja_applique_ou_non_reconnu_est_signale()
    {
        ClientEntity c = await _t.AddClientAsync("Marie", "Ngo", "0700000001");
        OrderDto order = await OrderAsync(c);
        await _t.Orders.UpdateAllItemsStatusAsync(order.Id, ItemStatus.Ready, _t.Cashier);

        OrderDraftDto already = await Service().BuildDraftAsync(ForTicket(AssistantIntents.UpdateItemStatus, order.TicketNumber, status: "ready"));
        OrderDraftDto invalid = await Service().BuildDraftAsync(ForTicket(AssistantIntents.UpdateItemStatus, order.TicketNumber, status: "picked_up"));

        Assert.Equal(AssistantTarget.UpdateStatus, already.Target);
        Assert.Equal(ItemStatus.Ready, already.ItemStatus);
        Assert.Contains(already.Warnings, w => w.Contains("déjà « Prêt »"));
        Assert.Null(invalid.ItemStatus);
        Assert.Contains("itemStatus", invalid.UncertainFields);
    }

    // ---------- Principe non négociable : lecture seule ----------

    [Fact]
    public async Task L_assistant_ne_cree_ni_commande_ni_paiement_ni_audit()
    {
        ClientEntity c = await _t.AddClientAsync("Marie", "Ngo", "0700000001");
        OrderDto order = await OrderAsync(c);
        int orders = await _t.Db.Orders.CountAsync();
        int payments = await _t.Db.Payments.CountAsync();
        int clients = await _t.Db.Clients.CountAsync();
        int audits = await _t.Db.AuditLogs.CountAsync();

        await Service().BuildDraftAsync(Create("Marie Ngo", items: [Item("Chemise", "lavage repassage", 3)], deposit: 1000m));
        await Service().BuildDraftAsync(Create("Mme Inconnue", items: [Item("Chemise", "lavage repassage")]));
        await Service().BuildDraftAsync(ForTicket(AssistantIntents.AddPayment, order.TicketNumber, 500m, "cash"));
        await Service().BuildDraftAsync(ForTicket(AssistantIntents.UpdateItemStatus, order.TicketNumber, status: "ready"));

        Assert.Equal(orders, await _t.Db.Orders.CountAsync());
        Assert.Equal(payments, await _t.Db.Payments.CountAsync());
        Assert.Equal(clients, await _t.Db.Clients.CountAsync());
        Assert.Equal(audits, await _t.Db.AuditLogs.CountAsync());
        Assert.All((await _t.Db.Orders.Include(o => o.Items).ToListAsync()).SelectMany(o => o.Items),
            i => Assert.Equal(ItemStatus.Deposited, i.Status));
    }
}
