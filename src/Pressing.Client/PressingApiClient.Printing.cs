using Pressing.Core.Dtos;

namespace Pressing.Client;

public sealed partial class PressingApiClient
{
    #region Impression thermique

    /// <summary>Octets ESC/POS du ticket 80 mm d'une commande : <c>GET /api/orders/{id}/receipt/escpos</c>.</summary>
    public Task<byte[]> GetThermalTicketAsync(int orderId, CancellationToken ct = default) =>
        GetBytesAsync($"api/orders/{orderId}/receipt/escpos", ct);

    /// <summary>Imprimante de tickets configurée (lisible par tout le personnel) : <c>GET /api/printing/ticket-printer</c>.</summary>
    public Task<TicketPrinterSettingsDto?> GetTicketPrinterAsync(CancellationToken ct = default) =>
        GetAsync<TicketPrinterSettingsDto>("api/printing/ticket-printer", ct);

    #endregion
}
