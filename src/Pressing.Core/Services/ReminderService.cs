using Microsoft.Extensions.Logging;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Pressing.Core.Data;
using Pressing.Core.Email;
using Pressing.Core.Entities;
using Pressing.Core.Messaging;

namespace Pressing.Core.Services;

public sealed class ReminderService
{
    private readonly PressingDbContext _db;
    private readonly IWhatsAppTransport _whatsApp;
    private readonly EmailService _email;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReminderService> _logger;

    public ReminderService(
        PressingDbContext db,
        IWhatsAppTransport whatsApp,
        EmailService email,
        TimeProvider timeProvider,
        ILogger<ReminderService> logger)
    {
        _db = db;
        _whatsApp = whatsApp;
        _email = email;
        _timeProvider = timeProvider;
        _logger = logger;
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

            if (order.Client is null || !order.Client.IsActive)
                continue;

            var reminders = new List<(string Type, string Subject, string Body)>();

            if (order.Status == OrderStatus.Ready &&
                order.ReadyNotifiedAt.HasValue)
            {
                reminders.Add((
                    "ready",
                    "Votre commande est prête",
                    $"Bonjour {order.Client.FirstName}, votre commande {order.TicketNumber} est prête. Vous pouvez venir la récupérer."
                ));
            }

            if ((order.Status == OrderStatus.Ready ||
                 order.Status == OrderStatus.PartiallyPickedUp) &&
                order.ReadyNotifiedAt.HasValue)
            {
                int days = (now.Date - order.ReadyNotifiedAt.Value.Date).Days;

                if (days >= 3)
                    reminders.Add((
                        "uncollected-j3",
                        "Rappel : commande non récupérée",
                        $"Bonjour {order.Client.FirstName}, votre commande {order.TicketNumber} attend toujours d'être récupérée depuis 3 jours."
                    ));

                if (days >= 7)
                    reminders.Add((
                        "uncollected-j7",
                        "Rappel : commande en attente",
                        $"Bonjour {order.Client.FirstName}, votre commande {order.TicketNumber} est toujours en attente de retrait depuis 7 jours."
                    ));

                if (days >= 14)
                    reminders.Add((
                        "uncollected-j14",
                        "Dernier rappel de retrait",
                        $"Bonjour {order.Client.FirstName}, votre commande {order.TicketNumber} est en attente de retrait depuis 14 jours. Merci de nous contacter."
                    ));
            }

            if (order.Balance > 0.005m)
            {
                reminders.Add((
                    "unpaid-balance",
                    "Rappel de solde impayé",
                    $"Bonjour {order.Client.FirstName}, le solde restant de votre commande {order.TicketNumber} est de {order.Balance:N0} FCFA."
                ));
            }

            if (order.DueDate.Date == now.Date.AddDays(1))
            {
                reminders.Add((
                    "due-tomorrow",
                    "Rappel : échéance de votre commande",
                    $"Bonjour {order.Client.FirstName}, votre commande {order.TicketNumber} a une échéance prévue demain."
                ));
            }

            foreach (var reminder in reminders)
            {
                string key = $"{order.Id}:{reminder.Type}";

                if (sentKeys.Contains(key))
                    continue;

                bool delivered = false;
                string channel = string.Empty;

                string phone = !string.IsNullOrWhiteSpace(order.Client.WhatsApp)
                    ? order.Client.WhatsApp
                    : order.Client.Phone;

                if (!string.IsNullOrWhiteSpace(phone))
                {
                    try
                    {
                        WhatsAppSendResult result = await _whatsApp.SendAsync(
                            new WhatsAppMessage(phone, reminder.Body), ct);

                        if (result.Success)
                        {
                            delivered = true;
                            channel = result.Simulated
                                ? "whatsapp-simulated"
                                : "whatsapp";
                        }
                        else
                        {
                            _logger.LogWarning(
                                "WhatsApp échoué pour la commande {OrderId}: {Error}",
                                order.Id, result.Error);
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex,
                            "Erreur WhatsApp pour la commande {OrderId}", order.Id);
                    }
                }

                if (!delivered &&
                    !string.IsNullOrWhiteSpace(order.Client.Email))
                {
                    try
                    {
                        string html = $"<p>{WebUtility.HtmlEncode(reminder.Body)}</p>";

                        await _email.QueueRawAsync(
                            order.Client.Email,
                            order.Client.FullName,
                            reminder.Subject,
                            html,
                            reminder.Body,
                            order.ClientId,
                            order.Id,
                            ct);

                        delivered = true;
                        channel = "email-queued";
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex,
                            "Mise en attente de l'e-mail échouée pour la commande {OrderId}",
                            order.Id);
                    }
                }

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
}




