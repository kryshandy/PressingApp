using System.Net;
using System.Text.Json;
using Pressing.Core.Assistant;

namespace Pressing.Tests.Unit.Assistant;

public class LlmIntentExtractorTests
{
    [Fact]
    public async Task Envoie_le_schema_et_le_catalogue_puis_valide_la_sortie()
    {
        string json = AssistantTestData.ValidJson();
        var handler = new StubHandler().EnqueueJson(Chat(json));
        using var http = new HttpClient(handler);
        var client = new LlmIntentExtractor(http, new AssistantOptions());

        IntentExtraction result = await client.ExtractAsync("Dépôt pour trois chemises.", AssistantTestData.Catalog());

        Assert.Equal(AssistantIntentions.CreateOrder, result.Payload.Intent);
        Assert.Equal("http://127.0.0.1:8081/v1/chat/completions", handler.Uris[0].ToString());
        Assert.Contains("\"response_format\"", handler.Bodies[0]);
        Assert.Contains("json_schema", handler.Bodies[0]);
        Assert.Contains("2026-11-27", handler.Bodies[0]);
        // System.Text.Json échappe '+' en \u002B : on vérifie les deux mots séparément.
        Assert.Contains("Lavage", handler.Bodies[0]);
        Assert.Contains("repassage", handler.Bodies[0]);
    }

    [Fact]
    public async Task La_transcription_originale_est_imposee_meme_si_le_modele_la_corrige()
    {
        string original = "Je voudrais faire laver une chemise en express, s il vous plait.";
        string corrige = "Je voudrais faire laver une chemise en express, s'il vous plait.";
        string json = AssistantTestData.ValidJson(transcript: corrige)
            .Replace("\"express\": false", "\"express\": true", StringComparison.Ordinal);
        var handler = new StubHandler().EnqueueJson(Chat(json));
        using var http = new HttpClient(handler);
        var client = new LlmIntentExtractor(http, new AssistantOptions());

        IntentExtraction result = await client.ExtractAsync(original, AssistantTestData.Catalog());

        Assert.Equal(original, result.Payload.Transcript);
        Assert.Contains("s'il vous plait", result.RawJson, StringComparison.Ordinal);
        Assert.Equal(AssistantIntentions.CreateOrder, result.Payload.Intent);
        Assert.True(result.Payload.Items[0].Express);
    }

