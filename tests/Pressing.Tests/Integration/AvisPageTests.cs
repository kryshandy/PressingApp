extern alias WebApp;

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pressing.Client;

namespace Pressing.Tests.Integration;

/// <summary>Page publique /avis du site : formulaire de note, lien invalide, avis déjà reçu (API simulée).</summary>
public sealed class AvisPageTests
{
    private sealed class Factory(Func<HttpRequestMessage, HttpResponseMessage> api) : WebApplicationFactory<WebApp::Pressing.Web.WebEntryPoint>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<PressingApiClient>();
                services.AddScoped(_ => new PressingApiClient(
                    new HttpClient(new Handler(api)) { BaseAddress = new Uri("http://fake-api/") }));
            });
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> api) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri!.AbsolutePath.StartsWith("/api/public/feedback", StringComparison.Ordinal)
                ? api(request)
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Lien_valide_affiche_les_cinq_notes_et_le_champ_commentaire()
    {
        using var factory = new Factory(_ => Json(HttpStatusCode.OK,
            """{"ticketNumber":"PRESS-2026-00042","firstName":"Awa","shopName":"Pressing Éclat","alreadyAnswered":false,"note":null}"""));
        using HttpClient http = factory.CreateClient();

        string html = await (await http.GetAsync("/avis?token=abc")).Content.ReadAsStringAsync();

        Assert.Contains("PRESS-2026-00042", html);
        Assert.Contains("Bonjour Awa", html);
        for (int i = 1; i <= 5; i++) Assert.Contains($"id=\"note-{i}\"", html);
        Assert.Contains("maxlength=\"1000\"", html);
        Assert.Contains("__RequestVerificationToken", html);
    }

    [Fact]
    public async Task Lien_invalide_ou_absent_affiche_un_message_clair()
    {
        using var factory = new Factory(_ => Json(HttpStatusCode.BadRequest, """{"message":"Ce lien d'avis est invalide ou a expiré."}"""));
        using HttpClient http = factory.CreateClient();

        string invalid = await (await http.GetAsync("/avis?token=faux")).Content.ReadAsStringAsync();
        Assert.Contains("Lien invalide", invalid);
        Assert.DoesNotContain("note-1", invalid);

        string missing = await (await http.GetAsync("/avis")).Content.ReadAsStringAsync();
        Assert.Contains("Lien invalide", missing);
    }

    [Fact]
    public async Task Avis_deja_donne_n_affiche_plus_le_formulaire()
    {
        using var factory = new Factory(_ => Json(HttpStatusCode.OK,
            """{"ticketNumber":"PRESS-2026-00042","firstName":"Awa","shopName":"Pressing Éclat","alreadyAnswered":true,"note":5}"""));
        using HttpClient http = factory.CreateClient();

        string html = await (await http.GetAsync("/avis?token=abc")).Content.ReadAsStringAsync();
        Assert.Contains("Avis déjà reçu", html);
        Assert.DoesNotContain("note-1", html);
    }
}
