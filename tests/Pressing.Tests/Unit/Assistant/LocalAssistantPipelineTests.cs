using System.Text.Json;
using Pressing.Core.Assistant;

namespace Pressing.Tests.Unit.Assistant;

public class LocalAssistantPipelineTests
{
    [Fact]
    public async Task Le_pipeline_complet_produit_un_brouillon_sans_modifier_de_donnees()
    {
        var stt = new FakeSpeechToText();
        stt.Enqueue("Dépôt pour trois chemises.");
        var llm = new FakeIntentExtractor();
        llm.Enqueue(AssistantTestData.Extraction());
        var pipeline = new LocalAssistantPipeline(stt, llm, new AssistantOptions());
        using var wav = new MemoryStream(AssistantTestData.Wav(0.5));

        PipelineResult result = await pipeline.ProcessAsync(wav, AssistantTestData.Catalog());

        Assert.Equal(PipelineStatus.Ok, result.Status);
        Assert.Equal("Dépôt pour trois chemises.", result.Transcript);
        Assert.NotNull(result.Extraction);
        Assert.Equal(AssistantIntentions.CreateOrder, result.Extraction.Payload.Intent);
        Assert.Equal(1, stt.CallCount);
        Assert.Equal(1, llm.CallCount);
    }

    [Fact]
    public async Task Une_confiance_faible_demande_une_reformulation_avec_la_transcription()
    {
        var stt = new FakeSpeechToText();
        stt.Enqueue("Je ne sais pas trop.");
        var llm = new FakeIntentExtractor();
        llm.Enqueue(AssistantTestData.Extraction(AssistantTestData.ValidJson(confidence: 0.4)));
        var pipeline = new LocalAssistantPipeline(stt, llm, new AssistantOptions { ConfidenceThreshold = 0.6 });
        using var wav = new MemoryStream(AssistantTestData.Wav(0.5));

        PipelineResult result = await pipeline.ProcessAsync(wav, AssistantTestData.Catalog());

        Assert.Equal(PipelineStatus.NotUnderstood, result.Status);
        Assert.Equal(AssistantMessages.NotUnderstood, result.Message);
        Assert.Equal("Je ne sais pas trop.", result.Transcript);
    }

    [Fact]
    public async Task Une_intention_inconnue_demande_une_reformulation()
    {
        var llm = new FakeIntentExtractor();
        llm.Enqueue(AssistantTestData.Extraction(AssistantTestData.ValidJson(
            intent: AssistantIntentions.Unknown, confidence: 0.95)));
        var pipeline = new LocalAssistantPipeline(new FakeSpeechToText(), llm, new AssistantOptions());

        PipelineResult result = await pipeline.InterpretAsync("Bla bla", AssistantTestData.Catalog());

        Assert.Equal(PipelineStatus.NotUnderstood, result.Status);
        Assert.Equal(AssistantMessages.NotUnderstood, result.Message);
    }

    [Fact]
    public async Task Une_categorie_hors_catalogue_est_signalee_sans_deviner()
    {
        var llm = new FakeIntentExtractor();
        llm.Enqueue(AssistantTestData.Extraction(
            AssistantTestData.ValidJson(category: "Dragons"),
            AssistantTestData.Catalog()));
        var pipeline = new LocalAssistantPipeline(new FakeSpeechToText(), llm, new AssistantOptions());

        PipelineResult result = await pipeline.InterpretAsync("Dépôt de dragons.", AssistantTestData.Catalog());

        Assert.Equal(PipelineStatus.NotUnderstood, result.Status);
        Assert.Contains(result.Warnings, w => w.Contains("Dragons", StringComparison.Ordinal));
        Assert.Equal("Dragons", result.Extraction!.Payload.Items[0].Category);
    }

    [Fact]
    public async Task Un_silence_est_detecte_sans_appel_au_llm()
    {
        var stt = new FakeSpeechToText();
        stt.Enqueue("", silence: true);
        var llm = new FakeIntentExtractor();
        var pipeline = new LocalAssistantPipeline(stt, llm, new AssistantOptions());
        using var wav = new MemoryStream(AssistantTestData.Wav(0.5));

        PipelineResult result = await pipeline.ProcessAsync(wav, AssistantTestData.Catalog());

        Assert.Equal(PipelineStatus.Silence, result.Status);
        Assert.Equal(AssistantMessages.Silence, result.Message);
        Assert.Equal(0, llm.CallCount);
    }

    [Fact]
    public async Task Un_moteur_arrete_renvoie_le_message_exact()
    {
        var stt = new ThrowingSpeechToText();
        var pipeline = new LocalAssistantPipeline(stt, new FakeIntentExtractor(), new AssistantOptions());
        using var wav = new MemoryStream(AssistantTestData.Wav(0.5));

        PipelineResult result = await pipeline.ProcessAsync(wav, AssistantTestData.Catalog());

        Assert.Equal(PipelineStatus.EngineUnavailable, result.Status);
        Assert.Equal(AssistantMessages.EngineNotStarted, result.Message);
    }

    [Fact]
    public async Task Le_pipeline_reel_corrige_une_categorie_hallucinee_sans_inventer()
    {
        string texte = "Dépôt de trois rideaux à repasser.";
        var catalog = new CatalogHints(
            new[] { new CatalogTerm("Rideau", []) },
            new[] { new CatalogTerm("Lavage", []), new CatalogTerm("Repassage", []) },
            new DateOnly(2026, 11, 27));
        string json = AssistantTestData.ValidJson(
            category: "Drapeau 2 places", service: "Repassage", transcript: texte);
        string chat = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = json } } }
        });
        var handler = new StubHandler().EnqueueJson(chat);
        using var http = new HttpClient(handler);
        var pipeline = new LocalAssistantPipeline(
            new FakeSpeechToText(),
            new LlmIntentExtractor(http, new AssistantOptions()),
            new AssistantOptions());

        PipelineResult result = await pipeline.InterpretAsync(texte, catalog);

        Assert.Equal(PipelineStatus.Ok, result.Status);
        Assert.Equal("Rideau", result.Extraction!.Payload.Items[0].Category);
        Assert.Equal(3, result.Extraction.Payload.Items[0].Quantity);
        Assert.NotEqual("Drapeau 2 places", result.Extraction!.Payload.Items[0].Category);
        Assert.Contains(result.Warnings, w => w.Contains("Valeur corrigée depuis la transcription", StringComparison.Ordinal));
    }
}

internal sealed class ThrowingSpeechToText : ISpeechToText
{
    public Task<SpeechTranscription> TranscribeAsync(Stream wav, CancellationToken cancellationToken = default) =>
        Task.FromException<SpeechTranscription>(
            new AssistantEngineUnavailableException(AssistantMessages.EngineNotStarted));

    public Task<bool> IsReadyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
}
