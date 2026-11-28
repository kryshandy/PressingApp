using Pressing.Core.Dtos;

namespace Pressing.Client;

// Clôture de caisse journalière (« Z de caisse ») — voir docs/API.md, section « Clôture de caisse ».
public sealed partial class PressingApiClient
{
    /// <summary>Montants attendus du jour (paiements − remboursements par mode), sans rien enregistrer.</summary>
    public Task<CashClosingPreviewDto?> PreviewCashClosingAsync(DateTime date, CancellationToken ct = default) =>
        GetAsync<CashClosingPreviewDto>("api/cash-closings/preview" + Query(("date", date)), ct);

    public Task<CashClosingDto?> CreateCashClosingAsync(CashClosingRequest request, CancellationToken ct = default) =>
        PostAsync<CashClosingDto>("api/cash-closings", request, ct);

    /// <summary>Modifie un brouillon ; une clôture validée est refusée (400).</summary>
    public Task<CashClosingDto?> UpdateCashClosingAsync(int id, CashClosingRequest request, CancellationToken ct = default) =>
        PutAsync<CashClosingDto>($"api/cash-closings/{id}", request, ct);

    /// <summary>Validation par un manager ou un administrateur (403 pour un caissier).</summary>
    public Task<CashClosingDto?> ValidateCashClosingAsync(int id, CancellationToken ct = default) =>
        PostAsync<CashClosingDto>($"api/cash-closings/{id}/validate", null, ct);

    /// <summary>Historique des clôtures (Manager/Admin) ; 90 derniers jours par défaut.</summary>
    public Task<List<CashClosingDto>?> GetCashClosingsAsync(DateTime? from = null, DateTime? to = null, CancellationToken ct = default) =>
        GetAsync<List<CashClosingDto>>("api/cash-closings" + Query(("from", from), ("to", to)), ct);

    public Task<CashClosingDto?> GetCashClosingAsync(int id, CancellationToken ct = default) =>
        GetAsync<CashClosingDto>($"api/cash-closings/{id}", ct);

    public Task<byte[]> GetCashClosingPdfAsync(int id, CancellationToken ct = default) =>
        GetBytesAsync($"api/cash-closings/{id}/pdf", ct);
}
