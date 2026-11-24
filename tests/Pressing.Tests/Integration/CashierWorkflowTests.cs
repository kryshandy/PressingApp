using System.Net;
using System.Text;
using System.Text.Json;
using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Tests.Support;

namespace Pressing.Tests.Integration;

/// <summary>Parcours complet d'un caissier (client, devis, dépôt, traitement, encaissement, retrait, reçu).</summary>
[Collection("Api")]
public class CashierWorkflowTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public CashierWorkflowTests(ApiFactory factory) => _factory = factory;

    private static string UniquePhone()
    {
        string digits = Random.Shared.NextInt64(10_000_000, 99_999_999).ToString();
        return $"+225 09 {digits[..2]} {digits[2..4]} {digits[4..6]} {digits[6..]}";
    }

    private static async Task<ClientDto> CreateClientAsync(PressingApiClient api, string last)
    {
        string phone = UniquePhone();
        ClientDto? client = await api.CreateClientAsync(new CreateClientRequest(
            "Flux", last, phone, phone, $"{last.ToLowerInvariant()}-{Guid.NewGuid():N}@example.com",
            "Cocody", null, null, false));
        return client!;
    }

    private static async Task<List<CreateOrderItemRequest>> ItemsAsync(PressingApiClient api, int chemises, int pantalons)
    {
        List<CategoryDto> catalog = (await api.GetCatalogAsync())!;
        CategoryDto chemise = catalog.Single(c => c.Name == "Chemise");
        CategoryDto pantalon = catalog.Single(c => c.Name == "Pantalon");
        var items = new List<CreateOrderItemRequest>();
        if (chemises > 0)
            items.Add(new(chemise.Id, ServiceType.WashAndIron, chemises, null, false, null, null, null));
        if (pantalons > 0)
            items.Add(new(pantalon.Id, ServiceType.WashAndIron, pantalons, null, false, null, null, null));
        return items;
    }

    private static CreateOrderRequest NewOrder(int clientId, List<CreateOrderItemRequest> items, decimal deposit = 0) =>
        new(clientId, items, false, null, DeliveryType.StorePickup, null, 0, 0, null, 0, null,
            deposit, PaymentMethod.Cash, null);

    [Fact]
    public async Task Flux_complet_de_caisse()
    {
        PressingApiClient api = await _factory.CashierAsync();

        // 1. Client
        ClientDto client = await CreateClientAsync(api, "Parcours");
        Assert.True(client.Id > 0);
        Assert.True(client.IsActive);

        // 2. Devis
        List<CreateOrderItemRequest> items = await ItemsAsync(api, chemises: 2, pantalons: 1);
        OrderQuoteDto? quote = await api.QuoteOrderAsync(new OrderQuoteRequest(
            items, false, DeliveryType.StorePickup, 0, 0, null, 0, client.Id));
        Assert.Equal(3500m, quote!.Total);

        // 3. Commande avec acompte
        OrderDto? created = await api.CreateOrderAsync(NewOrder(client.Id, items, deposit: 1000));
        OrderDto order = created!;
        Assert.Matches(@"^PRESS-\d{4}-\d{5}$", order.TicketNumber);
        Assert.Equal(quote.Total, order.Total);
        Assert.Equal(1000m, order.PaidAmount);
        Assert.Equal(2500m, order.Balance);
        Assert.Equal(OrderStatus.Deposited, order.Status);
        Assert.Equal(client.Id, order.ClientId);

        // 4. Articles à « Prêt » un par un
        OrderDto? step = null;
        foreach (OrderItemDto item in order.Items)
        {
            step = await api.UpdateItemStatusAsync(order.Id, item.Id, ItemStatus.Ready);
        }

        Assert.Equal(OrderStatus.Ready, step!.Status);
        Assert.Contains(await api.GetReadyOrdersAsync() ?? [], o => o.Id == order.Id);

        // 5. Encaissement du solde
        PaymentDto? payment = await api.AddPaymentAsync(order.Id,
            new AddPaymentRequest(2500, PaymentMethod.MobileMoney, "OM-123", "solde", null));
        Assert.Equal(2500m, payment!.Amount);
        Assert.Equal(PaymentType.Payment, payment.Type);
        Assert.Equal(PaymentMethod.MobileMoney, payment.Method);

        OrderDto paid = (await api.GetOrderAsync(order.Id))!;
        Assert.Equal(0m, paid.Balance);
        Assert.Equal(3500m, paid.PaidAmount);

        // 6. Retrait de tous les articles
        OrderDto? done = await api.PickupAsync(order.Id, new PickupRequest(
            paid.Items.Select(i => new PickupItemRequest(i.Id, i.Quantity)).ToList(),
            0, PaymentMethod.Cash, null, "Retrait client"));

        Assert.Equal(OrderStatus.Completed, done!.Status);
        Assert.NotNull(done.CompletedAt);
        Assert.All(done.Items, i => Assert.Equal(ItemStatus.PickedUp, i.Status));

        // 7. Reçu PDF
        byte[] pdf = await api.GetReceiptPdfAsync(order.Id);
        Assert.True(pdf.Length > 500);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));

        // 8. Les deux encaissements apparaissent dans /api/payments
        PagedResult<PaymentDto>? payments = await api.GetPaymentsAsync(search: order.TicketNumber);
        Assert.Equal(2, payments!.Items.Count(p => p.OrderId == order.Id));
        Assert.Equal(3500m, payments.Items.Where(p => p.OrderId == order.Id).Sum(p => p.Amount));

        // 9. Recherche par ticket
        OrderDto? byTicket = await api.GetOrderByTicketAsync(order.TicketNumber);
        Assert.Equal(order.Id, byTicket!.Id);
        Assert.Null(await api.GetOrderByTicketAsync("PRESS-1999-00000"));
    }

    [Fact]
    public async Task Trop_percu_donne_400_avec_message_lisible()
    {
        PressingApiClient api = await _factory.CashierAsync();
        ClientDto client = await CreateClientAsync(api, "Surplus");
        OrderDto order = (await api.CreateOrderAsync(NewOrder(client.Id, await ItemsAsync(api, 1, 0), deposit: 500)))!;

        ApiException ex = await Assert.ThrowsAsync<ApiException>(() => api.AddPaymentAsync(order.Id,
            new AddPaymentRequest(600, PaymentMethod.Cash, null, null, null)));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("solde", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(500m, (await api.GetOrderAsync(order.Id))!.PaidAmount);
    }

    [Fact]
    public async Task Retrait_avec_solde_est_refuse_par_l_api()
    {
        PressingApiClient api = await _factory.CashierAsync();
        ClientDto client = await CreateClientAsync(api, "Solde");
        OrderDto order = (await api.CreateOrderAsync(NewOrder(client.Id, await ItemsAsync(api, 1, 0), deposit: 300)))!;
        order = (await api.UpdateOrderStatusAsync(order.Id, ItemStatus.Ready))!;
        Assert.Equal(OrderStatus.Ready, order.Status);

        ApiException ex = await Assert.ThrowsAsync<ApiException>(() => api.PickupAsync(order.Id,
            new PickupRequest(order.Items.Select(i => new PickupItemRequest(i.Id, i.Quantity)).ToList(),
                0, PaymentMethod.Cash, null, null)));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("solde", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(OrderStatus.Ready, (await api.GetOrderAsync(order.Id))!.Status);
    }

    [Fact]
    public async Task Impayes_listent_la_commande_jusqu_a_son_paiement_complet()
    {
        PressingApiClient api = await _factory.CashierAsync();
        ClientDto client = await CreateClientAsync(api, "Impaye");
        OrderDto order = (await api.CreateOrderAsync(NewOrder(client.Id, await ItemsAsync(api, 1, 1), deposit: 1000)))!;

        List<OrderSummaryDto> outstanding = (await api.GetOutstandingOrdersAsync(take: 200))!;
        OrderSummaryDto line = Assert.Single(outstanding, o => o.Id == order.Id);
        Assert.Equal(1500m, line.Balance);

        PagedResult<OrderSummaryDto> filtered = (await api.SearchOrdersAsync(onlyOutstanding: true, clientId: client.Id))!;
        Assert.Contains(filtered.Items, o => o.Id == order.Id);

        await api.AddPaymentAsync(order.Id, new AddPaymentRequest(1500, PaymentMethod.Cash, null, null, null));

        outstanding = (await api.GetOutstandingOrdersAsync(take: 200))!;
        Assert.DoesNotContain(outstanding, o => o.Id == order.Id);
    }

    [Fact]
    public async Task Annulation_par_le_manager_rembourse_et_le_caissier_ne_peut_pas_annuler()
    {
        PressingApiClient cashier = await _factory.CashierAsync();
        PressingApiClient manager = await _factory.ManagerAsync();
        ClientDto client = await CreateClientAsync(cashier, "Annule");
        OrderDto order = (await cashier.CreateOrderAsync(NewOrder(client.Id, await ItemsAsync(cashier, 1, 0), deposit: 400)))!;

        ApiException forbidden = await Assert.ThrowsAsync<ApiException>(() => cashier.CancelOrderAsync(order.Id, "test"));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        OrderDto cancelled = (await manager.CancelOrderAsync(order.Id, "Demande du client"))!;
        Assert.Equal(OrderStatus.Cancelled, cancelled.Status);
        Assert.Equal(0m, cancelled.PaidAmount);
        Assert.Contains(cancelled.Payments, p => p.Type == PaymentType.Refund && p.Amount == 400m);
    }

    [Fact]
    public async Task Client_en_double_est_refuse()
    {
        PressingApiClient api = await _factory.CashierAsync();
        string phone = UniquePhone();
        var request = new CreateClientRequest("Dup", "Licat", phone, phone, null, null, null, null, false);
        await api.CreateClientAsync(request);

        ApiException ex = await Assert.ThrowsAsync<ApiException>(() => api.CreateClientAsync(request));
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task Les_enums_sont_serialises_en_texte()
    {
        PressingApiClient api = await _factory.CashierAsync();
        ClientDto client = await CreateClientAsync(api, "Enum");
        OrderDto order = (await api.CreateOrderAsync(NewOrder(client.Id, await ItemsAsync(api, 1, 0), deposit: 200)))!;

        // Lecture brute de la réponse, sans passer par la désérialisation typée.
        HttpClient http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", api.Token);
        using JsonDocument doc = JsonDocument.Parse(await http.GetStringAsync($"/api/orders/{order.Id}"));
        JsonElement root = doc.RootElement;

        Assert.Equal(JsonValueKind.String, root.GetProperty("status").ValueKind);
        Assert.Equal("Deposited", root.GetProperty("status").GetString());
        Assert.Equal("StorePickup", root.GetProperty("deliveryType").GetString());

        JsonElement item = root.GetProperty("items")[0];
        Assert.Equal("WashAndIron", item.GetProperty("serviceType").GetString());
        Assert.Equal("Deposited", item.GetProperty("status").GetString());

        JsonElement payment = root.GetProperty("payments")[0];
        Assert.Equal("Payment", payment.GetProperty("type").GetString());
        Assert.Equal("Cash", payment.GetProperty("method").GetString());

        // Les noms de propriétés sont en camelCase.
        Assert.True(root.TryGetProperty("ticketNumber", out _));
        Assert.False(root.TryGetProperty("TicketNumber", out _));

        // Et l'API accepte les énumérations en texte dans les requêtes.
        var payload = new StringContent(
            """{"amount":100,"method":"MobileMoney","reference":null,"note":null,"paidAt":null}""",
            Encoding.UTF8, "application/json");
        HttpResponseMessage response = await http.PostAsync($"/api/orders/{order.Id}/payments", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"MobileMoney\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Valeur_d_enum_inconnue_donne_400()
    {
        PressingApiClient api = await _factory.CashierAsync();
        HttpClient http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", api.Token);

        var payload = new StringContent("""{"status":"Teleporte"}""", Encoding.UTF8, "application/json");
        HttpResponseMessage response = await http.PutAsync("/api/orders/1/status", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Tableau_de_bord_et_catalogue_sont_disponibles_pour_le_personnel()
    {
        PressingApiClient api = await _factory.CashierAsync();

        DashboardDto? dashboard = await api.GetDashboardAsync();
        Assert.NotNull(dashboard);
        Assert.NotNull(dashboard!.RecentOrders);

        List<CategoryDto>? catalog = await api.GetCatalogAsync();
        Assert.True(catalog!.Count >= 20);
        Assert.All(catalog.Where(c => c.IsActive), c => Assert.Equal(5, c.Prices.Count));
    }
}
