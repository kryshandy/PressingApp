namespace Pressing.Core.Dtos;

/// <summary>Une ligne du journal des rappels (automatiques ou manuels).</summary>
public record ReminderLogDto(
    int Id,
    int OrderId,
    string TicketNumber,
    int ClientId,
    string ClientName,
    string ReminderType,
    string TypeLabel,
    string Channel,
    string ChannelLabel,
    DateTime SentAt,
    bool Success,
    string? Error);
