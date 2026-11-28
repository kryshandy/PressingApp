using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Data;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Core.Security;
using Pressing.Tests.Support;

namespace Pressing.Tests.Integration;

/// <summary>Routes de l'enquête de satisfaction : pages publiques à jeton, liste réservée au responsable.</summary>
[Collection("Api")]
public class FeedbackApiTests : IClassFixture<ApiFactory>
{
    private static int _counter = 100;
    private readonly ApiFactory _factory;

    public FeedbackApiTests(ApiFactory factory) => _factory = factory;

    private async Task<(int OrderId, int ClientId, string Ticket)> CompletedOrderAsync()
    {
        int n = Interlocked.Increment(ref _counter);
        PressingApiClient staff = await _factory.CashierAsync();
        ClientDto client = (await staff.CreateClientAsync(new CreateClientRequest(
            "Mariam", "Avis" + n, "07330" + n.ToString("00000"), "07330" + n.ToString("00000"),
            $"mariam.avis{n}@example.com", null, null, null, false)))!;

        using IServiceScope scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PressingDbContext>();
        var order = new Order
        {
            TicketNumber = $"PRESS-2026-6{n:0000}",
            ClientId = client.Id,
            Status = OrderStatus.Completed,
            DueDate = DateTime.Today,
            CompletedAt = DateTime.Now.AddHours(-3),
            Total = 4000,
            PaidAmount = 4000
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return (order.Id, client.Id, order.TicketNumber);
    }

    private string TokenFor(int orderId, int clientId) =>
        _factory.Services.GetRequiredService<TokenService>().CreateFeedbackToken(orderId, clientId);

    [Fact]
    public async Task Page_publique_puis_reponse_puis_deuxieme_reponse_409()
    {
        var (orderId, clientId, ticket) = await CompletedOrderAsync();
        string token = TokenFor(orderId, clientId);
        PressingApiClient anonymous = _factory.NewApiClient();

        FeedbackFormDto form = (await anonymous.GetFeedbackFormAsync(token))!;
        Assert.Equal(ticket, form.TicketNumber);
        Assert.Equal("Mariam", form.FirstName);
        Assert.False(form.AlreadyAnswered);

        string message = await anonymous.SubmitFeedbackAsync(token, 5, "Parfait, merci");
        Assert.Contains("Merci", message);
        Assert.True((await anonymous.GetFeedbackFormAsync(token))!.AlreadyAnswered);

        var second = await Assert.ThrowsAsync<ApiException>(() => anonymous.SubmitFeedbackAsync(token, 1, "Autre"));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-3)]
    public async Task Note_hors_de_1_a_5_refusee_en_400(int note)
    {
        var (orderId, clientId, _) = await CompletedOrderAsync();
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => _factory.NewApiClient().SubmitFeedbackAsync(TokenFor(orderId, clientId), note, null));
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("entre 1 et 5", ex.Message);
    }

    [Fact]
    public async Task Commentaire_de_plus_de_1000_caracteres_refuse()
    {
        var (orderId, clientId, _) = await CompletedOrderAsync();
        var ex = await Assert.ThrowsAsync<ApiException>(
            () => _factory.NewApiClient().SubmitFeedbackAsync(TokenFor(orderId, clientId), 3, new string('a', 1001)));
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task Jetons_invalides_ou_d_un_autre_type_sont_refuses_sur_les_routes_publiques()
    {
        var (orderId, clientId, _) = await CompletedOrderAsync();
        var tokens = _factory.Services.GetRequiredService<TokenService>();
        PressingApiClient anonymous = _factory.NewApiClient();

        foreach (string bad in new[] { tokens.CreateUnsubscribeToken(clientId), "abc", TokenFor(orderId, clientId + 9999) })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await Assert.ThrowsAsync<ApiException>(() => anonymous.GetFeedbackFormAsync(bad))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Assert.ThrowsAsync<ApiException>(() => anonymous.SubmitFeedbackAsync(bad, 4, null))).StatusCode);
        }

        // Un jeton de désabonnement ne vaut pas non plus pour la route de désabonnement s'il s'agit d'un jeton d'avis.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Assert.ThrowsAsync<ApiException>(() => anonymous.GetUnsubscribeStatusAsync(TokenFor(orderId, clientId)))).StatusCode);
    }

    [Fact]
    public async Task Le_jeton_d_avis_n_authentifie_aucune_autre_route()
    {
        var (orderId, clientId, _) = await CompletedOrderAsync();
        using HttpClient http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenFor(orderId, clientId));

        foreach (string path in new[] { "/api/auth/me", "/api/orders", "/api/dashboard", "/api/feedback", "/api/portal/orders", "/api/staff" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(path)).StatusCode);
        }
    }

    [Fact]
    public async Task Liste_des_avis_reservee_au_responsable_et_a_l_administrateur()
    {
        var (orderId, clientId, ticket) = await CompletedOrderAsync();
        await _factory.NewApiClient().SubmitFeedbackAsync(TokenFor(orderId, clientId), 4, "Bien");

        var forbidden = await Assert.ThrowsAsync<ApiException>(async () => await (await _factory.CashierAsync()).GetFeedbackAsync());
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var unauthorized = await Assert.ThrowsAsync<ApiException>(() => _factory.NewApiClient().GetFeedbackAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        foreach (PressingApiClient who in new[] { await _factory.ManagerAsync(), await _factory.AdminAsync() })
        {
            FeedbackPageDto page = (await who.GetFeedbackAsync(1, 50, 30))!;
            Assert.True(page.Count >= 1);
            Assert.NotNull(page.Average);
            Assert.Contains(page.Items, f => f.TicketNumber == ticket && f.Note == 4 && f.Comment == "Bien");
        }
    }
}
