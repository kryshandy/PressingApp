using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Pressing.Core.Data;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Core.Security;
using Pressing.Core.Services;

namespace Pressing.Core.Assistant;

/// <summary>
/// Transforme une intention (sortie du modèle de langage) en <b>brouillon validé</b> par les règles métier existantes :
/// client retrouvé, catégories et services rapprochés du catalogue, prix calculés par le catalogue, ticket vérifié.
/// <para>
/// Principe non négociable : ce service est en <b>lecture seule</b>. Il ne crée ni commande, ni paiement, ni retrait,
/// ne modifie aucun statut et n'écrit rien en base : le caissier vérifie le brouillon puis valide par les routes existantes.
/// Il ne devine jamais : ce qui est absent, ambigu ou refusé devient un avertissement lisible et un champ « incertain ».
/// </para>
/// </summary>
public class AssistantService
{
    private const decimal Tolerance = 0.005m;

    private readonly PressingDbContext _db;
    private readonly CatalogService _catalog;
    private readonly OrderService _orders;
    private readonly TimeProvider _time;

    public AssistantService(PressingDbContext db, CatalogService catalog, OrderService orders, TimeProvider? time = null)
    {
        _db = db;
        _catalog = catalog;
        _orders = orders;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Construit le brouillon d'une intention. Ne modifie jamais la base.</summary>
    /// <param name="intent">Sortie du modèle de langage.</param>
    /// <param name="confidenceThreshold">Seuil sous lequel l'assistant « n'a pas compris » ; 0,6 par défaut.</param>
    public async Task<OrderDraftDto> BuildDraftAsync(
        AssistantIntentDto intent,
        double? confidenceThreshold = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(intent);

        double confidence = double.IsNaN(intent.Confidence) ? 0 : Math.Clamp(intent.Confidence, 0, 1);
        string intentName = AssistantIntents.IsKnown(intent.Intent) ? intent.Intent : AssistantIntents.Unknown;
        double threshold = confidenceThreshold ?? AssistantContract.DefaultConfidenceThreshold;
        var draft = new Draft(intentName, confidence, intent.Transcript ?? string.Empty);

        if (intentName == AssistantIntents.Unknown)
        {
            draft.Warn("Je n'ai pas compris la demande.");
            return draft.ToDto();
        }

        if (confidence < threshold)
        {
            draft.Warn($"Je n'ai pas compris avec assez de certitude (confiance {Percent(confidence)}).");
            return draft.ToDto();
        }

        switch (intentName)
        {
            case AssistantIntents.CreateOrder:
                await BuildCreateOrderAsync(intent, draft, ct);
                break;
            case AssistantIntents.RegisterPickup:
                await BuildPickupAsync(intent, draft, ct);
                break;
            case AssistantIntents.AddPayment:
                await BuildPaymentAsync(intent, draft, ct);
                break;
            case AssistantIntents.UpdateItemStatus:
                await BuildStatusAsync(intent, draft, ct);
                break;
            case AssistantIntents.FindOrder:
                await BuildFindAsync(intent, draft, ct);
                break;
        }

        return draft.ToDto();
    }

    // ------------------------------------------------------------------ une méthode par intention

    private async Task BuildCreateOrderAsync(AssistantIntentDto intent, Draft d, CancellationToken ct)
    {
        d.Target = AssistantTarget.NewOrder;
        d.Client = await ResolveClientAsync(intent.Client, d, required: true, ct);
        await BuildItemsAsync(intent.Items, d.Client?.ClientId, d, ct);

        d.DepositAmount = ReadAmount(intent.Deposit, "deposit", "L'acompte", d, required: false, allowZero: true);
        d.PaymentMethod = ReadPaymentMethod(intent.PaymentMethod, d);
        d.DueDate = ReadDate(intent.PickupDate, d);

        if (d.DepositAmount.HasValue && d.Quote is not null && d.DepositAmount.Value > d.Quote.Total + Tolerance)
        {
            d.Warn($"L'acompte ({Fcfa(d.DepositAmount.Value)}) dépasse le total du dépôt ({Fcfa(d.Quote.Total)}).", "deposit");
        }
    }

    private async Task BuildPickupAsync(AssistantIntentDto intent, Draft d, CancellationToken ct)
    {
        d.Target = AssistantTarget.Pickup;
        Order? order = await FindOrderAsync(intent.Ticket, d, ct);
        await AttachClientAsync(intent, order, d, ct);
        if (order is null) return;

        WarnOrderState(order, d);
        if (order.Status is OrderStatus.Cancelled or OrderStatus.Completed) return;

        int readyCount = order.Items.Where(i => i.Status == ItemStatus.Ready).Sum(i => i.RemainingQuantity);
        if (readyCount == 0)
        {
            d.Warn("Aucun article prêt à retirer pour ce ticket.", "ticket");
        }

        if (order.Balance > Tolerance)
        {
            d.Warn($"Solde restant à payer : {Fcfa(order.Balance)}.");
        }
    }

    private async Task BuildPaymentAsync(AssistantIntentDto intent, Draft d, CancellationToken ct)
    {
        d.Target = AssistantTarget.Payment;
        Order? order = await FindOrderAsync(intent.Ticket, d, ct);
        await AttachClientAsync(intent, order, d, ct);

        d.PaymentMethod = ReadPaymentMethod(intent.PaymentMethod, d);
        if (string.IsNullOrWhiteSpace(intent.PaymentMethod))
        {
            d.Warn("Mode de paiement non précisé.", "paymentMethod");
        }

        d.PaymentAmount = ReadAmount(intent.Amount, "amount", "Le montant", d, required: true, allowZero: false);
        if (order is null) return;

        WarnOrderState(order, d);
        if (order.Status == OrderStatus.Cancelled) return;

        if (order.Balance <= Tolerance)
        {
            d.Warn("Cette commande est déjà soldée.", "amount");
        }
        else if (d.PaymentAmount.HasValue && d.PaymentAmount.Value > order.Balance + Tolerance)
        {
            d.Warn($"Le montant ({Fcfa(d.PaymentAmount.Value)}) dépasse le solde restant ({Fcfa(order.Balance)}).", "amount");
        }
    }

    private async Task BuildStatusAsync(AssistantIntentDto intent, Draft d, CancellationToken ct)
    {
        d.Target = AssistantTarget.UpdateStatus;
        Order? order = await FindOrderAsync(intent.Ticket, d, ct);
        await AttachClientAsync(intent, order, d, ct);

        ItemStatus? status = AssistantItemStatuses.ToItemStatus(intent.ItemStatus);
        d.ItemStatus = status;
        if (status is null)
        {
            d.Warn(
                string.IsNullOrWhiteSpace(intent.ItemStatus)
                    ? "Statut non précisé (« en traitement » ou « prêt »)."
                    : $"Statut non reconnu : « {intent.ItemStatus.Trim()} » (seuls « en traitement » et « prêt » sont possibles).",
                "itemStatus");
        }

        if (order is null) return;

        WarnOrderState(order, d);
        if (order.Status is OrderStatus.Cancelled or OrderStatus.Completed) return;

        // Un article retiré (même partiellement) ou annulé ne peut plus changer de statut (règle de docs/API.md).
        List<OrderItem> editable = order.Items
            .Where(i => i.Status is not (ItemStatus.PickedUp or ItemStatus.Cancelled) && i.PickedUpQuantity == 0)
            .ToList();

        if (editable.Count == 0)
        {
            d.Warn("Aucun article modifiable : tous sont retirés ou annulés.", "ticket");
        }
        else if (status.HasValue && editable.All(i => i.Status == status.Value))
        {
            d.Warn($"Tous les articles sont déjà « {status.Value.Label()} ».", "itemStatus");
        }
    }

    private async Task BuildFindAsync(AssistantIntentDto intent, Draft d, CancellationToken ct)
    {
        d.Target = AssistantTarget.FindOrder;
        if (!string.IsNullOrWhiteSpace(intent.Ticket))
        {
            Order? order = await FindOrderAsync(intent.Ticket, d, ct);
            await AttachClientAsync(intent, order, d, ct);
            return;
        }

        d.Client = await ResolveClientAsync(intent.Client, d, required: false, ct);
        if (d.Client is null)
        {
            d.Warn("Ni ticket ni client précisé.", "ticket");
        }
    }

    // ------------------------------------------------------------------ client

    private sealed record ClientRow(int Id, string FullName, string Phone, string PhoneNormalized)
    {
        public ClientEntry ToEntry() => new(Id, FullName, Phone);
    }

    private async Task<List<ClientRow>> LoadClientsAsync(CancellationToken ct)
    {
        // Le rapprochement ignore accents et casse, ce que SQLite ne sait pas faire en SQL : on charge donc les
        // clients actifs (quelques colonnes) et on compare en mémoire. Largement suffisant pour un pressing.
        var rows = await _db.Clients.AsNoTracking()
            .Where(c => c.IsActive)
            .Select(c => new { c.Id, c.FirstName, c.LastName, c.Phone, c.PhoneNormalized })
            .ToListAsync(ct);

        return rows
            .Select(c => new ClientRow(c.Id, $"{c.FirstName} {c.LastName}".Trim(), c.Phone, c.PhoneNormalized))
            .ToList();
    }

    private static AssistantClientMatchDto Matched(ClientRow row) =>
        new(row.Id, row.FullName, row.Phone, false, []);

    private async Task<AssistantClientMatchDto?> ResolveClientAsync(
        AssistantClientDto? heard, Draft d, bool required, CancellationToken ct)
    {
        string? name = heard?.Name?.Trim();
        string? phone = heard?.Phone?.Trim();
        string phoneDigits = PhoneNormalizer.Normalize(phone);
        bool hasName = !string.IsNullOrWhiteSpace(name);
        bool hasPhone = phoneDigits.Length > 0;

        if (!hasName && !hasPhone)
        {
            if (required) d.Warn("Aucun client précisé.", "client");
            return null;
        }

        List<ClientRow> all = await LoadClientsAsync(ct);

        // 1. Le téléphone normalisé est l'identifiant le plus fiable.
        if (hasPhone)
        {
            List<ClientRow> byPhone = all.Where(c => c.PhoneNormalized == phoneDigits).ToList();
            if (byPhone.Count == 1)
            {
                ClientRow found = byPhone[0];
                if (hasName && AssistantMatching.MatchClient(name, [found.ToEntry()]).Kind == MatchKind.None)
                {
                    d.Warn($"Le numéro {phone} correspond à {found.FullName}, mais le nom entendu est « {name} ».", "client");
                }

                return Matched(found);
            }

            if (byPhone.Count > 1)
            {
                // Numéro partagé (famille) : le nom départage s'il y en a un.
                if (hasName)
                {
                    MatchResult<ClientEntry> narrowed = AssistantMatching.MatchClient(name, byPhone.Select(c => c.ToEntry()));
                    if (narrowed.Kind == MatchKind.Single)
                    {
                        return Matched(byPhone.First(c => c.Id == narrowed.Match!.Id));
                    }
                }

                d.Warn($"Plusieurs clients ont le numéro {phone} : choisissez dans la liste.", "client");
                return new AssistantClientMatchDto(
                    null, name, phone, false,
                    byPhone.Take(5).Select(c => new AssistantClientCandidateDto(c.Id, c.FullName, c.Phone)).ToList());
            }

            if (!hasName)
            {
                d.Warn($"Aucun client n'a le numéro {phone} : création proposée.", "client");
                return new AssistantClientMatchDto(null, null, phone, true, []);
            }
        }

        // 2. Sinon le nom, approché (accents, casse, fautes), avec gestion des homonymes.
        MatchResult<ClientEntry> byName = AssistantMatching.MatchClient(name, all.Select(c => c.ToEntry()));
        switch (byName.Kind)
        {
            case MatchKind.Single:
            {
                ClientRow found = all.First(c => c.Id == byName.Match!.Id);
                if (hasPhone)
                {
                    d.Warn($"Aucun client n'a le numéro {phone} ; client retrouvé par son nom : {found.FullName}.", "client");
                }
                else if (byName.Approximate)
                {
                    d.Warn($"Client « {name} » rapproché de {found.FullName}.", "client");
                }

                return Matched(found);
            }

            case MatchKind.Ambiguous:
                d.Warn($"Plusieurs clients correspondent à « {name} » : choisissez dans la liste.", "client");
                return new AssistantClientMatchDto(
                    null, name, phone, false,
                    byName.Candidates.Select(c => new AssistantClientCandidateDto(c.Id, c.FullName, c.Phone)).ToList());

            default:
                d.Warn("Client inconnu : création proposée.", "client");
                return new AssistantClientMatchDto(null, name, phone, true, []);
        }
    }

    /// <summary>Client affiché pour une commande retrouvée par son ticket ; sinon client entendu.</summary>
    private async Task AttachClientAsync(AssistantIntentDto intent, Order? order, Draft d, CancellationToken ct)
    {
        if (order is null)
        {
            d.Client = await ResolveClientAsync(intent.Client, d, required: false, ct);
            return;
        }

        string fullName = order.Client.FullName;
        d.Client = new AssistantClientMatchDto(order.ClientId, fullName, order.Client.Phone, false, []);

        string? heardName = intent.Client?.Name;
        if (!string.IsNullOrWhiteSpace(heardName) &&
            AssistantMatching.MatchClient(heardName, [new ClientEntry(order.ClientId, fullName, order.Client.Phone)]).Kind == MatchKind.None)
        {
            d.Warn($"Le ticket {order.TicketNumber} appartient à {fullName}, pas à « {heardName.Trim()} ».", "client");
        }
    }

    // ------------------------------------------------------------------ ticket

    private async Task<Order?> FindOrderAsync(string? ticketText, Draft d, CancellationToken ct)
    {
        TicketReference? reference = AssistantMatching.ParseTicket(ticketText);
        if (reference is null)
        {
            d.Warn(
                string.IsNullOrWhiteSpace(ticketText) ? "Ticket non précisé." : $"Ticket illisible : « {ticketText.Trim()} ».",
                "ticket");
            return null;
        }

        IQueryable<Order> query = _db.Orders.AsNoTracking().Include(o => o.Items).Include(o => o.Client);
        Order? order;
        if (reference.FullTicket is not null)
        {
            string wanted = reference.FullTicket;
            order = await query.FirstOrDefaultAsync(o => o.TicketNumber == wanted, ct);
        }
        else
        {
            // Numéro court (« 142 ») : le ticket est PRESS-AAAA-00142 ; l'année n'est pas dite à l'oral.
            string pattern = "%-" + reference.Number!.Value.ToString("00000", CultureInfo.InvariantCulture);
            List<Order> found = await query
                .Where(o => EF.Functions.Like(o.TicketNumber, pattern))
                .OrderByDescending(o => o.CreatedAt)
                .Take(2)
                .ToListAsync(ct);

            order = found.FirstOrDefault();
            if (found.Count > 1)
            {
                d.Warn(
                    $"Plusieurs tickets portent le numéro {reference.Number} (années différentes) : le plus récent est proposé ({order!.TicketNumber}).",
                    "ticket");
            }
        }

        if (order is null)
        {
            d.Warn($"Ticket inexistant : {reference.Display}.", "ticket");
            return null;
        }

        d.OrderId = order.Id;
        d.Ticket = order.TicketNumber;
        d.Balance = order.Balance;
        return order;
    }

    private static void WarnOrderState(Order order, Draft d)
    {
        if (order.Status == OrderStatus.Cancelled)
        {
            d.Warn($"La commande {order.TicketNumber} est annulée.", "ticket");
        }
        else if (order.Status == OrderStatus.Completed)
        {
            d.Warn($"La commande {order.TicketNumber} est terminée : tous les articles ont été retirés.", "ticket");
        }
    }

    // ------------------------------------------------------------------ articles et devis

    private async Task BuildItemsAsync(
        IReadOnlyList<AssistantItemDto>? heardItems, int? clientId, Draft d, CancellationToken ct)
    {
        if (heardItems is null || heardItems.Count == 0)
        {
            d.Warn("Aucun article entendu.", "items");
            return;
        }

        var categories = await _db.Categories.AsNoTracking()
            .Where(c => c.IsActive)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);
        List<CatalogEntry> catalog = categories.Select(c => new CatalogEntry(c.Id, c.Name)).ToList();

        var quoteItems = new List<CreateOrderItemRequest>();
        for (int i = 0; i < heardItems.Count; i++)
        {
            AssistantItemDto heard = heardItems[i];
            string label = $"Article {i + 1}";
            string field = $"items[{i}]";
            bool uncertain = false;

            int quantity = heard.Quantity;
            if (quantity < 1 || quantity > InputLimits.MaxQuantity)
            {
                d.Warn($"{label} : quantité invalide ({quantity}), à vérifier.", $"{field}.quantity");
                quantity = Math.Clamp(quantity, 1, InputLimits.MaxQuantity);
                uncertain = true;
            }

            // Catégorie : rapprochée du catalogue, jamais devinée.
            string? heardCategory = heard.Category?.Trim();
            MatchResult<CatalogEntry> found = AssistantMatching.MatchCategory(heardCategory, catalog);
            CatalogEntry? category = null;
            if (found.Kind == MatchKind.Single)
            {
                category = found.Match;
                if (found.Approximate)
                {
                    d.Warn($"{label} : « {heardCategory} » rapproché de « {category!.Name} ».", $"{field}.category");
                    uncertain = true;
                }
            }
            else if (found.Kind == MatchKind.Ambiguous)
            {
                string names = string.Join(", ", found.Candidates.Select(c => c.Name));
                d.Warn($"{label} : « {heardCategory} » correspond à plusieurs catégories ({names}).", $"{field}.category");
                uncertain = true;
            }
            else
            {
                d.Warn(
                    string.IsNullOrWhiteSpace(heardCategory)
                        ? $"{label} : catégorie non précisée."
                        : $"{label} : « {heardCategory} » n'existe pas au catalogue.",
                    $"{field}.category");
                uncertain = true;
            }

            // Service.
            string? heardService = heard.Service?.Trim();
            ServiceType? service = AssistantMatching.MatchService(heardService);
            if (service is null)
            {
                d.Warn(
                    string.IsNullOrWhiteSpace(heardService)
                        ? $"{label} : service non précisé."
                        : $"{label} : service « {heardService} » non reconnu.",
                    $"{field}.service");
                uncertain = true;
            }

            // Prix : toujours ceux du catalogue (CatalogService), express compris.
            decimal? unitPrice = null;
            decimal? lineTotal = null;
            if (category is not null && service.HasValue)
            {
                CategoryPrice? price = await _catalog.GetPriceAsync(category.Id, service.Value, ct);
                if (price is null)
                {
                    d.Warn($"{label} : aucun tarif actif pour « {category.Name} » ({service.Value.Label()}).", $"{field}.service");
                    uncertain = true;
                }
                else
                {
                    unitPrice = price.Price;
                    lineTotal = (price.Price + (heard.Express ? price.ExpressSupplement : 0m)) * quantity;
                    quoteItems.Add(new CreateOrderItemRequest(
                        category.Id, service.Value, quantity, null, heard.Express, null, null, null));
                }
            }

            d.Items.Add(new OrderDraftItemDto(
                category?.Id, category?.Name, service, service?.Label(),
                quantity, heard.Express, unitPrice, lineTotal, heardCategory, uncertain));
        }

        if (quoteItems.Count == 0) return;

        try
        {
            d.Quote = await _orders.QuoteAsync(
                new OrderQuoteRequest(quoteItems, false, DeliveryType.StorePickup, 0m, 0m, null, 0, clientId), ct);
            if (quoteItems.Count < heardItems.Count)
            {
                d.Warn("Le total ne comprend que les articles reconnus.", "items");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            d.Warn(ex.Message);
        }
    }

    // ------------------------------------------------------------------ valeurs dictées

    private static decimal? ReadAmount(decimal? value, string field, string label, Draft d, bool required, bool allowZero)
    {
        if (!value.HasValue)
        {
            if (required) d.Warn($"{label} non entendu.", field);
            return null;
        }

        decimal amount = value.Value;
        if (amount < 0 || amount > InputLimits.MaxAmount || (!allowZero && amount == 0))
        {
            d.Warn($"{label} invalide : {Fcfa(amount)}.", field);
            return null;
        }

        return amount;
    }

    private static PaymentMethod? ReadPaymentMethod(string? raw, Draft d)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        PaymentMethod? method = AssistantPaymentMethods.ToPaymentMethod(raw.Trim());
        if (method is null)
        {
            d.Warn($"Mode de paiement non reconnu : « {raw.Trim()} ».", "paymentMethod");
        }

        return method;
    }

