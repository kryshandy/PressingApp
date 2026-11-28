using Microsoft.Extensions.Logging;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Pressing.Core.Data;
using Pressing.Core.Dtos;
using Pressing.Core.Email;
using Pressing.Core.Entities;
using Pressing.Core.Messaging;
using Pressing.Core.Options;
using Pressing.Core.Security;

namespace Pressing.Core.Services;

public sealed partial class ReminderService
{
    private readonly PressingDbContext _db;
    private readonly IWhatsAppTransport _whatsApp;
    private readonly EmailService _email;
    private readonly EmailOptions _emailOptions;
    private readonly TokenService _tokens;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReminderService> _logger;
    private readonly AuditService _audit;

    public ReminderService(
        PressingDbContext db,
        IWhatsAppTransport whatsApp,
        EmailService email,
        EmailOptions emailOptions,
        TokenService tokens,
        TimeProvider timeProvider,
        ILogger<ReminderService> logger,
        AuditService audit)
    {
        _db = db;
        _whatsApp = whatsApp;
        _email = email;
        _emailOptions = emailOptions;
        _tokens = tokens;
        _timeProvider = timeProvider;
        _logger = logger;
        _audit = audit;
    }

    public async Task<int> ProcessDueRemindersAsync(CancellationToken ct = default)
    {
        DateTime now = _timeProvider.GetLocalNow().DateTime;

        if (now.Hour < 8 || now.Hour >= 20)
            return 0;

        List<Order> orders = await _db.Orders
            .Include(o => o.Client)
            .Where(o => o.Status != OrderStatus.Completed &&
                        o.Status != OrderStatus.Cancelled)
            .ToListAsync(ct);

        if (orders.Count == 0)
            return 0;

        int[] orderIds = orders.Select(o => o.Id).ToArray();

        var sent = await _db.ReminderLogs
            .Where(r => orderIds.Contains(r.OrderId) && r.Success)
            .Select(r => new { r.OrderId, r.ReminderType })
            .ToListAsync(ct);

        var sentKeys = sent
            .Select(r => $"{r.OrderId}:{r.ReminderType}")
            .ToHashSet();

        int processed = 0;

        foreach (Order order in orders)
        {
            ct.ThrowIfCancellationRequested();

            if (order.Client is null || !order.Client.IsActive ||
                order.Client.NotificationPreference == ClientNotificationPreference.None ||
                !order.Client.ConsentGivenAt.HasValue ||
                order.Client.ConsentRevokedAt.HasValue)
                continue;

            bool waitingPickup = order.Status is OrderStatus.Ready or OrderStatus.PartiallyPickedUp;

            var reminders = new List<(string Type, string Subject, string Body, string[] Vars)>();

            // « Prête » par WhatsApp seulement : l'e-mail « commande prête » existe déjà (EmailService).
            if (order.Status == OrderStatus.Ready &&
                order.ReadyNotifiedAt.HasValue &&
                (now.Date - order.ReadyNotifiedAt.Value.Date).Days < 3 &&
                order.Client.NotificationPreference == ClientNotificationPreference.WhatsApp)
            {
                reminders.Add((
                    "ready",
                    "Votre commande est prête",
                    $"Bonjour {order.Client.FirstName}, votre commande {order.TicketNumber} est prête. Vous pouvez venir la récupérer.",
                    [order.Client.FirstName, order.TicketNumber]
                ));
            }

            if (waitingPickup && order.ReadyNotifiedAt.HasValue)
            {
                int days = (now.Date - order.ReadyNotifiedAt.Value.Date).Days;

                // Un seul palier à la fois (le plus élevé atteint) : à l'activation des rappels, une commande
                // prête depuis 14 jours reçoit le dernier rappel, pas trois messages d'un coup.
                if (days >= 3 && days < 7)
                    reminders.Add((
                        "uncollected-j3",
                        "Rappel : commande non récupérée",
                        $"Bonjour {order.Client.FirstName}, votre commande {order.TicketNumber} attend toujours d'être récupérée depuis 3 jours.",
                        [order.Client.FirstName, order.TicketNumber, "3"]
                    ));

                if (days >= 7 && days < 14)
                    reminders.Add((
                        "uncollected-j7",
                        "Rappel : commande en attente",
                        $"Bonjour {order.Client.FirstName}, votre commande {order.TicketNumber} est toujours en attente de retrait depuis 7 jours.",
                        [order.Client.FirstName, order.TicketNumber, "7"]
                    ));

                if (days >= 14)
                    reminders.Add((
                        "uncollected-j14",
                        "Dernier rappel de retrait",
                        $"Bonjour {order.Client.FirstName}, votre commande {order.TicketNumber} est en attente de retrait depuis 14 jours. Merci de nous contacter.",
                        [order.Client.FirstName, order.TicketNumber, "14"]
                    ));
            }

            // Solde rappelé quand le client peut venir payer et retirer, pas juste après le dépôt.
            if (waitingPickup && order.Balance > 0.005m)
            {
                reminders.Add((
                    "unpaid-balance",
                    "Rappel de solde impayé",
                    $"Bonjour {order.Client.FirstName}, le solde restant de votre commande {order.TicketNumber} est de {order.Balance:N0} FCFA.",
                    [order.Client.FirstName, order.TicketNumber, $"{order.Balance:N0} FCFA"]
                ));
            }

            if (!waitingPickup && order.DueDate.Date == now.Date.AddDays(1))
            {
                reminders.Add((
                    "due-tomorrow",
                    "Rappel : échéance de votre commande",
                    $"Bonjour {order.Client.FirstName}, votre commande {order.TicketNumber} a une échéance prévue demain.",
                    [order.Client.FirstName, order.TicketNumber, order.DueDate.ToString("dd/MM/yyyy")]
                ));
            }

            foreach (var reminder in reminders)
            {
                string key = $"{order.Id}:{reminder.Type}";

                if (sentKeys.Contains(key))
                    continue;

                (bool delivered, string channel, _) = await DeliverAsync(
                    order, reminder.Type, reminder.Subject, reminder.Body, reminder.Vars, ct);

                if (!delivered)
                {
                    _logger.LogWarning(
                        "Aucun canal disponible pour le rappel {ReminderType}, commande {OrderId}",
                        reminder.Type, order.Id);
                    continue;
                }

                _db.ReminderLogs.Add(new ReminderLog
                {
                    OrderId = order.Id,
                    ClientId = order.ClientId,
                    ReminderType = reminder.Type,
                    Channel = channel,
                    SentAt = now,
                    Success = true
                });

                await _db.SaveChangesAsync(ct);
                sentKeys.Add(key);
                processed++;
            }
        }

        return processed;
    }

