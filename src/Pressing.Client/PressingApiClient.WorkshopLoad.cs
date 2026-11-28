using Pressing.Core.Dtos;

namespace Pressing.Client;

public sealed partial class PressingApiClient
{
    #region Charge de l'atelier

    /// <summary>Charge prévisionnelle des <paramref name="days"/> prochains jours : <c>GET /api/workshop/load</c>.</summary>
    public Task<WorkshopLoadDto?> GetWorkshopLoadAsync(int days = 14, CancellationToken ct = default) =>
        GetAsync<WorkshopLoadDto>("api/workshop/load" + Query(("days", days.ToString(System.Globalization.CultureInfo.InvariantCulture))), ct);

    /// <summary>Date de retrait conseillée pour un dépôt : <c>POST /api/workshop/suggest-due-date</c>.</summary>
    public Task<WorkshopDueDateSuggestionDto?> SuggestDueDateAsync(
        IReadOnlyList<WorkshopSuggestLineRequest> items, bool express, CancellationToken ct = default) =>
        PostAsync<WorkshopDueDateSuggestionDto>("api/workshop/suggest-due-date", new WorkshopSuggestDueDateRequest(items, express), ct);

    /// <summary>Change la capacité journalière (Manager/Admin) : <c>PUT /api/workshop/capacity</c> ; la réponse porte la valeur enregistrée.</summary>
    public Task<WorkshopCapacityDto?> SetWorkshopCapacityAsync(int dailyCapacity, CancellationToken ct = default) =>
        PutAsync<WorkshopCapacityDto>("api/workshop/capacity", new WorkshopCapacityDto(dailyCapacity), ct);

    #endregion
}
