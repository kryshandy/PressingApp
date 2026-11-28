using System.Net;
using System.Reflection;
using System.Text.Json;
using Pressing.Core.Assistant;
using Pressing.Core.Dtos;

namespace Pressing.Tests.Unit.Assistant;

public class M2CompatibilityTests
{
    [Fact]
    public void Les_facades_M2_exposent_les_signatures_attendues()
    {
        Assert.Equal(typeof(Task<string>), typeof(IM2SpeechToText).GetMethod(
            "TranscribeAsync", [typeof(Stream), typeof(CancellationToken)])!.ReturnType);
        Assert.Equal(typeof(Task<bool>), typeof(IM2SpeechToText).GetMethod(
            "IsReadyAsync", [typeof(CancellationToken)])!.ReturnType);

        MethodInfo extract = typeof(IM2IntentExtractor).GetMethod(
            "ExtractAsync", [typeof(string), typeof(AssistantCatalogHints), typeof(CancellationToken)])!;
        Assert.Equal(typeof(Task<AssistantIntentDto>), extract.ReturnType);
        Assert.Equal(typeof(Task<bool>), typeof(IM2IntentExtractor).GetMethod(
            "IsReadyAsync", [typeof(CancellationToken)])!.ReturnType);

        Assert.True(typeof(AssistantUnavailableException).IsSubclassOf(typeof(AssistantException)));
        Assert.Equal("Pressing.Core.Dtos", typeof(AssistantIntentDto).Namespace);
    }

    [Fact]
    public async Task Fakes_configurables_repondent_aux_deux_contrats()
    {
        var speechFake = new FakeSpeechToText();
        ISpeechToText stt = speechFake;
        speechFake.Enqueue("trois chemises", silence: false);
        speechFake.Enqueue("", silence: true);
        using var wav = new MemoryStream(AssistantTestData.Wav(0.2));

        SpeechTranscription riche = await ((FakeSpeechToText)stt).TranscribeAsync(wav);
        string simple = await ((IM2SpeechToText)stt).TranscribeAsync(wav);
        Assert.Equal("trois chemises", riche.Text);
        Assert.False(riche.SilenceDetected);
        Assert.Equal("", simple);
        Assert.True(await ((IM2SpeechToText)stt).IsReadyAsync());

        var fakeReady = new FakeSpeechToText { ReadyAfterFailure = true };
        Assert.False(await ((IM2SpeechToText)fakeReady).IsReadyAsync());
        Assert.True(await ((IM2SpeechToText)fakeReady).IsReadyAsync());

        var llm = new FakeIntentExtractor { DefaultDto = Dto() };
        AssistantIntentDto dtoM2 = await ((IM2IntentExtractor)llm).ExtractAsync(
            "Dépôt de trois chemises.",
            new AssistantCatalogHints(["Chemise"], ["Lavage + repassage"]));
        Assert.Equal(AssistantIntentions.CreateOrder, dtoM2.Intent);
        Assert.Single(dtoM2.Items);
        Assert.Equal("Chemise", dtoM2.Items[0].Category);

        var llmReady = new FakeIntentExtractor { ReadyAfterFailure = true };
        Assert.False(await ((IM2IntentExtractor)llmReady).IsReadyAsync());
        Assert.True(await ((IM2IntentExtractor)llmReady).IsReadyAsync());
    }

