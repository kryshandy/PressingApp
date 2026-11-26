using System.Net;
using System.Net.Http.Json;
using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Tests.Support;

namespace Pressing.Tests.Integration;

/// <summary>Correctifs de la revue : jetons révoqués, IP derrière un proxy, validations (400), statuts, tickets.</summary>
[Collection("Api")]
public class ReviewFixesApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ReviewFixesApiTests(ApiFactory factory) => _factory = factory;

    /// <summary>Client typé dont toutes les requêtes portent une IP source simulée (limitation de débit isolée).</summary>
    private PressingApiClient ApiFrom(string ip)
    {
        HttpClient http = _factory.CreateClient();
        http.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
        return new PressingApiClient(http);
    }

    private static string UniquePhone()
    {
        string digits = Random.Shared.NextInt64(10_000_000, 99_999_999).ToString();
        return $"+225 09 {digits[..2]} {digits[2..4]} {digits[4..6]} {digits[6..]}";
    }

    private static async Task<ClientDto> CreateClientAsync(PressingApiClient api, string? password = null)
    {
        string phone = UniquePhone();
        return (await api.CreateClientAsync(new CreateClientRequest(
            "Revue", "Test", phone, phone, $"revue-{Guid.NewGuid():N}@example.com", "Cocody", null, password, false)))!;
    }

    private static async Task<OrderDto> CreateOrderAsync(PressingApiClient api, int clientId, int quantity = 1)
    {
        CategoryDto chemise = (await api.GetCatalogAsync())!.Single(c => c.Name == "Chemise");
        return (await api.CreateOrderAsync(new CreateOrderRequest(
            clientId,
            [new CreateOrderItemRequest(chemise.Id, ServiceType.WashAndIron, quantity, null, false, null, null, null)],
            false, null, DeliveryType.StorePickup, null, 0, 0, null, 0, null, 0, PaymentMethod.Cash, null)))!;
    }

    // ---------- 3. Jetons révoqués ----------

    [Fact]
    public async Task Jeton_du_personnel_desactive_est_refuse_immediatement()
    {
        PressingApiClient admin = await _factory.AdminAsync();
        string username = "revoque" + Guid.NewGuid().ToString("N")[..8];
        StaffUserDto created = (await admin.CreateStaffAsync(
            new CreateStaffRequest("Agent Révoqué", username, null, UserRole.Cashier, "Revoque@2026!")))!;

        PressingApiClient agent = ApiFrom("10.20.0.1");
        await agent.LoginStaffAsync(username, "Revoque@2026!");
        Assert.NotNull(await agent.GetDashboardAsync()); // le jeton fonctionne

        await admin.UpdateStaffAsync(created.Id,
            new UpdateStaffRequest(created.FullName, created.Username, created.Email, created.Role, false));

        ApiException ex = await Assert.ThrowsAsync<ApiException>(() => agent.GetDashboardAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task Jeton_du_personnel_dont_le_role_a_change_est_refuse()
    {
        PressingApiClient admin = await _factory.AdminAsync();
        string username = "role" + Guid.NewGuid().ToString("N")[..8];
        StaffUserDto created = (await admin.CreateStaffAsync(
            new CreateStaffRequest("Agent Rôle", username, null, UserRole.Cashier, "Revoque@2026!")))!;

        PressingApiClient agent = ApiFrom("10.20.0.2");
        await agent.LoginStaffAsync(username, "Revoque@2026!");
        Assert.NotNull(await agent.GetDashboardAsync());

        await admin.UpdateStaffAsync(created.Id,
            new UpdateStaffRequest(created.FullName, created.Username, created.Email, UserRole.Manager, true));

        ApiException ex = await Assert.ThrowsAsync<ApiException>(() => agent.GetDashboardAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);

        // Une nouvelle connexion donne un jeton au nouveau rôle, valide.
        PressingApiClient again = ApiFrom("10.20.0.2");
        AuthResponse? auth = await again.LoginStaffAsync(username, "Revoque@2026!");
        Assert.Equal(UserRole.Manager, auth!.Staff!.Role);
        Assert.NotNull(await again.GetDashboardAsync());
    }

    [Fact]
    public async Task Jeton_client_desactive_ou_sans_acces_web_est_refuse()
    {
        PressingApiClient cashier = await _factory.CashierAsync();
        ClientDto client = await CreateClientAsync(cashier, "Client@2026!");

        PressingApiClient portal = ApiFrom("10.20.0.3");
        await portal.LoginClientAsync(client.Phone, "Client@2026!");
        Assert.NotNull(await portal.GetPortalDashboardAsync());

        await cashier.SetClientPasswordAsync(client.Id, new SetClientPasswordRequest(null, false, false));
        ApiException ex = await Assert.ThrowsAsync<ApiException>(() => portal.GetPortalDashboardAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);

        // Réactivation de l'accès puis désactivation du compte.
        await cashier.SetClientPasswordAsync(client.Id, new SetClientPasswordRequest("Client@2026!", true, false));
        PressingApiClient second = ApiFrom("10.20.0.3");
        await second.LoginClientAsync(client.Phone, "Client@2026!");
        Assert.NotNull(await second.GetPortalDashboardAsync());

        await cashier.UpdateClientAsync(client.Id, new UpdateClientRequest(
            client.FirstName, client.LastName, client.Phone, client.WhatsApp, client.Email, client.Address, null, false));
        ex = await Assert.ThrowsAsync<ApiException>(() => second.GetPortalDashboardAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    // ---------- 4. IP derrière le site web ----------

    [Fact]
    public async Task Limitation_de_debit_partitionnee_par_IP_transmise()
    {
        HttpClient first = _factory.CreateClient();
        first.DefaultRequestHeaders.Add("X-Forwarded-For", "10.0.0.1");
        var statuses = new List<HttpStatusCode>();
        for (int i = 0; i < 13; i++)
        {
            HttpResponseMessage r = await first.PostAsJsonAsync(
                "/api/auth/staff/login", new { username = "fantome-limite", password = "x" });
            statuses.Add(r.StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]); // le visiteur 10.0.0.1 est limité…

        HttpClient second = _factory.CreateClient();
        second.DefaultRequestHeaders.Add("X-Forwarded-For", "10.0.0.2");
        HttpResponseMessage ok = await second.PostAsJsonAsync(
            "/api/auth/staff/login", new { username = "caisse", password = ApiFactory.CashierPassword });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode); // …mais pas le visiteur 10.0.0.2.
    }

    [Fact]
    public async Task Client_type_transmet_ForwardedFor_en_X_Forwarded_For()
    {
        var handler = new CaptureHandler();
        var api = new PressingApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") })
        {
            ForwardedFor = " 203.0.113.9 "
        };

        await api.GetHealthAsync();
        Assert.Equal("203.0.113.9", handler.LastForwardedFor);

        api.ForwardedFor = null;
        await api.GetHealthAsync();
        Assert.Null(handler.LastForwardedFor);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? LastForwardedFor { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastForwardedFor = request.Headers.TryGetValues("X-Forwarded-For", out var values) ? values.Single() : null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { status = "ok" })
            });
        }
    }

    // ---------- 1 / 9. Statuts manuels et validations : 400, jamais 500 ----------

    [Fact]
    public async Task Statut_PickedUp_ou_Cancelled_par_l_API_donne_400_et_ne_change_rien()
    {
        PressingApiClient cashier = await _factory.CashierAsync();
        ClientDto client = await CreateClientAsync(cashier);
        OrderDto order = await CreateOrderAsync(cashier, client.Id);

        foreach (ItemStatus status in new[] { ItemStatus.PickedUp, ItemStatus.Cancelled })
        {
            ApiException one = await Assert.ThrowsAsync<ApiException>(() =>
                cashier.UpdateItemStatusAsync(order.Id, order.Items[0].Id, status));
            Assert.Equal(HttpStatusCode.BadRequest, one.StatusCode);
            Assert.Contains("retrait ou l'annulation", one.Message);

            ApiException all = await Assert.ThrowsAsync<ApiException>(() => cashier.UpdateOrderStatusAsync(order.Id, status));
            Assert.Equal(HttpStatusCode.BadRequest, all.StatusCode);
        }

        OrderDto reloaded = (await cashier.GetOrderAsync(order.Id))!;
        Assert.Equal(OrderStatus.Deposited, reloaded.Status);
        Assert.All(reloaded.Items, i => Assert.Equal(ItemStatus.Deposited, i.Status));
    }

    [Fact]
    public async Task Entrees_hors_bornes_donnent_400_et_jamais_500()
    {
        PressingApiClient cashier = await _factory.CashierAsync();
        ClientDto client = await CreateClientAsync(cashier);
        CategoryDto chemise = (await cashier.GetCatalogAsync())!.Single(c => c.Name == "Chemise");

        CreateOrderRequest Order(int quantity, decimal discount = 0, decimal deposit = 0, decimal? unitPrice = null) => new(
            client.Id,
            [new CreateOrderItemRequest(chemise.Id, ServiceType.WashAndIron, quantity, unitPrice, false, null, null, null)],
            false, null, DeliveryType.StorePickup, null, 0, discount, null, 0, null, deposit, PaymentMethod.Cash, null);

        foreach (CreateOrderRequest bad in new[]
                 {
                     Order(5000), Order(0), Order(-2), Order(1, discount: -5), Order(1, deposit: -1),
                     Order(1, unitPrice: 99_999_999m), Order(1, discount: 99_999_999m)
                 })
        {
            ApiException ex = await Assert.ThrowsAsync<ApiException>(() => cashier.CreateOrderAsync(bad));
            Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        }

        OrderDto order = await CreateOrderAsync(cashier, client.Id);
        ApiException payment = await Assert.ThrowsAsync<ApiException>(() => cashier.AddPaymentAsync(order.Id,
            new AddPaymentRequest(99_999_999m, PaymentMethod.Cash, null, null, null)));
        Assert.Equal(HttpStatusCode.BadRequest, payment.StatusCode);

        PressingApiClient manager = await _factory.ManagerAsync();
        ApiException refund = await Assert.ThrowsAsync<ApiException>(() => manager.RefundAsync(order.Id,
            new RefundRequest(99_999_999m, PaymentMethod.Cash, null)));
        Assert.Equal(HttpStatusCode.BadRequest, refund.StatusCode);
    }

    // ---------- 6. Téléphone canonique ----------

    [Fact]
    public async Task Connexion_client_avec_le_numero_canonique_ou_avec_l_indicatif()
    {
        foreach (string identifier in new[] { "0711223344", "+225 07 11 22 33 44", "00225 0711223344" })
        {
            PressingApiClient portal = ApiFrom("10.30.0." + Random.Shared.Next(1, 250));
            AuthResponse? auth = await portal.LoginClientAsync(identifier, ApiFactory.ClientPassword);
            Assert.Equal("Awa", auth!.Client!.FirstName);
        }
    }

    // ---------- 12. Ticket insensible à la casse ----------

    [Fact]
    public async Task Ticket_en_minuscules_fonctionne_pour_le_suivi_public_et_la_recherche_du_personnel()
    {
        PressingApiClient cashier = await _factory.CashierAsync();
        ClientDto client = await CreateClientAsync(cashier);
        OrderDto order = await CreateOrderAsync(cashier, client.Id);
        string lower = " " + order.TicketNumber.ToLowerInvariant() + " ";

        OrderDto? byTicket = await cashier.GetOrderByTicketAsync(lower);
        Assert.Equal(order.Id, byTicket!.Id);

        PressingApiClient anonymous = ApiFrom("10.40.0.1");
        TrackingDto? tracking = await anonymous.TrackOrderAsync(order.TicketNumber.ToLowerInvariant(), client.Phone);
        Assert.NotNull(tracking);
        Assert.Equal(order.TicketNumber, tracking!.TicketNumber);
    }
}
