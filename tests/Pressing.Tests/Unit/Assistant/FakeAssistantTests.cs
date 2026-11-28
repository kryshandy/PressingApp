using Pressing.Core.Assistant;

namespace Pressing.Tests.Unit.Assistant;

public class FakeAssistantTests
{
    [Fact]
    public async Task FakeSpeechToText_renvoie_les_transcriptions_filees()
    {
        var fake = new FakeSpeechToText();
        fake.Enqueue("première phrase");
        fake.Enqueue("deuxième phrase");
        using var wav = new MemoryStream(AssistantTestData.Wav(0.2));

        Assert.Equal("première phrase", (await fake.TranscribeAsync(wav)).Text);
        Assert.Equal("deuxième phrase", (await fake.TranscribeAsync(wav)).Text);
        Assert.Equal(2, fake.CallCount);
    }

    [Fact]
    public async Task FakeIntentExtractor_valide_le_json_et_conserve_le_catalogue()
    {
        var fake = new FakeIntentExtractor();
        CatalogHints catalog = AssistantTestData.Catalog();
        fake.EnqueueJson(AssistantTestData.ValidJson(), catalog);

        IntentExtraction result = await fake.ExtractAsync("Dépôt pour trois chemises.", catalog);

        Assert.Equal(AssistantIntentions.CreateOrder, result.Payload.Intent);
        Assert.Equal("Dépôt pour trois chemises.", fake.LastText);
        Assert.Same(catalog, fake.LastCatalog);
        Assert.Empty(result.Warnings);
    }
}
