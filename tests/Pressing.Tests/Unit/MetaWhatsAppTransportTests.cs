using System.Text.Json;
using Pressing.Core.Messaging;

namespace Pressing.Tests.Unit;

/// <summary>
/// Transport Meta : hors fenêtre de 24 h, seuls les messages « template » sont acceptés. Quand un modèle est configuré
/// pour le type de rappel, le JSON envoyé est un modèle avec ses variables ; sinon le texte libre est conservé.
/// </summary>
public class MetaWhatsAppTransportTests
{
    private static WhatsAppOptions Options(Action<WhatsAppOptions>? configure = null)
    {
        var options = new WhatsAppOptions
        {
            Provider = "meta", AccessToken = "token-test", PhoneNumberId = "123456", ApiVersion = "v23.0"
        };
        configure?.Invoke(options);
        return options;
    }

    private static async Task<(WhatsAppSendResult Result, CapturingHandler Handler)> SendAsync(
        WhatsAppOptions options, WhatsAppMessage message)
    {
        var handler = new CapturingHandler();
        var transport = new MetaWhatsAppTransport(new HttpClient(handler), options);
        return (await transport.SendAsync(message), handler);
    }

    [Fact]
    public async Task Modele_configure_envoie_un_message_template_avec_les_variables()
    {
        var (result, handler) = await SendAsync(
            Options(o => o.TemplateBalance = "rappel_solde"),
            new WhatsAppMessage("+225 07 11 22 33 44", "texte libre", "unpaid-balance",
                ["Awa", "PRESS-2026-00042", "3 500 FCFA"]));

        Assert.True(result.Success);
        Assert.Equal("wamid.test", result.ProviderMessageId);
        Assert.Equal("https://graph.facebook.com/v23.0/123456/messages", handler.Url);
        Assert.Equal("Bearer token-test", handler.Authorization);

        JsonElement root = handler.Json;
        Assert.Equal("whatsapp", root.GetProperty("messaging_product").GetString());
        Assert.Equal("2250711223344", root.GetProperty("to").GetString());
        Assert.Equal("template", root.GetProperty("type").GetString());
        Assert.False(root.TryGetProperty("text", out _));

        JsonElement template = root.GetProperty("template");
        Assert.Equal("rappel_solde", template.GetProperty("name").GetString());
        Assert.Equal("fr", template.GetProperty("language").GetProperty("code").GetString());

        JsonElement body = Assert.Single(template.GetProperty("components").EnumerateArray());
        Assert.Equal("body", body.GetProperty("type").GetString());
        string[] texts = body.GetProperty("parameters").EnumerateArray()
            .Select(p => { Assert.Equal("text", p.GetProperty("type").GetString()); return p.GetProperty("text").GetString()!; })
            .ToArray();
        Assert.Equal(["Awa", "PRESS-2026-00042", "3 500 FCFA"], texts);
    }

    [Theory]
    [InlineData("ready", "modele_pret")]
    [InlineData("uncollected-j3", "modele_oublie")]
    [InlineData("uncollected-j7", "modele_oublie")]
    [InlineData("uncollected-j14", "modele_oublie")]
    [InlineData("unpaid-balance", "modele_solde")]
    [InlineData("due-tomorrow", "modele_echeance")]
    public async Task Chaque_type_de_rappel_utilise_son_modele(string type, string expected)
    {
        var options = Options(o =>
        {
            o.TemplateReady = "modele_pret";
            o.TemplateUncollected = "modele_oublie";
            o.TemplateBalance = "modele_solde";
            o.TemplateDue = "modele_echeance";
            o.TemplateLanguage = "fr_FR";
        });

        var (_, handler) = await SendAsync(options, new WhatsAppMessage("0711223344", "x", type, ["Awa", "T-1"]));

        JsonElement template = handler.Json.GetProperty("template");
        Assert.Equal(expected, template.GetProperty("name").GetString());
        Assert.Equal("fr_FR", template.GetProperty("language").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Sans_modele_configure_le_texte_libre_est_conserve()
    {
        var (result, handler) = await SendAsync(
            Options(o => o.TemplateReady = "modele_pret"),
            new WhatsAppMessage("0711223344", "Bonjour Awa", "unpaid-balance", ["Awa", "T-1", "500 FCFA"]));

        Assert.True(result.Success);
        JsonElement root = handler.Json;
        Assert.Equal("text", root.GetProperty("type").GetString());
        Assert.Equal("Bonjour Awa", root.GetProperty("text").GetProperty("body").GetString());
        Assert.False(root.TryGetProperty("template", out _));
    }

    [Fact]
    public async Task Message_sans_type_de_rappel_reste_en_texte_libre_meme_avec_des_modeles()
    {
        var (_, handler) = await SendAsync(
            Options(o => { o.TemplateReady = "a"; o.TemplateUncollected = "b"; o.TemplateBalance = "c"; o.TemplateDue = "d"; }),
            new WhatsAppMessage("0711223344", "Relance libre"));

        Assert.Equal("text", handler.Json.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Modele_sans_variable_n_envoie_pas_de_composant_et_nettoie_les_sauts_de_ligne()
    {
        var (_, none) = await SendAsync(
            Options(o => o.TemplateDue = "echeance"),
            new WhatsAppMessage("0711223344", "x", "due-tomorrow"));
        Assert.Equal(0, none.Json.GetProperty("template").GetProperty("components").GetArrayLength());

        var (_, cleaned) = await SendAsync(
            Options(o => o.TemplateReady = "pret"),
            new WhatsAppMessage("0711223344", "x", "ready", ["Awa\nKoné", " "]));
        var parameters = cleaned.Json.GetProperty("template").GetProperty("components")[0].GetProperty("parameters");
        Assert.Equal("Awa Koné", parameters[0].GetProperty("text").GetString());
        Assert.Equal("-", parameters[1].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Erreur_de_Meta_est_rapportee_sans_exception()
    {
        var handler = new CapturingHandler { Status = System.Net.HttpStatusCode.BadRequest, Response = """{"error":{"message":"template not found"}}""" };
        var transport = new MetaWhatsAppTransport(new HttpClient(handler), Options(o => o.TemplateReady = "inconnu"));

        WhatsAppSendResult result = await transport.SendAsync(new WhatsAppMessage("0711223344", "x", "ready", ["Awa"]));

        Assert.False(result.Success);
        Assert.Contains("400", result.Error);
        Assert.Contains("template not found", result.Error);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public System.Net.HttpStatusCode Status { get; init; } = System.Net.HttpStatusCode.OK;
        public string Response { get; init; } = """{"messages":[{"id":"wamid.test"}]}""";
        public string? Url { get; private set; }
        public string? Authorization { get; private set; }
        public JsonElement Json { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri?.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Json = JsonDocument.Parse(body).RootElement.Clone();
            return new HttpResponseMessage(Status) { Content = new StringContent(Response) };
        }
    }
}
