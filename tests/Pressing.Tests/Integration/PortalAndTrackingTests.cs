using System.Net;
using System.Text;
using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Tests.Support;

namespace Pressing.Tests.Integration;

/// <summary>Espace client (portail) et suivi public par ticket + téléphone, sur les données de démonstration.</summary>
[Collection("Api")]
public class PortalAndTrackingTests : IClassFixture<ApiFactory>
{
    private const string AwaPhone = "+225 07 11 22 33 44";
    private const string KoffiPhone = "+225 05 22 33 44 55";

    private readonly ApiFactory _factory;

    public PortalAndTrackingTests(ApiFactory factory) => _factory = factory;

    private async Task<ClientDto> FindClientAsync(PressingApiClient staff, string search)
    {
        List<ClientDto> found = (await staff.LookupClientsAsync(search))!;
        return Assert.Single(found);
    }

    [Fact]
    public async Task Portail_connexion_par_telephone_et_tableau_de_bord()
    {
        PressingApiClient portal = await _factory.PortalClientAsync(AwaPhone);

        PortalDashboardDto? dashboard = await portal.GetPortalDashboardAsync();
        Assert.NotNull(dashboard);
        Assert.Equal("Awa", dashboard!.Client.FirstName);
        Assert.Equal("Koné", dashboard.Client.LastName);
        Assert.All(dashboard.RecentOrders, o => Assert.Equal(dashboard.Client.Id, o.ClientId));
        Assert.All(dashboard.ActiveOrders, o =>
            Assert.True(o.Status is not (OrderStatus.Completed or OrderStatus.Cancelled)));
        Assert.True(dashboard.RecentOrders.Count >= 1);
        Assert.True(dashboard.Balance >= 0);

        MeDto? me = await portal.GetMeAsync();
        Assert.Equal("client", me!.Type);
        Assert.Equal(dashboard.Client.Id, me.Client!.Id);

        ClientDto? profile = await portal.GetProfileAsync();
        Assert.Equal(dashboard.Client.Id, profile!.Id);
        Assert.NotNull(await portal.GetPortalLoyaltyAsync());
    }

