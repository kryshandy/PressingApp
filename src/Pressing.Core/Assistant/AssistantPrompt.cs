namespace Pressing.Core.Assistant;

public static class AssistantPrompt
{
    public static string BuildSystemPrompt(CatalogHints catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.ReferenceDate == default)
        {
            throw new ArgumentException("Une date de référence est requise pour les dates relatives.", nameof(catalog));
        }

        return AssistantResources.SystemPrompt
            .Replace("{{DATE_REFERENCE}}", catalog.ReferenceDate.ToString("yyyy-MM-dd"))
            .Replace("{{CATALOGUE}}", catalog.BuildPromptBlock());
    }
}