    /// <summary>
    /// Relance manuelle d'un client par le personnel (hors plage horaire des rappels automatiques).
    /// Refusée sans consentement valide, pour une commande terminée ou annulée, ou sans coordonnée du canal choisi.
    /// L'envoi, réussi ou non, est journalisé (ReminderLog) et audité.
    /// </summary>
    public async Task<ReminderLogDto> SendManualAsync(int orderId, CurrentUser user, CancellationToken ct = default)
    {
        Order order = await _db.Orders
            .Include(o => o.Client)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new KeyNotFoundException("Commande introuvable.");

        if (order.Status is OrderStatus.Completed or OrderStatus.Cancelled)
            throw new InvalidOperationException("Cette commande est terminée ou annulée : aucune relance n'est possible.");

        Client client = order.Client
            ?? throw new InvalidOperationException("Cette commande n'est rattachée à aucun client.");

        if (!client.IsActive ||
            client.NotificationPreference == ClientNotificationPreference.None ||
            !client.ConsentGivenAt.HasValue ||
            client.ConsentRevokedAt.HasValue)
        {
            throw new InvalidOperationException(
                $"{client.FullName} n'a pas consenti aux rappels (WhatsApp ou e-mail) : la relance est impossible. " +
                "Le client peut donner son accord depuis son profil ou à l'accueil.");
        }

        DateTime now = _timeProvider.GetLocalNow().DateTime;
        bool waitingPickup = order.Status is OrderStatus.Ready or OrderStatus.PartiallyPickedUp;
        string body = waitingPickup
            ? $"Bonjour {client.FirstName}, votre commande {order.TicketNumber} est prête et vous attend."
            : $"Bonjour {client.FirstName}, votre commande {order.TicketNumber} est en cours de traitement ; son échéance est prévue le {order.DueDate:dd/MM/yyyy}.";
        if (order.Balance > 0.005m)
            body += $" Le solde restant est de {order.Balance:N0} FCFA.";

        (bool delivered, string channel, string? error) = await DeliverAsync(
            order, ManualType, "Rappel de votre commande", body, [], ct);

        var log = new ReminderLog
        {
            OrderId = order.Id,
            ClientId = order.ClientId,
            ReminderType = ManualType,
            Channel = delivered ? channel : ChannelOf(client.NotificationPreference),
            SentAt = now,
            Success = delivered,
            Error = delivered ? null : Truncate(error, 1000)
        };
        _db.ReminderLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(user, "reminder-send", nameof(Order), order.Id.ToString(),
            $"Relance manuelle {order.TicketNumber} ({log.Channel}) : {(delivered ? "envoyée" : "échec")}", ct: ct);

        if (!delivered)
            throw new InvalidOperationException($"L'envoi de la relance a échoué : {error ?? "canal indisponible"}");

        return ToDto(log, order.TicketNumber, client.FullName);
    }

