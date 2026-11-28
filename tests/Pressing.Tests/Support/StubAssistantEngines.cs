using Pressing.Core.Assistant;
using Pressing.Core.Dtos;

namespace Pressing.Tests.Support;

/// <summary>Reconnaissance vocale factice pour les tests d'intégration : aucun moteur réel n'est lancé.</summary>
public sealed class StubSpeechToText : ISpeechToText
{
    public string Text { get; set; } = "dépôt pour Mme Ngo, deux chemises lavage repassage";
    public bool Ready { get; set; } = true;

    /// <summary>Simule un moteur arrêté : lève <see cref="AssistantUnavailableException"/>.</summary>
    public bool Stopped { get; set; }

    public int Calls { get; private set; }

    public Task<string> TranscribeAsync(Stream wav, CancellationToken ct = default)
    {
        if (Stopped) throw new AssistantUnavailableException();
        Calls++;
        return Task.FromResult(Text);
    }

    public Task<bool> IsReadyAsync(CancellationToken ct = default) => Task.FromResult(Ready && !Stopped);
}

/// <summary>Extraction d'intention factice : renvoie l'intention configurée et mémorise ce qu'elle a reçu.</summary>
public sealed class StubIntentExtractor : IIntentExtractor
{
    public AssistantIntentDto Intent { get; set; } = new(
        Intent: AssistantIntents.CreateOrder,
        Client: new AssistantClientDto("Zorro", null),
        Items: [new AssistantItemDto("Chemise", "lavage repassage", 2, false)],
        Confidence: 0.92);

    public bool Ready { get; set; } = true;
    public bool Stopped { get; set; }
    public string? LastText { get; private set; }
    public CatalogHints? LastHints { get; private set; }

    public Task<AssistantIntentDto> ExtractAsync(string text, CatalogHints hints, CancellationToken ct = default)
    {
        if (Stopped) throw new AssistantUnavailableException();
        LastText = text;
        LastHints = hints;
        return Task.FromResult(Intent);
    }

    public Task<bool> IsReadyAsync(CancellationToken ct = default) => Task.FromResult(Ready && !Stopped);
}
