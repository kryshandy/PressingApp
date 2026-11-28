using System.Net;
using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Tests.Support;

namespace Pressing.Tests.Integration;

/// <summary>Routes d'atelier : étiquettes PDF, scan et annulation (rôles, 404, enchaînement).</summary>
[Collection("Api")]
public class WorkshopApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public WorkshopApiTests(ApiFactory factory) => _factory = factory;

    private static async Task<OrderDto> CreateOrderAsync(PressingApiClient api)
    {
        string phone = "+225 09 " + Random.Shared.NextInt64(10_000_000, 99_999_999).ToString().Insert(2, " ").Insert(5, " ").Insert(8, " ");
        ClientDto client = (await api.CreateClientAsync(new CreateClientRequest(
            "Atelier", "Scan", phone, phone, $"scan-{Guid.NewGuid():N}@example.com", "Cocody", null, null, false)))!;
        CategoryDto chemise = (await api.GetCatalogAsync())!.Single(c => c.Name == "Chemise");
        return (await api.CreateOrderAsync(new CreateOrderRequest(
            client.Id,
            [new CreateOrderItemRequest(chemise.Id, ServiceType.WashAndIron, 2, null, false, null, null, null)],
            false, null, DeliveryType.StorePickup, null, 0, 0, null, 0, null, 0, PaymentMethod.Cash, null)))!;
    }

    [Fact]
    public async Task Etiquettes_renvoient_un_pdf()
    {
        PressingApiClient api = await _factory.CashierAsync();
        OrderDto order = await CreateOrderAsync(api);

        byte[] pdf = await api.GetLabelsPdfAsync(order.Id);

        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public async Task Etiquettes_commande_inconnue_donne_404()
    {
        PressingApiClient api = await _factory.CashierAsync();
        ApiException ex = await Assert.ThrowsAsync<ApiException>(() => api.GetLabelsPdfAsync(987654));
        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task Routes_atelier_refusees_a_un_client_du_portail_et_sans_jeton()
    {
        PressingApiClient portal = await _factory.PortalClientAsync("+225 07 11 22 33 44");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await Assert.ThrowsAsync<ApiException>(() => portal.GetLabelsPdfAsync(1))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Assert.ThrowsAsync<ApiException>(() => portal.WorkshopScanAsync("PRESS-2026-00001-01"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Assert.ThrowsAsync<ApiException>(() => portal.WorkshopUndoAsync("PRESS-2026-00001-01", ItemStatus.Ready))).StatusCode);

        PressingApiClient anonymous = _factory.NewApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Assert.ThrowsAsync<ApiException>(() => anonymous.WorkshopScanAsync("PRESS-2026-00001-01"))).StatusCode);
    }

    [Fact]
    public async Task Scan_puis_annulation_via_l_api()
    {
        PressingApiClient api = await _factory.CashierAsync();
        OrderDto order = await CreateOrderAsync(api);
        string code = $"{order.TicketNumber}-01-a";

        WorkshopScanResultDto first = (await api.WorkshopScanAsync(code))!;
        Assert.Equal(ItemStatus.InProgress, first.NewStatus);
        Assert.Equal(order.TicketNumber, first.TicketNumber);

        WorkshopScanResultDto second = (await api.WorkshopScanAsync(code))!;
        Assert.Equal(ItemStatus.Ready, second.NewStatus);
        Assert.True(second.OrderReady);

        ApiException refused = await Assert.ThrowsAsync<ApiException>(() => api.WorkshopScanAsync(code));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("Déjà prêt", refused.Message);

        WorkshopScanResultDto undo = (await api.WorkshopUndoAsync(code, ItemStatus.Ready))!;
        Assert.Equal(ItemStatus.InProgress, undo.NewStatus);
        Assert.Equal(OrderStatus.InProgress, (await api.GetOrderAsync(order.Id))!.Status);
    }

    [Fact]
    public async Task Scan_ticket_inconnu_donne_404_et_code_illisible_400()
    {
        PressingApiClient api = await _factory.CashierAsync();

        Assert.Equal(HttpStatusCode.NotFound,
            (await Assert.ThrowsAsync<ApiException>(() => api.WorkshopScanAsync("PRESS-2026-99999-01"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Assert.ThrowsAsync<ApiException>(() => api.WorkshopScanAsync("abc"))).StatusCode);
    }
}
