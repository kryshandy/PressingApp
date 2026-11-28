using Pressing.Core.Dtos;

namespace Pressing.Core.Assistant;

/// <summary>
/// Façade M2 demandée par Maël. Les implémentations M1 conservent leurs contrats
/// enrichis (<see cref="SpeechTranscription"/>, <see cref="IntentExtraction"/>) ;
/// ces interfaces exposent exactement les signatures attendues par le Membre 2.
/// </summary>
public interface IM2SpeechToText
{
    Task<string> TranscribeAsync(Stream wav, CancellationToken ct = default);
    Task<bool> IsReadyAsync(CancellationToken ct = default);
}

/// <summary>Contrat M2 d'extraction : DTO officiel et catalogue simple.</summary>
public interface IM2IntentExtractor
{
    Task<AssistantIntentDto> ExtractAsync(
        string text,
        AssistantCatalogHints hints,
        CancellationToken ct = default);
    Task<bool> IsReadyAsync(CancellationToken ct = default);
}

/// <summary>
/// Adaptateur pour une implémentation M2 qui expose uniquement les signatures annoncées
/// par Maël. Les garde-fous M1 doivent être appliqués par le producteur du DTO.
/// </summary>
public sealed class M2SpeechToTextAdapter(string text, bool ready = true) : ISpeechToText, IM2SpeechToText
{
    public Task<string> TranscribeAsync(Stream wav, CancellationToken ct = default) => Task.FromResult(text);

    public Task<bool> IsReadyAsync(CancellationToken ct = default) => Task.FromResult(ready);

    Task<SpeechTranscription> ISpeechToText.TranscribeAsync(Stream wav, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new SpeechTranscription(
            text, 0, 0, string.IsNullOrWhiteSpace(text)));
    }
}

/// <summary>
/// Adaptateur inverse : une implémentation M1 riche peut être consommée avec le contrat M2.
/// </summary>
public sealed class M2IntentExtractorAdapter(IIntentExtractor inner) : IM2IntentExtractor
{
    public Task<AssistantIntentDto> ExtractAsync(
        string text,
        AssistantCatalogHints hints,
        CancellationToken ct = default) =>
        inner.ExtractAsync(text, hints, ct);

    public Task<bool> IsReadyAsync(CancellationToken ct = default) => inner.IsReadyAsync(ct);
}

/// <summary>
/// Erreur M2 d'indisponibilité. Elle complète les exceptions M1 sans retirer
/// <see cref="AssistantEngineUnavailableException"/> utilisée par le pipeline local.
/// </summary>
public sealed class AssistantUnavailableException(string message, Exception? innerException = null)
    : AssistantException(message, innerException);
