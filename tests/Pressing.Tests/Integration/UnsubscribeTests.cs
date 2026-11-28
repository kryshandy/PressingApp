using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Pressing.Client;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Core.Security;
using Pressing.Tests.Support;

namespace Pressing.Tests.Integration;

/// <summary>Désinscription des rappels e-mail par le lien des e-mails, et sécurité du jeton de désinscription.</summary>
[Collection("Api")]
public class UnsubscribeTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public UnsubscribeTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Lien_valide_desactive_les_rappels_e_mail()
    {
        PressingApiClient staff = await _factory.CashierAsync();
        ClientDto client = (await staff.CreateClientAsync(new CreateClientRequest(
            "Awa", "Desabonne", "0701020304", "0701020304", "awa.desabonne@example.com", null, null, null, false,
            ClientNotificationPreference.Email)))!;
        string token = _factory.Services.GetRequiredService<TokenService>().CreateUnsubscribeToken(client.Id);
        PressingApiClient anonymous = _factory.NewApiClient();

        Assert.True((await anonymous.GetUnsubscribeStatusAsync(token))!.Active);
        Assert.Contains("ne recevrez plus", await anonymous.UnsubscribeAsync(token));
        Assert.False((await anonymous.GetUnsubscribeStatusAsync(token))!.Active);
    }

    [Fact]
    public async Task Lien_invalide_renvoie_400()
    {
        PressingApiClient anonymous = _factory.NewApiClient();
        var ex = await Assert.ThrowsAsync<ApiException>(() => anonymous.GetUnsubscribeStatusAsync("pas-un-jeton"));
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public async Task Jeton_de_desabonnement_n_ouvre_pas_l_espace_client()
    {
        string token = _factory.Services.GetRequiredService<TokenService>().CreateUnsubscribeToken(1);
        HttpClient http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response = await http.GetAsync("/api/portal/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Jeton_de_session_du_personnel_ne_sert_pas_de_lien_de_desabonnement()
    {
        PressingApiClient staff = await _factory.CashierAsync();
        HttpClient http = _factory.CreateClient();
        HttpResponseMessage response = await http.PostAsJsonAsync("/api/public/unsubscribe",
            new UnsubscribeRequest(staff.Token!));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
