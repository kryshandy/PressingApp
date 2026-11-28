using Pressing.Core;
using Pressing.Core.Dtos;

namespace Pressing.Client;

public sealed partial class PressingApiClient
{
    #region Atelier

    /// <summary>Fait avancer l'article d'une étiquette d'une étape : <c>POST /api/workshop/scan</c>.</summary>
    public Task<WorkshopScanResultDto?> WorkshopScanAsync(string code, CancellationToken ct = default) =>
        PostAsync<WorkshopScanResultDto>("api/workshop/scan", new WorkshopScanRequest(code), ct);

    /// <summary>Annule le dernier scan (une étape en arrière) : <c>POST /api/workshop/undo</c>.</summary>
    public Task<WorkshopScanResultDto?> WorkshopUndoAsync(string code, ItemStatus fromStatus, CancellationToken ct = default) =>
        PostAsync<WorkshopScanResultDto>("api/workshop/undo", new WorkshopUndoRequest(code, fromStatus), ct);

    #endregion
}