    [Fact]
    public async Task Portail_liste_uniquement_les_commandes_du_client()
    {
        PressingApiClient portal = await _factory.PortalClientAsync(AwaPhone);
        PressingApiClient staff = await _factory.CashierAsync();
        ClientDto awa = await FindClientAsync(staff, "Koné");

        List<OrderSummaryDto> orders = (await portal.GetPortalOrdersAsync())!;
        Assert.NotEmpty(orders);
        Assert.All(orders, o => Assert.Equal(awa.Id, o.ClientId));

        OrderDto? own = await portal.GetPortalOrderAsync(orders[0].Id);
        Assert.NotNull(own);
        Assert.Equal(orders[0].TicketNumber, own!.TicketNumber);

        byte[] pdf = await portal.GetPortalReceiptPdfAsync(orders[0].Id);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public async Task Portail_ne_peut_pas_lire_la_commande_d_un_autre_client()
    {
        PressingApiClient staff = await _factory.CashierAsync();
        ClientDto koffi = await FindClientAsync(staff, "Yao");
        PagedResult<OrderSummaryDto> koffiOrders = (await staff.SearchOrdersAsync(clientId: koffi.Id))!;
        Assert.NotEmpty(koffiOrders.Items);
        int foreignOrderId = koffiOrders.Items[0].Id;

        PressingApiClient awa = await _factory.PortalClientAsync(AwaPhone);

        // Le client typé traduit le 404 en null...
        Assert.Null(await awa.GetPortalOrderAsync(foreignOrderId));

        // ... et l'API répond bien 404 (pas 403 : on ne révèle pas l'existence de la commande).
        HttpClient http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", awa.Token);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/api/portal/orders/{foreignOrderId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/api/portal/orders/{foreignOrderId}/receipt")).StatusCode);

        ApiException ex = await Assert.ThrowsAsync<ApiException>(() => awa.GetPortalReceiptPdfAsync(foreignOrderId));
        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);

        // La commande, elle, existe bien côté personnel.
        Assert.NotNull(await staff.GetOrderAsync(foreignOrderId));
    }

    [Fact]
    public async Task Portail_connexion_par_email_fonctionne_aussi()
    {
        PressingApiClient portal = await _factory.PortalClientAsync("koffi.yao@example.com");
        PortalDashboardDto? dashboard = await portal.GetPortalDashboardAsync();
        Assert.Equal("Koffi", dashboard!.Client.FirstName);
    }

    [Fact]
    public async Task Client_sans_acces_web_ne_peut_pas_se_connecter()
    {
        // Jean-Marc Bamba : WebAccessEnabled = false dans le seed.
        PressingApiClient api = _factory.NewApiClient();
        ApiException ex = await Assert.ThrowsAsync<ApiException>(
            () => api.LoginClientAsync("jm.bamba@example.com", ApiFactory.ClientPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task Suivi_public_avec_bon_telephone_retourne_la_commande()
    {
        PressingApiClient staff = await _factory.CashierAsync();
        ClientDto awa = await FindClientAsync(staff, "Koné");
        OrderSummaryDto order = (await staff.SearchOrdersAsync(clientId: awa.Id))!.Items[0];

        PressingApiClient anonymous = _factory.NewApiClient();
        TrackingDto? tracking = await anonymous.TrackOrderAsync(order.TicketNumber, awa.Phone);

        Assert.NotNull(tracking);
        Assert.Equal(order.TicketNumber, tracking!.TicketNumber);
        Assert.Equal(order.Status.ToString(), tracking.Status);
        Assert.NotEmpty(tracking.Items);
        Assert.False(string.IsNullOrWhiteSpace(tracking.ShopName));

        // Le format du numéro n'a pas d'importance.
        Assert.NotNull(await anonymous.TrackOrderAsync(order.TicketNumber, "0711223344".Insert(0, "+225")));
    }

    [Fact]
    public async Task Suivi_public_avec_mauvais_telephone_ou_ticket_donne_404()
    {
        PressingApiClient staff = await _factory.CashierAsync();
        ClientDto awa = await FindClientAsync(staff, "Koné");
        OrderSummaryDto order = (await staff.SearchOrdersAsync(clientId: awa.Id))!.Items[0];

        PressingApiClient anonymous = _factory.NewApiClient();
        Assert.Null(await anonymous.TrackOrderAsync(order.TicketNumber, KoffiPhone));
        Assert.Null(await anonymous.TrackOrderAsync("PRESS-1999-00000", awa.Phone));

        HttpClient http = _factory.CreateClient();
        HttpResponseMessage wrong = await http.GetAsync(
            $"/api/public/tracking/{order.TicketNumber}?phone={Uri.EscapeDataString(KoffiPhone)}");
        Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
    }

    [Fact]
    public async Task Suivi_public_sans_telephone_donne_400()
    {
        PressingApiClient staff = await _factory.CashierAsync();
        OrderSummaryDto order = (await staff.SearchOrdersAsync(pageSize: 1))!.Items[0];

        HttpResponseMessage response = await _factory.CreateClient().GetAsync($"/api/public/tracking/{order.TicketNumber}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Informations_publiques_sont_accessibles_sans_jeton()
    {
        PressingApiClient anonymous = _factory.NewApiClient();

        PublicInfoDto? info = await anonymous.GetPublicInfoAsync();
        Assert.NotNull(info);
        Assert.False(string.IsNullOrWhiteSpace(info!.Shop.Name));
        Assert.NotEmpty(info.Categories);

        List<CategoryDto>? catalog = await anonymous.GetPublicCatalogAsync();
        Assert.NotEmpty(catalog!);
    }
}