    /// <summary>Journal des rappels, du plus récent au plus ancien, filtrable par type, canal et période.</summary>
    public async Task<PagedResult<ReminderLogDto>> GetLogsAsync(
        int page = 1,
        int pageSize = 30,
        string? type = null,
        string? channel = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        IQueryable<ReminderLog> query = _db.ReminderLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(type))
        {
            string wanted = type.Trim();
            query = query.Where(r => r.ReminderType == wanted);
        }

        if (!string.IsNullOrWhiteSpace(channel))
        {
            // « whatsapp » couvre aussi « whatsapp-simulated » ; « email » couvre « email-queued ».
            string wanted = channel.Trim().ToLowerInvariant();
            query = query.Where(r => r.Channel == wanted || r.Channel.StartsWith(wanted + "-"));
        }

        if (from.HasValue)
        {
            DateTime start = from.Value.Date;
            query = query.Where(r => r.SentAt >= start);
        }

        if (to.HasValue)
        {
            DateTime end = to.Value.Date.AddDays(1);
            query = query.Where(r => r.SentAt < end);
        }

        int total = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(r => r.SentAt)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Join(_db.Orders, r => r.OrderId, o => o.Id, (r, o) => new { Log = r, o.TicketNumber })
            .Join(_db.Clients, x => x.Log.ClientId, c => c.Id,
                (x, c) => new { x.Log, x.TicketNumber, c.FirstName, c.LastName })
            .ToListAsync(ct);

        List<ReminderLogDto> items = rows
            .Select(x => ToDto(x.Log, x.TicketNumber, $"{x.FirstName} {x.LastName}".Trim()))
            .ToList();