    [Fact]
    public void Le_mapping_M1_M2_preserve_tous_les_champs_du_contrat_json()
    {
        IntentPayload payload = AssistantTestData.Extraction().Payload;
        AssistantIntentDto dto = payload.ToDto();
        IntentPayload retour = dto.FromDto();

        Assert.Equal(payload.Intent, retour.Intent);
        Assert.Equal(payload.Client, retour.Client);
        Assert.Equal(payload.Items, retour.Items.ToArray());
        Assert.Equal(payload.Ticket, retour.Ticket);
        Assert.Equal(payload.Amount, retour.Amount);
        Assert.Equal(payload.PaymentMethod, retour.PaymentMethod);
        Assert.Equal(payload.Deposit, retour.Deposit);
        Assert.Equal(payload.PickupDate, retour.PickupDate);
        Assert.Equal(payload.ItemStatus, retour.ItemStatus);
        Assert.Equal(payload.Confidence, retour.Confidence);
        Assert.Equal(payload.Transcript, retour.Transcript);
        Assert.Equal("intent", typeof(AssistantIntentDto)
            .GetProperty(nameof(AssistantIntentDto.Intent))!
            .GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonPropertyNameAttribute), false)
            .Cast<System.Text.Json.Serialization.JsonPropertyNameAttribute>()
            .Single().Name);
    }

    [Fact]
    public async Task LlmIntentExtractor_M2_applique_les_garde_fous_et_renvoie_le_dto()
    {
        string texte = "Je voudrais faire laver trois chemises.";
        string json = AssistantTestData.ValidJson(
                intent: AssistantIntentions.RegisterPickup,
                transcript: texte)
            .Replace("\"category\": \"Chemise\"", "\"category\": \"Dragons\"", StringComparison.Ordinal);
        var handler = new StubHandler().EnqueueJson(Chat(json));
        using var http = new HttpClient(handler);
        IIntentExtractor extractor = new LlmIntentExtractor(http, new AssistantOptions());

        AssistantIntentDto dto = await extractor.ExtractAsync(
            texte,
            new AssistantCatalogHints(["Chemise"], ["Lavage + repassage"]));

        Assert.Equal(AssistantIntentions.CreateOrder, dto.Intent);
        Assert.Equal("Chemise", dto.Items[0].Category);
        Assert.Equal(3, dto.Items[0].Quantity);
        Assert.Equal("Lavage", dto.Items[0].Service);
        Assert.Equal(texte, dto.Transcript);
    }

    [Fact]
    public async Task Les_adaptateurs_M2_sont_consomables_par_le_pipeline_M1()
    {
        var stt = new M2SpeechToTextAdapter("Dépôt pour trois chemises.");
        var llm = new FakeIntentExtractor();
        llm.Enqueue(AssistantTestData.Extraction());
        var pipeline = new LocalAssistantPipeline(stt, llm, new AssistantOptions());
        using var wav = new MemoryStream(AssistantTestData.Wav(0.2));

        PipelineResult result = await pipeline.ProcessAsync(wav, AssistantTestData.Catalog());

        Assert.Equal(PipelineStatus.Ok, result.Status);
        Assert.Equal("Dépôt pour trois chemises.", result.Transcript);
        Assert.True(await stt.IsReadyAsync());

        IM2IntentExtractor facade = new M2IntentExtractorAdapter(new FakeIntentExtractor
        {
            DefaultDto = Dto()
        });
        AssistantIntentDto dto = await facade.ExtractAsync(
            "Dépôt de trois chemises.",
            new AssistantCatalogHints(["Chemise"], ["Lavage + repassage"]));
        Assert.Equal(AssistantIntentions.CreateOrder, dto.Intent);
    }

    [Fact]
    public async Task Un_adaptateur_M2_qui_leve_AssistantUnavailableException_est_traite()
    {
        var stt = new UnavailableSpeechToText();
        var pipeline = new LocalAssistantPipeline(stt, new FakeIntentExtractor(), new AssistantOptions());
        using var wav = new MemoryStream(AssistantTestData.Wav(0.2));

        PipelineResult result = await pipeline.ProcessAsync(
            wav, AssistantTestData.Catalog());

        Assert.Equal(PipelineStatus.EngineUnavailable, result.Status);
        Assert.Equal(AssistantMessages.EngineNotStarted, result.Message);
    }

    private static AssistantIntentDto Dto() => new()
    {
        Intent = AssistantIntentions.CreateOrder,
        Client = new AssistantClientDto(null, null),
        Items = [new AssistantItemDto("Chemise", "Lavage + repassage", 3, null)],
        Ticket = null,
        Amount = null,
        PaymentMethod = null,
        Deposit = null,
        PickupDate = null,
        ItemStatus = null,
        Confidence = 0.92,
        Transcript = "Dépôt de trois chemises."
    };

    private static string Chat(string content) => JsonSerializer.Serialize(new
    {
        choices = new[] { new { message = new { content } } }
    });

    private sealed class UnavailableSpeechToText : ISpeechToText
    {
        public Task<SpeechTranscription> TranscribeAsync(Stream wav, CancellationToken cancellationToken = default) =>
            Task.FromException<SpeechTranscription>(
                new AssistantUnavailableException(AssistantMessages.EngineNotStarted));

        public Task<bool> IsReadyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
