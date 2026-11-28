using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pressing.Core.Entities;

namespace Pressing.Core.Services;

// Enquête de satisfaction : même transport (WhatsApp / file d'e-mails), même plage horaire (8 h – 20 h)
// et même journal (ReminderLogs, index unique OrderId + type) que les rappels.
public sealed partial class ReminderService
{
    private const string FeedbackType = "feedback";

    /// <summary>Délai après le retrait complet (statut Completed) avant d'envoyer la demande d'avis.</summary>
    public static readonly TimeSpan FeedbackDelay = TimeSpan.FromHours(2);

    /// <summary>
    /// Au-delà, la demande n'est plus envoyée (serveur arrêté, activation de la fonction : pas de messages en retard
    /// sur d'anciennes commandes). Le lien, lui, reste valable 14 jours.
    /// </summary>
    public static readonly TimeSpan FeedbackWindow = TimeSpan.FromHours(48);

    /// <summary>
    /// Envoie une seule fois, 2 h après le retrait complet, le lien vers la page publique /avis aux clients qui ont
    /// consenti (WhatsApp ou e-mail, ConsentGivenAt, non révoqué). Retourne le nombre de demandes envoyées.
    /// </summary>
    public async Task<int> ProcessFeedbackRequestsAsync(CancellationToken ct = default)
    {
        DateTime now = _timeProvider.GetLocalNow().DateTime;
        if (now.Hour < 8 || now.Hour >= 20)
            return 0;

        DateTime latest = now - FeedbackDelay;
        DateTime earliest = now - FeedbackWindow;

        List<Order> orders = await _db.Orders
            .Include(o => o.Client)
            .Where(o => o.Status == OrderStatus.Completed &&
                        o.CompletedAt != null && o.CompletedAt <= latest && o.CompletedAt >= earliest &&
                        !_db.ReminderLogs.Any(r => r.OrderId == o.Id && r.ReminderType == FeedbackType && r.Success) &&
                        !_db.Feedbacks.Any(f => f.OrderId == o.Id))
            .ToListAsync(ct);

        int processed = 0;
        foreach (Order order in orders)
        {
            ct.ThrowIfCancellationRequested();

            Client? client = order.Client;
            if (client is null || !client.IsActive ||
                client.NotificationPreference == ClientNotificationPreference.None ||
                !client.ConsentGivenAt.HasValue ||
                client.ConsentRevokedAt.HasValue)
                continue;

            // Page servie par le site (PRESSING_WEB_URL) ; le jeton ne donne aucun autre droit.
            string token = _tokens.CreateFeedbackToken(order.Id, order.ClientId);
            string url = $"{_emailOptions.AppBaseUrl}/avis?token={Uri.EscapeDataString(token)}";
            string body = $"Bonjour {client.FirstName}, merci d'avoir confié votre commande {order.TicketNumber} à Pressing Éclat. " +
                          $"Votre avis nous aide à progresser (30 secondes) : {url}";

            (bool delivered, string channel, string? error) = await DeliverAsync(
                order, FeedbackType, "Votre avis sur votre commande", body,
                [client.FirstName, order.TicketNumber, url], ct, url, "Donner mon avis");

            if (!delivered)
            {
                _logger.LogWarning("Demande d'avis non envoyée pour la commande {OrderId} : {Error}", order.Id, error);
                continue;
            }

            _db.ReminderLogs.Add(new ReminderLog
            {
                OrderId = order.Id,
                ClientId = order.ClientId,
                ReminderType = FeedbackType,
                Channel = channel,
                SentAt = now,
                Success = true
            });
            await _db.SaveChangesAsync(ct);
            processed++;
        }

        return processed;
    }
}