        return new PagedResult<ReminderLogDto>(items, total, page, pageSize);
    }

    private const string ManualType = "manual";

    private static ReminderLogDto ToDto(ReminderLog log, string ticket, string clientName) => new(
        log.Id, log.OrderId, ticket, log.ClientId, clientName,
        log.ReminderType, TypeLabel(log.ReminderType), log.Channel, ChannelLabel(log.Channel),
        log.SentAt, log.Success, log.Error);

    public static string TypeLabel(string type) => type switch
    {
        "ready" => "Commande prête",
        "uncollected-j3" => "Non récupérée (3 jours)",
        "uncollected-j7" => "Non récupérée (7 jours)",
        "uncollected-j14" => "Dernier rappel de retrait (14 jours)",
        "unpaid-balance" => "Solde impayé",
        "due-tomorrow" => "Échéance demain",
        ManualType => "Relance manuelle",
        FeedbackType => "Demande d'avis",
        _ => type
    };

    public static string ChannelLabel(string channel) => channel switch
    {
        "whatsapp" => "WhatsApp",
        "whatsapp-simulated" => "WhatsApp (simulé)",
        "email-queued" => "E-mail",
        _ => channel
    };

    private static string ChannelOf(ClientNotificationPreference preference) =>
        preference == ClientNotificationPreference.WhatsApp ? "whatsapp" : "email-queued";

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];

    /// <summary>Envoie un rappel par le canal choisi par le client. Ne journalise pas : c'est l'appelant qui le fait.</summary>
    private async Task<(bool Delivered, string Channel, string? Error)> DeliverAsync(
        Order order, string type, string subject, string body, string[] variables, CancellationToken ct, string? actionUrl = null, string? actionLabel = null)
    {
        Client client = order.Client!;

        if (client.NotificationPreference == ClientNotificationPreference.WhatsApp)
        {
            if (string.IsNullOrWhiteSpace(client.WhatsApp))
                return (false, string.Empty, "Aucun numéro WhatsApp n'est renseigné pour ce client.");

            try
            {
                WhatsAppSendResult result = await _whatsApp.SendAsync(
                    new WhatsAppMessage(client.WhatsApp, body, type, variables), ct);

                if (result.Success)
                    return (true, result.Simulated ? "whatsapp-simulated" : "whatsapp", null);

                _logger.LogWarning(
                    "WhatsApp échoué pour la commande {OrderId}: {Error}", order.Id, result.Error);
                return (false, string.Empty, result.Error);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Erreur WhatsApp pour la commande {OrderId}", order.Id);
                return (false, string.Empty, ex.Message);
            }
        }

        if (client.NotificationPreference == ClientNotificationPreference.Email)
        {
            if (string.IsNullOrWhiteSpace(client.Email))
                return (false, string.Empty, "Aucune adresse e-mail n'est renseignée pour ce client.");

            try
            {
                string unsubscribeToken = _tokens.CreateUnsubscribeToken(order.ClientId);
                // Page servie par le site (PRESSING_WEB_URL) : l'API n'est pas forcément joignable du public.
                string unsubscribeUrl = $"{_emailOptions.AppBaseUrl}/desabonnement?token={Uri.EscapeDataString(unsubscribeToken)}";
                string action = actionUrl is null
                    ? string.Empty
                    : $"<p><a href='{WebUtility.HtmlEncode(actionUrl)}'>{WebUtility.HtmlEncode(actionLabel ?? actionUrl)}</a></p>";
                string html = $"<p>{WebUtility.HtmlEncode(body)}</p>{action}<hr><p style='font-size:12px;color:#666'>Vous recevez ce message parce que vous avez accepté les rappels par e-mail. <a href='{WebUtility.HtmlEncode(unsubscribeUrl)}'>Me désabonner</a></p>";

                await _email.QueueRawAsync(
                    client.Email,
                    client.FullName,
                    subject,
                    html,
                    body,
                    order.ClientId,
                    order.Id,
                    ct,
                    template: "reminder");

                return (true, "email-queued", null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "Mise en attente de l'e-mail échouée pour la commande {OrderId}", order.Id);
                return (false, string.Empty, ex.Message);
            }
        }

        return (false, string.Empty, "Ce client n'a choisi aucun canal de rappel.");
    }
}