    private DateTime? ReadDate(DateOnly? value, Draft d)
    {
        if (!value.HasValue) return null;

        DateOnly today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
        if (value.Value < today)
        {
            d.Warn($"La date de retrait ({value.Value:yyyy-MM-dd}) est dans le passé.", "dueDate");
            return null;
        }

        return value.Value.ToDateTime(TimeOnly.MinValue);
    }

    private static string Fcfa(decimal value) =>
        value.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ') + " FCFA";

    private static string Percent(double value) =>
        Math.Round(value * 100).ToString("0", CultureInfo.InvariantCulture) + " %";

    // ------------------------------------------------------------------ brouillon en construction

    private sealed class Draft(string intent, double confidence, string transcript)
    {
        public AssistantTarget Target { get; set; } = AssistantTarget.NotUnderstood;
        public AssistantClientMatchDto? Client { get; set; }
        public List<OrderDraftItemDto> Items { get; } = [];
        public int? OrderId { get; set; }
        public string? Ticket { get; set; }
        public decimal? Balance { get; set; }
        public decimal? PaymentAmount { get; set; }
        public PaymentMethod? PaymentMethod { get; set; }
        public decimal? DepositAmount { get; set; }
        public DateTime? DueDate { get; set; }
        public ItemStatus? ItemStatus { get; set; }
        public OrderQuoteDto? Quote { get; set; }
        private List<string> Uncertain { get; } = [];
        private List<string> Warnings { get; } = [];

        public void Flag(string field)
        {
            if (!Uncertain.Contains(field)) Uncertain.Add(field);
        }

        public void Warn(string message, string? field = null)
        {
            Warnings.Add(message);
            if (field is not null) Flag(field);
        }

        public OrderDraftDto ToDto() => new(
            intent, Target, Client, Items, OrderId, Ticket, Balance, PaymentAmount, PaymentMethod,
            DepositAmount, DueDate, ItemStatus, Quote, Uncertain, Warnings, confidence, transcript);
    }
}
