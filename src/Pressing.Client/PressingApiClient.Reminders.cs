using Pressing.Core.Dtos;

namespace Pressing.Client;

// Rappels clients (responsable ou administrateur) : journal d'envoi et relance manuelle.
public sealed partial class PressingApiClient
{
    /// <summary>Journal des rappels. <paramref name="channel"/> : « whatsapp » ou « email » ; <paramref name="type"/> : ex. « unpaid-balance ».</summary>
    public Task<PagedResult<ReminderLogDto>?> GetRemindersAsync(int page = 1, int pageSize = 30,
        string? type = null, string? channel = null, DateTime? from = null, DateTime? to = null,
        CancellationToken ct = default) =>
        GetAsync<PagedResult<ReminderLogDto>>("api/reminders" + Query(("page", page), ("pageSize", pageSize),
            ("type", type), ("channel", channel), ("from", from), ("to", to)), ct);

    /// <summary>Relance manuellement le client d'une commande (refusée par l'API sans consentement).</summary>
    public Task<ReminderLogDto?> SendReminderAsync(int orderId, CancellationToken ct = default) =>
        PostAsync<ReminderLogDto>($"api/reminders/orders/{orderId}/send", null, ct);
}
