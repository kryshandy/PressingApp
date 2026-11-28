namespace Pressing.Client;

public sealed partial class PressingApiClient
{
    /// <summary>Planche d'étiquettes QR A4 (PDF) d'une commande : <c>GET /api/orders/{id}/labels</c>.</summary>
    public Task<byte[]> GetLabelsPdfAsync(int orderId, CancellationToken ct = default) =>
        GetBytesAsync($"api/orders/{orderId}/labels", ct);
}
