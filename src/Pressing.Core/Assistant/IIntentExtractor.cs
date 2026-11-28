using Pressing.Core.Dtos;

namespace Pressing.Core.Assistant;

/// <summary>Extraction d'une intention structurée par le LLM local, sans aucune opération métier.</summary>
public interface IIntentExtractor
{
    Task<IntentExtraction> ExtractAsync(string texte, CatalogHints catalog, CancellationToken cancellationToken = default);
    Task<bool> IsReadyAsync(CancellationToken cancellationToken = default);

    /// <summary>Surcharge M2 : le DTO officiel est obtenu sans duplication de contrat.</summary>
    Task<AssistantIntentDto> ExtractAsync(
        string texte,
        AssistantCatalogHints hints,
        CancellationToken cancellationToken = default);
}
