using Microsoft.EntityFrameworkCore;
using Pressing.Core.Data;
using Pressing.Core.Dtos;
using Pressing.Core.Email;
using Pressing.Core.Entities;
using Pressing.Core.Options;
using Pressing.Core.Security;

namespace Pressing.Core.Services;

public class OrderService
{
    private readonly PressingDbContext _db;
    private readonly SettingsService _settings;
    private readonly PromotionService _promotions;
    private readonly PaymentService _payments;
    private readonly EmailService _email;
    private readonly NotificationService _notifications;
    private readonly AuditService _audit;

    public OrderService(
        PressingDbContext db,
        SettingsService settings,
        PromotionService promotions,
        PaymentService payments,
        EmailService email,
        NotificationService notifications,
        AuditService audit)
    {
        _db = db;
        _settings = settings;
        _promotions = promotions;
        _payments = payments;
        _email = email;
        _notifications = notifications;
        _audit = audit;
    }

    public async Task<OrderDto> CreateAsync(
        CreateOrderRequest request,
        CurrentUser user,
        CancellationToken ct = default)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            throw new InvalidOperationException("Ajoutez au moins un article à la commande.");
        }

        Client client = await _db.Clients.FirstOrDefaultAsync(c => c.Id == request.ClientId, ct)
            ?? throw new KeyNotFoundException("Client introuvable.");
        if (!client.IsActive)
        {
            throw new InvalidOperationException("Ce client est désactivé.");
        }

        QuoteComputation quote = await ComputeAsync(
            request.Items,
            request.IsExpress,
            request.DueDate,
            request.DeliveryType,
            request.DeliveryFee,
            request.DiscountAmount,
            request.PromotionCode,
            request.LoyaltyPointsToRedeem,
            request.ClientId,
            strictPromotion: true,
            ct);

        if (request.DepositAmount < 0)
        {
            throw new InvalidOperationException("L'acompte ne peut pas être négatif.");
        }

        if (request.DepositAmount > quote.Total + 0.005m)
        {
            throw new InvalidOperationException("L'acompte ne peut pas dépasser le total de la commande.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var order = new Order
            {
                TicketNumber = $"TEMP-{Guid.NewGuid():N}",
                ClientId = client.Id,
                Client = client,
                CreatedAt = DateTime.Now,
                DueDate = quote.DueDate,
                Status = OrderStatus.Deposited,
                DeliveryType = request.DeliveryType,
                DeliveryAddress = request.DeliveryType == DeliveryType.HomeDelivery
                    ? request.DeliveryAddress?.Trim()
                    : null,
                DeliveryFee = quote.DeliveryFee,
                Subtotal = quote.Subtotal,
                ExpressTotal = quote.ExpressTotal,
                DiscountAmount = quote.ManualDiscount,
                PromotionDiscount = quote.PromotionDiscount,
                PromotionCode = string.IsNullOrWhiteSpace(request.PromotionCode) ? null : request.PromotionCode.Trim().ToUpperInvariant(),
                LoyaltyPointsRedeemed = quote.LoyaltyPointsUsed,
                LoyaltyDiscount = quote.LoyaltyDiscount,
                Total = quote.Total,
                IsExpress = request.IsExpress || request.Items.Any(i => i.IsExpress),
                Notes = request.Notes?.Trim(),
                CreatedByStaffId = user.StaffId,
                UpdatedAt = DateTime.Now
            };

            foreach (LineData line in quote.Lines)
            {
                order.Items.Add(new OrderItem
                {
                    CategoryId = line.Category.Id,
                    Category = line.Category,
                    ServiceType = line.ServiceType,
                    Description = line.Description,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    ExpressSupplement = line.ExpressSupplement,
                    IsExpress = line.IsExpress,
                    Status = ItemStatus.Deposited,
                    DueDate = line.DueDate,
                    Notes = line.Notes
                });
            }

            if (quote.LoyaltyPointsUsed > 0)
            {
                client.LoyaltyPoints -= quote.LoyaltyPointsUsed;
                client.UpdatedAt = DateTime.Now;
                _db.LoyaltyTransactions.Add(new LoyaltyTransaction
                {
                    ClientId = client.Id,
                    Points = -quote.LoyaltyPointsUsed,
                    Type = LoyaltyTransactionType.Redeemed,
                    Note = "Points utilisés lors du dépôt",
                    CreatedAt = DateTime.Now
                });
            }

            _db.Orders.Add(order);
            await _db.SaveChangesAsync(ct);

            order.TicketNumber = $"PRESS-{order.CreatedAt:yyyy}-{order.Id:00000}";

            if (!string.IsNullOrWhiteSpace(order.PromotionCode))
            {
                Promotion? promotion = await _promotions.GetEntityAsync(order.PromotionCode, ct);
                if (promotion is not null)
                {
                    promotion.UsedCount++;
                }
            }

            if (request.DepositAmount > 0)
            {
                await _payments.AddPaymentCoreAsync(
                    order,
                    request.DepositAmount,
                    request.DepositMethod,
                    request.DepositReference,
                    "Acompte au dépôt",
                    DateTime.Now,
                    user,
                    ct);
            }

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            await _email.QueueOrderConfirmationAsync(order, ct);
            await _notifications.NotifyAsync(
                "Nouvelle commande",
                $"{order.TicketNumber} — {client.FullName} — {order.Total:N0}",
                NotificationKind.Info,
                $"/orders/{order.Id}",
                ct);
            await _audit.LogAsync(
                user, "create", nameof(Order), order.Id.ToString(),
                $"Dépôt {order.TicketNumber} pour {client.FullName}", ct: ct);

            return await GetAsync(order.Id, ct) ?? order.ToDto();
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<OrderQuoteDto> QuoteAsync(OrderQuoteRequest request, CancellationToken ct = default)
    {
        QuoteComputation quote = await ComputeAsync(
            request.Items,
            request.IsExpress,
            null,
            request.DeliveryType,
            request.DeliveryFee,
            request.DiscountAmount,
            request.PromotionCode,
            request.LoyaltyPointsToRedeem,
            request.ClientId,
            strictPromotion: false,
            ct);

        return new OrderQuoteDto(
            quote.Subtotal,
            quote.ExpressTotal,
            quote.DeliveryFee,
            quote.ManualDiscount,
            quote.PromotionDiscount,
            quote.LoyaltyDiscount,
            quote.Total,
            quote.LoyaltyPointsUsed,
            quote.PromotionMessage,
            quote.PromotionValid);
    }

    public async Task<OrderDto?> GetAsync(int id, CancellationToken ct = default)
    {
        Order? order = await QueryFull().FirstOrDefaultAsync(o => o.Id == id, ct);
        return order?.ToDto();
    }

    public async Task<PagedResult<OrderSummaryDto>> SearchAsync(
        OrderStatus? status = null,
        string? search = null,
        DateTime? from = null,
        DateTime? to = null,
        int? clientId = null,
        bool onlyReady = false,
        bool onlyOutstanding = false,
        bool includeCancelled = true,
        int page = 1,
        int pageSize = 30,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        IQueryable<Order> query = _db.Orders.AsNoTracking()
            .Include(o => o.Client)
            .Include(o => o.Items);

        if (status.HasValue) query = query.Where(o => o.Status == status.Value);
        if (!includeCancelled) query = query.Where(o => o.Status != OrderStatus.Cancelled);
        if (clientId.HasValue) query = query.Where(o => o.ClientId == clientId.Value);
        if (from.HasValue) query = query.Where(o => o.CreatedAt >= from.Value.Date);
        if (to.HasValue) query = query.Where(o => o.CreatedAt < to.Value.Date.AddDays(1));
        if (onlyReady)
        {
            query = query.Where(o =>
                o.Status != OrderStatus.Cancelled &&
                o.Status != OrderStatus.Completed &&
                o.Items.Any(i => i.Status == ItemStatus.Ready && i.PickedUpQuantity < i.Quantity));
        }

        if (onlyOutstanding)
        {
            query = query.Where(o => o.Status != OrderStatus.Cancelled && o.Total > o.PaidAmount);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string raw = search.Trim();
            string term = $"%{raw}%";
            // Un texte sans chiffre (nom, ticket sans numéro…) donnerait le motif "%%" qui correspond à tout :
            // on ne filtre sur le téléphone normalisé que s'il y a des chiffres à chercher.
            string digits = PhoneNormalizer.Normalize(raw);
            bool searchPhone = digits.Length > 0;
            string normalized = $"%{digits}%";
            query = query.Where(o =>
                EF.Functions.Like(o.TicketNumber, term) ||
                EF.Functions.Like(o.Client.FirstName, term) ||
                EF.Functions.Like(o.Client.LastName, term) ||
                EF.Functions.Like(o.Client.Phone, term) ||
                (searchPhone && EF.Functions.Like(o.Client.PhoneNormalized, normalized)));
        }

        int total = await query.CountAsync(ct);
        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<OrderSummaryDto>(orders.Select(o => o.ToSummary()).ToList(), total, page, pageSize);
    }

    public async Task<IReadOnlyList<OrderSummaryDto>> GetReadyAsync(int take = 30, CancellationToken ct = default)
    {
        PagedResult<OrderSummaryDto> result = await SearchAsync(onlyReady: true, pageSize: take, ct: ct);
        return result.Items;
    }

    public async Task<IReadOnlyList<OrderSummaryDto>> GetOutstandingAsync(int take = 30, CancellationToken ct = default)
    {
        PagedResult<OrderSummaryDto> result = await SearchAsync(onlyOutstanding: true, pageSize: take, ct: ct);
        return result.Items;
    }

    public async Task<OrderDto> UpdateItemStatusAsync(
        int orderId,
        int itemId,
        ItemStatus status,
        CurrentUser user,
        CancellationToken ct = default)
    {
        Order order = await QueryFull().FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new KeyNotFoundException("Commande introuvable.");

        if (order.Status is OrderStatus.Cancelled or OrderStatus.Completed)
        {
            throw new InvalidOperationException("Cette commande ne peut plus être modifiée.");
        }

        OrderItem item = order.Items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new KeyNotFoundException("Article introuvable.");

        if (item.Status is ItemStatus.PickedUp or ItemStatus.Cancelled)
        {
            throw new InvalidOperationException("Cet article ne peut plus changer de statut.");
        }

        bool wasReady = order.Status == OrderStatus.Ready;
        item.Status = status;
        RecomputeStatus(order);

        await _db.SaveChangesAsync(ct);
        await NotifyIfReadyAsync(order, wasReady, ct);
        await _audit.LogAsync(
            user, "status", nameof(OrderItem), item.Id.ToString(),
            $"{item.Category?.Name} → {status.Label()} ({order.TicketNumber})", ct: ct);

        return await GetAsync(orderId, ct) ?? order.ToDto();
    }

    public async Task<OrderDto> UpdateAllItemsStatusAsync(
        int orderId,
        ItemStatus status,
        CurrentUser user,
        CancellationToken ct = default)
    {
        Order order = await QueryFull().FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new KeyNotFoundException("Commande introuvable.");

        if (order.Status is OrderStatus.Cancelled or OrderStatus.Completed)
        {
            throw new InvalidOperationException("Cette commande ne peut plus être modifiée.");
        }

        bool wasReady = order.Status == OrderStatus.Ready;
        foreach (OrderItem item in order.Items.Where(i => i.Status is not (ItemStatus.PickedUp or ItemStatus.Cancelled)))
        {
            item.Status = status;
        }

        RecomputeStatus(order);
        await _db.SaveChangesAsync(ct);
        await NotifyIfReadyAsync(order, wasReady, ct);
        await _audit.LogAsync(
            user, "status", nameof(Order), order.Id.ToString(),
            $"Tous les articles → {status.Label()} ({order.TicketNumber})", ct: ct);

        return await GetAsync(orderId, ct) ?? order.ToDto();
    }

    public async Task<OrderDto> PickupAsync(
        int orderId,
        PickupRequest request,
        CurrentUser user,
        CancellationToken ct = default)
    {
        Order order = await QueryFull().FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new KeyNotFoundException("Commande introuvable.");

        if (order.Status is OrderStatus.Cancelled or OrderStatus.Completed)
        {
            throw new InvalidOperationException("Cette commande ne peut pas être retirée.");
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            throw new InvalidOperationException("Sélectionnez au moins un article à retirer.");
        }

        bool allowBalance = await _settings.GetBoolAsync(SettingsKeys.AllowPickupWithBalance, false, ct);
        var selected = new List<(OrderItem Item, int Quantity)>();

        foreach (PickupItemRequest line in request.Items)
        {
            OrderItem item = order.Items.FirstOrDefault(i => i.Id == line.OrderItemId)
                ?? throw new KeyNotFoundException("Article introuvable dans cette commande.");

            if (line.Quantity <= 0 || line.Quantity > item.RemainingQuantity)
            {
                throw new InvalidOperationException($"Quantité invalide pour {item.Category?.Name}.");
            }

            if (item.Status != ItemStatus.Ready)
            {
                throw new InvalidOperationException($"L'article {item.Category?.Name} n'est pas encore prêt.");
            }

            selected.Add((item, line.Quantity));
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            if (request.PaymentAmount > 0)
            {
                await _payments.AddPaymentCoreAsync(
                    order,
                    request.PaymentAmount,
                    request.PaymentMethod,
                    request.Reference,
                    "Paiement au retrait",
                    DateTime.Now,
                    user,
                    ct);
            }

            if (order.Total - order.PaidAmount > 0.005m && !allowBalance)
            {
                throw new InvalidOperationException(
                    "Le solde doit être réglé avant le retrait. Activez l'option dans les paramètres pour autoriser un retrait avec solde.");
            }

            var pickup = new Pickup
            {
                OrderId = order.Id,
                Order = order,
                ClientId = order.ClientId,
                Client = order.Client,
                PickedUpAt = DateTime.Now,
                StaffUserId = user.StaffId,
                Note = request.Note?.Trim()
            };

            foreach ((OrderItem item, int quantity) in selected)
            {
                item.PickedUpQuantity += quantity;
                if (item.RemainingQuantity <= 0)
                {
                    item.Status = ItemStatus.PickedUp;
                    item.PickedUpAt = DateTime.Now;
                }

                pickup.Items.Add(new PickupItem
                {
                    OrderItemId = item.Id,
                    OrderItem = item,
                    Quantity = quantity
                });
            }

            _db.Pickups.Add(pickup);
            RecomputeStatus(order);
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            await _notifications.NotifyAsync(
                "Retrait enregistré",
                $"{order.TicketNumber} — {order.Client?.FullName}",
                NotificationKind.Success,
                $"/orders/{order.Id}",
                ct);
            await _audit.LogAsync(
                user, "pickup", nameof(Order), order.Id.ToString(),
                $"Retrait de {selected.Sum(s => s.Quantity)} article(s) ({order.TicketNumber})", ct: ct);

            if (order.Client?.Email is not null)
            {
                await _email.QueueReceiptAsync(order, ct);
            }

            return await GetAsync(order.Id, ct) ?? order.ToDto();
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<OrderDto> CancelAsync(
        int orderId,
        string reason,
        CurrentUser user,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("Le motif d'annulation est obligatoire.");
        }

        Order order = await QueryFull().FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new KeyNotFoundException("Commande introuvable.");

        if (order.Status is OrderStatus.Cancelled or OrderStatus.Completed)
        {
            throw new InvalidOperationException("Cette commande ne peut pas être annulée.");
        }

        if (order.Items.Any(i => i.PickedUpQuantity > 0))
        {
            throw new InvalidOperationException("Impossible d'annuler une commande dont des articles ont déjà été retirés.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            if (order.PaidAmount > 0)
            {
                PaymentMethod method = order.Payments.LastOrDefault()?.Method ?? PaymentMethod.Cash;
                await _payments.RefundCoreAsync(
                    order, order.PaidAmount, method, null,
                    $"Remboursement — annulation : {reason}", user, ct);
            }

            if (order.LoyaltyPointsRedeemed > 0)
            {
                Client client = order.Client;
                client.LoyaltyPoints += order.LoyaltyPointsRedeemed;
                _db.LoyaltyTransactions.Add(new LoyaltyTransaction
                {
                    ClientId = client.Id,
                    OrderId = order.Id,
                    Points = order.LoyaltyPointsRedeemed,
                    Type = LoyaltyTransactionType.Adjusted,
                    Note = "Points restitués après annulation",
                    CreatedAt = DateTime.Now
                });
            }

            foreach (OrderItem item in order.Items)
            {
                if (item.Status != ItemStatus.PickedUp)
                {
                    item.Status = ItemStatus.Cancelled;
                }
            }

            order.Status = OrderStatus.Cancelled;
            order.CancelledAt = DateTime.Now;
            order.CancelReason = reason.Trim();
            order.UpdatedAt = DateTime.Now;

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            await _email.QueueOrderCancelledAsync(order, reason, ct);
            await _notifications.NotifyAsync(
                "Commande annulée",
                $"{order.TicketNumber} — {order.Client?.FullName}",
                NotificationKind.Warning,
                $"/orders/{order.Id}",
                ct);
            await _audit.LogAsync(
                user, "cancel", nameof(Order), order.Id.ToString(),
                $"Annulation : {reason}", ct: ct);

            return await GetAsync(order.Id, ct) ?? order.ToDto();
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<IReadOnlyList<OrderSummaryDto>> GetForClientAsync(
        int clientId,
        int take = 50,
        CancellationToken ct = default)
    {
        PagedResult<OrderSummaryDto> result = await SearchAsync(clientId: clientId, pageSize: take, ct: ct);
        return result.Items;
    }

    public async Task<OrderDto?> GetForClientOrderAsync(int clientId, int orderId, CancellationToken ct = default)
    {
        Order? order = await QueryFull().FirstOrDefaultAsync(o => o.Id == orderId && o.ClientId == clientId, ct);
        return order?.ToDto();
    }

    public async Task<TrackingDto?> TrackAsync(string ticket, string phone, CancellationToken ct = default)
    {
        string normalized = PhoneNormalizer.Normalize(phone);
        if (string.IsNullOrWhiteSpace(ticket) || string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        Order? order = await _db.Orders.AsNoTracking()
            .Include(o => o.Client)
            .Include(o => o.Items).ThenInclude(i => i.Category)
            .FirstOrDefaultAsync(o => o.TicketNumber == ticket.Trim(), ct);

        if (order is null ||
            (order.Client.PhoneNormalized != normalized &&
             PhoneNormalizer.Normalize(order.Client.WhatsApp) != normalized))
        {
            return null;
        }

        ShopInfo shop = await _settings.GetShopInfoAsync(ct);
        return new TrackingDto(
            order.TicketNumber,
            order.Status.ToString(),
            order.Status.Label(),
            order.CreatedAt,
            order.DueDate,
            order.IsExpress,
            shop.Name,
            shop.Phone,
            $"{shop.Address}, {shop.City}",
            order.Items.Select(i => new TrackingItemDto(
                i.Category.Name, i.ServiceType.Label(), i.Quantity, i.Status.Label())).ToList());
    }

    public async Task<MessageResponse> SendReceiptAsync(int orderId, CurrentUser user, CancellationToken ct = default)
    {
        Order order = await QueryFull().FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new KeyNotFoundException("Commande introuvable.");

        if (string.IsNullOrWhiteSpace(order.Client.Email))
        {
            throw new InvalidOperationException("Ce client n'a pas d'adresse e-mail.");
        }

        await _email.QueueReceiptAsync(order, ct);
        await _audit.LogAsync(user, "receipt", nameof(Order), order.Id.ToString(),
            $"Reçu envoyé à {order.Client.Email}", ct: ct);
        return new MessageResponse($"Le reçu a été mis en file d'envoi pour {order.Client.Email}.");
    }

    public async Task<Order> LoadFullAsync(int id, CancellationToken ct = default) =>
        await QueryFull().FirstOrDefaultAsync(o => o.Id == id, ct)
        ?? throw new KeyNotFoundException("Commande introuvable.");

    private IQueryable<Order> QueryFull() => _db.Orders
        .Include(o => o.Client)
        .Include(o => o.CreatedByStaff)
        .Include(o => o.Items).ThenInclude(i => i.Category)
        .Include(o => o.Payments).ThenInclude(p => p.StaffUser)
        .Include(o => o.Pickups).ThenInclude(p => p.StaffUser)
        .Include(o => o.Pickups).ThenInclude(p => p.Items).ThenInclude(i => i.OrderItem).ThenInclude(i => i.Category);

    private async Task NotifyIfReadyAsync(Order order, bool wasReady, CancellationToken ct)
    {
        if (!wasReady && order.Status == OrderStatus.Ready)
        {
            order.ReadyNotifiedAt ??= DateTime.Now;
            await _db.SaveChangesAsync(ct);
            await _email.QueueOrderReadyAsync(order, ct);
            await _notifications.NotifyAsync(
                "Commande prête",
                $"{order.TicketNumber} — {order.Client?.FullName}",
                NotificationKind.Success,
                $"/orders/{order.Id}",
                ct);
        }
    }

    private static void RecomputeStatus(Order order)
    {
        if (order.Status == OrderStatus.Cancelled)
        {
            return;
        }

        List<OrderItem> active = order.Items.Where(i => i.Status != ItemStatus.Cancelled).ToList();
        if (active.Count == 0)
        {
            order.Status = OrderStatus.Cancelled;
            return;
        }

        if (active.All(i => i.Status == ItemStatus.PickedUp))
        {
            order.Status = OrderStatus.Completed;
            order.CompletedAt ??= DateTime.Now;
            return;
        }

        if (active.Any(i => i.Status == ItemStatus.PickedUp))
        {
            order.Status = OrderStatus.PartiallyPickedUp;
            return;
        }

        if (active.All(i => i.Status == ItemStatus.Ready))
        {
            order.Status = OrderStatus.Ready;
            return;
        }

        if (active.Any(i => i.Status is ItemStatus.InProgress or ItemStatus.Ready))
        {
            order.Status = OrderStatus.InProgress;
            return;
        }

        order.Status = OrderStatus.Deposited;
    }

    private async Task<QuoteComputation> ComputeAsync(
        IReadOnlyList<CreateOrderItemRequest> items,
        bool globalExpress,
        DateTime? forcedDueDate,
        DeliveryType deliveryType,
        decimal deliveryFee,
        decimal manualDiscount,
        string? promotionCode,
        int loyaltyPoints,
        int? clientId,
        bool strictPromotion,
        CancellationToken ct)
    {
        if (items is null || items.Count == 0)
        {
            throw new InvalidOperationException("Ajoutez au moins un article.");
        }

        if (manualDiscount < 0) throw new InvalidOperationException("La remise ne peut pas être négative.");
        if (loyaltyPoints < 0) throw new InvalidOperationException("Les points fidélité ne peuvent pas être négatifs.");

        int[] categoryIds = items.Select(i => i.CategoryId).Distinct().ToArray();
        var categories = await _db.Categories.Include(c => c.Prices)
            .Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c, ct);

        int defaultDays = await _settings.GetIntAsync(SettingsKeys.DefaultProcessingDays, 3, ct);
        int expressDays = await _settings.GetIntAsync(SettingsKeys.ExpressProcessingDays, 1, ct);
        decimal maxDiscountPercent = await _settings.GetDecimalAsync(SettingsKeys.MaxDiscountPercent, 30, ct);

        var lines = new List<LineData>();
        foreach (CreateOrderItemRequest request in items)
        {
            if (request.Quantity <= 0)
            {
                throw new InvalidOperationException("Chaque article doit avoir une quantité supérieure à zéro.");
            }

            if (!categories.TryGetValue(request.CategoryId, out Category? category) || !category.IsActive)
            {
                throw new InvalidOperationException("Une catégorie sélectionnée n'est plus disponible.");
            }

            CategoryPrice? price = category.Prices
                .FirstOrDefault(p => p.ServiceType == request.ServiceType && p.IsActive)
                ?? throw new InvalidOperationException($"Aucun tarif actif pour {category.Name} ({request.ServiceType.Label()}).");

            decimal unitPrice = request.UnitPrice ?? price.Price;
            if (unitPrice < 0)
            {
                throw new InvalidOperationException("Le prix unitaire ne peut pas être négatif.");
            }

            bool express = globalExpress || request.IsExpress;
            int days = express ? expressDays : (price.EstimatedDays > 0 ? price.EstimatedDays : defaultDays);
            DateTime dueDate = forcedDueDate?.Date ?? DateTime.Now.Date.AddDays(days);
            if (dueDate < DateTime.Now.Date)
            {
                throw new InvalidOperationException("La date de retrait ne peut pas être dans le passé.");
            }

            lines.Add(new LineData(
                category,
                request.ServiceType,
                request.Quantity,
                unitPrice,
                express ? price.ExpressSupplement : 0,
                express,
                dueDate,
                request.Description?.Trim(),
                request.Notes?.Trim()));
        }

        decimal subtotal = lines.Sum(l => l.UnitPrice * l.Quantity);
        decimal expressTotal = lines.Sum(l => l.ExpressSupplement * l.Quantity);
        decimal safeDelivery = deliveryType == DeliveryType.HomeDelivery ? Math.Max(0, deliveryFee) : 0;
        decimal baseAmount = subtotal + expressTotal + safeDelivery;

        if (manualDiscount > baseAmount * maxDiscountPercent / 100m + 0.005m)
        {
            throw new InvalidOperationException($"La remise manuelle dépasse le maximum autorisé ({maxDiscountPercent:N0} %).");
        }

        decimal promotionDiscount = 0;
        string? promotionMessage = null;
        bool promotionValid = false;
        if (!string.IsNullOrWhiteSpace(promotionCode))
        {
            PromotionValidationDto validation = await _promotions.ValidateAsync(promotionCode, baseAmount, ct);
            promotionValid = validation.Valid;
            promotionMessage = validation.Message;
            promotionDiscount = validation.Valid ? validation.Discount : 0;

            if (!validation.Valid && strictPromotion)
            {
                throw new InvalidOperationException(validation.Message);
            }
        }

        decimal remainingBeforeLoyalty = Math.Max(0, baseAmount - manualDiscount - promotionDiscount);
        decimal loyaltyDiscount = 0;
        int loyaltyUsed = 0;
        if (loyaltyPoints > 0)
        {
            if (!clientId.HasValue)
            {
                throw new InvalidOperationException("Sélectionnez un client pour utiliser ses points fidélité.");
            }

            Client? client = await _db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientId.Value, ct)
                ?? throw new KeyNotFoundException("Client introuvable.");

            if (loyaltyPoints > client.LoyaltyPoints)
            {
                throw new InvalidOperationException($"Le client ne possède que {client.LoyaltyPoints} points.");
            }

            decimal pointValue = await _settings.GetDecimalAsync(SettingsKeys.LoyaltyPointValue, 10, ct);
            if (pointValue > 0)
            {
                int maxPoints = (int)Math.Floor(remainingBeforeLoyalty / pointValue);
                loyaltyUsed = Math.Min(loyaltyPoints, maxPoints);
                loyaltyDiscount = loyaltyUsed * pointValue;
            }
        }

        decimal total = Math.Max(0, baseAmount - manualDiscount - promotionDiscount - loyaltyDiscount);
        DateTime orderDue = lines.Max(l => l.DueDate);

        return new QuoteComputation(
            lines,
            subtotal,
            expressTotal,
            safeDelivery,
            manualDiscount,
            promotionDiscount,
            promotionMessage,
            promotionValid,
            loyaltyDiscount,
            loyaltyUsed,
            total,
            orderDue);
    }

    private sealed record LineData(
        Category Category,
        ServiceType ServiceType,
        int Quantity,
        decimal UnitPrice,
        decimal ExpressSupplement,
        bool IsExpress,
        DateTime DueDate,
        string? Description,
        string? Notes);

    private sealed record QuoteComputation(
        List<LineData> Lines,
        decimal Subtotal,
        decimal ExpressTotal,
        decimal DeliveryFee,
        decimal ManualDiscount,
        decimal PromotionDiscount,
        string? PromotionMessage,
        bool PromotionValid,
        decimal LoyaltyDiscount,
        int LoyaltyPointsUsed,
        decimal Total,
        DateTime DueDate);
}
