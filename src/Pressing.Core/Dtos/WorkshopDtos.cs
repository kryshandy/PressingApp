namespace Pressing.Core.Dtos;

/// <summary>Code d'étiquette saisi ou scanné (<c>PRESS-AAAA-NNNNN-II[-a]</c>).</summary>
public record WorkshopScanRequest(string Code);

/// <summary>Annulation du dernier scan : <paramref name="FromStatus"/> est le statut obtenu par ce scan.</summary>
public record WorkshopUndoRequest(string Code, ItemStatus FromStatus);

/// <summary>Résultat d'un scan ou d'une annulation, prêt à être affiché en grand dans l'atelier.</summary>
public record WorkshopScanResultDto(
    string Code,
    int OrderId,
    string TicketNumber,
    string ClientName,
    int ItemId,
    string ItemName,
    string ServiceLabel,
    int Rank,
    int ItemCount,
    ItemStatus PreviousStatus,
    ItemStatus NewStatus,
    string NewStatusLabel,
    bool OrderReady,
    string Message);
