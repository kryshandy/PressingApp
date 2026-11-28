using Pressing.Core.Dtos;

namespace Pressing.Core.Assistant;

/// <summary>Implémentation factice pour les tests : aucun LLM, aucun réseau.</summary>
public sealed class FakeIntentExtractor : IIntentExtractor, IM2IntentExtractor
{
    private readonly Queue<IntentExtraction> _results = new();

    public IntentExtraction? Default { get; set; }
    public AssistantIntentDto? DefaultDto { get; set; }
    public bool Ready { get; set; } = true;
    public bool ReadyAfterFailure { get; set; }
    public int CallCount { get; private set; }
    public string? LastText { get; private set; }
    public CatalogHints? LastCatalog { get; private set; }

    public void Enqueue(IntentExtraction extraction) => _results.Enqueue(extraction);

    public void EnqueueJson(string json, CatalogHints catalog)
    {
        IntentValidationResult validation = IntentValidator.Validate(json, catalog);
        _results.Enqueue(new IntentExtraction(json, validation.Payload, validation.Warnings));
    }

    public void EnqueueDto(AssistantIntentDto dto, string? rawJson = null) =>
        _results.Enqueue(new IntentExtraction(rawJson ?? "", AssistantDtoMapping.FromDto(dto), []));

    public Task<IntentExtraction> ExtractAsync(
        string texte,
        CatalogHints catalog,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texte);
        ArgumentNullException.ThrowIfNull(catalog);
        cancellationToken.ThrowIfCancellationRequested();

        CallCount++;
        LastText = texte;
        LastCatalog = catalog;

        if (_results.Count > 0) return Task.FromResult(_results.Dequeue());
        if (Default is not null) return Task.FromResult(Default);
        if (DefaultDto is not null)
        {
            return Task.FromResult(new IntentExtraction(
                "", DefaultDto.FromDto(), []));
        }

        throw new InvalidOperationException("Aucune intention factice configurée.");
    }

    /// <summary>Contrat M2 : renvoie directement le DTO officiel.</summary>
    public async Task<AssistantIntentDto> ExtractAsync(
        string text,
        AssistantCatalogHints hints,
        CancellationToken ct = default) =>
        (await ExtractAsync(text, new CatalogHints(hints.Categories, hints.Services), ct)).Payload.ToDto();

    public Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ReadyAfterFailure)
        {
            ReadyAfterFailure = false;
            return Task.FromResult(false);
        }
        return Task.FromResult(Ready);
    }
}