    [Fact]
    public async Task Un_express_false_invente_par_le_modele_devient_null()
    {
        string texte = "Je depose une chemise a laver, s il vous plait.";
        var handler = new StubHandler().EnqueueJson(Chat(AssistantTestData.ValidJson(transcript: texte)));
        using var http = new HttpClient(handler);
        var client = new LlmIntentExtractor(http, new AssistantOptions());

        IntentExtraction result = await client.ExtractAsync(texte, AssistantTestData.Catalog());

        Assert.Null(result.Payload.Items[0].Express);
        Assert.Contains("\"express\": false", result.RawJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_refus_explicite_de_express_reste_false()
    {
        string texte = "Je depose une chemise a laver, pas express.";
        var handler = new StubHandler().EnqueueJson(Chat(AssistantTestData.ValidJson(transcript: texte)));
        using var http = new HttpClient(handler);
        var client = new LlmIntentExtractor(http, new AssistantOptions());

        IntentExtraction result = await client.ExtractAsync(texte, AssistantTestData.Catalog());

        Assert.False(result.Payload.Items[0].Express);
    }

    [Fact]
    public async Task Un_express_omis_par_le_modele_est_restaure_si_la_phrase_le_dit()
    {
        string texte = "Je voudrais faire laver une chemise en express.";
        string json = AssistantTestData.ValidJson(transcript: texte)
            .Replace("\"express\": false", "\"express\": null", StringComparison.Ordinal);
        var handler = new StubHandler().EnqueueJson(Chat(json));
        using var http = new HttpClient(handler);
        var client = new LlmIntentExtractor(http, new AssistantOptions());

        IntentExtraction result = await client.ExtractAsync(texte, AssistantTestData.Catalog());

        Assert.True(result.Payload.Items[0].Express);
    }

    [Fact]
    public async Task Un_echec_500_est_rejoue_une_seule_fois()
    {
        string json = AssistantTestData.ValidJson();
        var handler = new StubHandler()
            .Enqueue(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("oops") })
            .EnqueueJson(Chat(json));
        using var http = new HttpClient(handler);
        var client = new LlmIntentExtractor(http, new AssistantOptions());

        IntentExtraction result = await client.ExtractAsync("Dépôt.", AssistantTestData.Catalog());

        Assert.Equal(AssistantIntentions.CreateOrder, result.Payload.Intent);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Un_moteur_arrete_renvoie_le_message_exact()
    {
        var handler = new StubHandler()
            .Throw(new HttpRequestException("refused"))
            .Throw(new HttpRequestException("refused"));
        using var http = new HttpClient(handler);
        var client = new LlmIntentExtractor(http, new AssistantOptions());

        AssistantEngineUnavailableException error = await Assert.ThrowsAsync<AssistantEngineUnavailableException>(
            () => client.ExtractAsync("Dépôt.", AssistantTestData.Catalog()));

        Assert.Equal(AssistantMessages.EngineNotStarted, error.Message);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Une_intention_inconnue_efface_les_informations_inventees()
    {
        string texte = "Euh, je ne sais pas trop, peut-être quelque chose avec ce pantalon.";
        string json = AssistantTestData.ValidJson(
                intent: AssistantIntentions.Unknown,
                confidence: 0.4,
                transcript: texte)
            .Replace("\"name\": null", "\"name\": \"Astride\"", StringComparison.Ordinal);
        var handler = new StubHandler().EnqueueJson(Chat(json));
        using var http = new HttpClient(handler);
        var client = new LlmIntentExtractor(http, new AssistantOptions());

        IntentExtraction result = await client.ExtractAsync(texte, AssistantTestData.Catalog());

        Assert.Equal(AssistantIntentions.Unknown, result.Payload.Intent);
        Assert.Empty(result.Payload.Items);
        Assert.Null(result.Payload.Client.Name);
        Assert.Null(result.Payload.Amount);
        Assert.Null(result.Payload.Ticket);
        Assert.Equal(texte, result.Payload.Transcript);
        Assert.Contains("Astride", result.RawJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Une_sortie_hors_contrat_est_refusee()
    {
        var handler = new StubHandler().EnqueueJson(Chat("{\"intent\":\"supprimer\"}"));
        using var http = new HttpClient(handler);
        var client = new LlmIntentExtractor(http, new AssistantOptions());

        await Assert.ThrowsAsync<AssistantContractException>(
            () => client.ExtractAsync("Dépôt.", AssistantTestData.Catalog()));
    }

    [Fact]
    public async Task Une_date_de_retrait_invalide_est_mise_a_null_sans_refuser_la_reponse()
    {
        string texte = "Retrait pour Madame Ngo.";
        string json = AssistantTestData.ValidJson(
                intent: AssistantIntentions.RegisterPickup,
                transcript: texte)
            .Replace("\"pickup_date\": null", "\"pickup_date\": \"demain\"", StringComparison.Ordinal);
        var handler = new StubHandler().EnqueueJson(Chat(json));
        using var http = new HttpClient(handler);
        var client = new LlmIntentExtractor(http, new AssistantOptions());

        IntentExtraction result = await client.ExtractAsync(texte, AssistantTestData.Catalog());

        Assert.Equal(AssistantIntentions.RegisterPickup, result.Payload.Intent);
        Assert.Null(result.Payload.PickupDate);
        Assert.Contains("pickup_date invalide", string.Join(" ", result.Warnings), StringComparison.Ordinal);
        Assert.Contains("demain", result.RawJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Une_reponse_sans_choices_est_refusee()
    {
        var handler = new StubHandler().EnqueueJson("{\"choices\":[]}");
        using var http = new HttpClient(handler);
        var client = new LlmIntentExtractor(http, new AssistantOptions());

        await Assert.ThrowsAsync<AssistantContractException>(
            () => client.ExtractAsync("Dépôt.", AssistantTestData.Catalog()));
    }

    [Fact]
    public async Task Le_garde_fou_recalle_intention_et_article_sans_appel_supplementaire()
    {
        string texte = "La chemise est prête.";
        string json = AssistantTestData.ValidJson(
            intent: AssistantIntentions.RegisterPickup,
            transcript: texte);
        var handler = new StubHandler().EnqueueJson(Chat(json));
        using var http = new HttpClient(handler);
        var client = new LlmIntentExtractor(http, new AssistantOptions());

        IntentExtraction result = await client.ExtractAsync(texte, AssistantTestData.Catalog());

        Assert.Equal(AssistantIntentions.UpdateItemStatus, result.Payload.Intent);
        Assert.Single(result.Payload.Items);
        Assert.Equal("Chemise", result.Payload.Items[0].Category);
        Assert.Equal(1, result.Payload.Items[0].Quantity);
        Assert.Equal("ready", result.Payload.ItemStatus);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Un_paiement_sans_montant_du_modele_est_reconstruit_depuis_la_transcription()
    {
        string texte = "Acompte de cinq mille en espèces.";
        string json = AssistantTestData.ValidJson(intent: AssistantIntentions.AddPayment, transcript: texte)
            .Replace("\"amount\": 5000", "\"amount\": null", StringComparison.Ordinal)
            .Replace("\"deposit\": 5000", "\"deposit\": null", StringComparison.Ordinal);
        var handler = new StubHandler().EnqueueJson(Chat(json));
        using var http = new HttpClient(handler);
        var client = new LlmIntentExtractor(http, new AssistantOptions());

        IntentExtraction result = await client.ExtractAsync(texte, AssistantTestData.Catalog());

        Assert.Equal(AssistantIntentions.AddPayment, result.Payload.Intent);
        Assert.Equal(5000m, result.Payload.Amount);
        Assert.Equal(5000m, result.Payload.Deposit);
        Assert.Equal("cash", result.Payload.PaymentMethod);
        Assert.Contains(result.Warnings, w => w.Contains("Montant retrouvé", StringComparison.Ordinal));
    }

    private static string Chat(string content) => JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content } } }
    });
}
