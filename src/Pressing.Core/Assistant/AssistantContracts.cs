using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Pressing.Core.Assistant;

/// <summary>
/// Contrat JSON figé entre les membres 1 et 2 (feuille de route v3, section 4.1).
/// Ces types sont la source unique : aucun DTO concurrent ne doit être créé.
/// </summary>
public sealed record IntentPayload(
    [property: JsonPropertyName("intent")] string Intent,
    [property: JsonPropertyName("client")] IntentClient Client,
    [property: JsonPropertyName("items")] IReadOnlyList<IntentItem> Items,
    [property: JsonPropertyName("ticket")] string? Ticket,
    [property: JsonPropertyName("amount")] decimal? Amount,
    [property: JsonPropertyName("payment_method")] string? PaymentMethod,
    [property: JsonPropertyName("deposit")] decimal? Deposit,
    [property: JsonPropertyName("pickup_date")] string? PickupDate,
    [property: JsonPropertyName("item_status")] string? ItemStatus,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("transcript")] string Transcript);

public sealed record IntentClient(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("phone")] string? Phone);

public sealed record IntentItem(
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("service")] string? Service,
    [property: JsonPropertyName("quantity")] int? Quantity,
    [property: JsonPropertyName("express")] bool? Express);

public sealed record SpeechTranscription(
    string Text,
    double DurationSeconds,
    double Rms,
    bool SilenceDetected);

public sealed record IntentExtraction(
    string RawJson,
    IntentPayload Payload,
    IReadOnlyList<string> Warnings);

public enum PipelineStatus
{
    Ok,
    NotUnderstood,
    Silence,
    EngineUnavailable,
    InvalidAudio
}

public sealed record PipelineResult(
    PipelineStatus Status,
    string Transcript,
    string Message,
    IntentExtraction? Extraction,
    IReadOnlyList<string> Warnings);

public static class AssistantIntentions
{
    public const string CreateOrder = "create_order";
    public const string RegisterPickup = "register_pickup";
    public const string AddPayment = "add_payment";
    public const string UpdateItemStatus = "update_item_status";
    public const string FindOrder = "find_order";
    public const string Unknown = "unknown";

    public static readonly string[] All =
    [
        CreateOrder, RegisterPickup, AddPayment, UpdateItemStatus, FindOrder, Unknown
    ];
}

public static class AssistantPaymentMethods
{
    public static readonly string[] All = ["cash", "mobile_money", "card", "transfer"];
}

public static class AssistantItemStatuses
{
    public static readonly string[] All = ["in_progress", "ready"];
}

public static class AssistantMessages
{
    public const string EngineNotStarted = "L'assistant vocal n'est pas démarré";
    public const string EngineTimeout = "Le moteur IA local met trop de temps à répondre";
    public const string NotUnderstood = "je n'ai pas compris";
    public const string Silence = "Aucune parole détectée";
    public const string InvalidAudio = "Audio invalide ou trop long (20 secondes maximum)";
    public const string InvalidContract = "La réponse du moteur IA ne respecte pas le contrat d'intention";
}

/// <summary>Catégorie ou service connu, avec ses synonymes éventuels.</summary>
public sealed record CatalogTerm(string Label, IReadOnlyList<string> Aliases);

/// <summary>
/// Indices injectés dans le prompt par l'appelant (Membre 2). Aucun accès base de données ici.
/// </summary>
public sealed record CatalogHints(
    IReadOnlyList<CatalogTerm> Categories,
    IReadOnlyList<CatalogTerm> Services,
    DateOnly ReferenceDate)
{
    public CatalogHints(IReadOnlyList<string> categories, IReadOnlyList<string> services)
        : this(categories, services, DateOnly.FromDateTime(DateTime.Today))
    {
    }

    public CatalogHints(IEnumerable<string> categories, IEnumerable<string> services, DateOnly referenceDate)
        : this(categories.Select(x => new CatalogTerm(x, [])).ToArray(),
               services.Select(x => new CatalogTerm(x, [])).ToArray(),
               referenceDate)
    {
    }

    public bool ContainsCategory(string? value) => Contains(Categories, value);

    public bool ContainsService(string? value) => Contains(Services, value);

    public string BuildPromptBlock()
    {
        var categories = string.Join(", ", Categories.Select(x => x.Label));
        var services = string.Join(", ", Services.Select(x => x.Label));
        var aliases = Categories.Concat(Services)
            .SelectMany(x => x.Aliases)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var aliasText = aliases.Length == 0 ? "(aucun)" : string.Join(", ", aliases);
        return $"Catégories : {categories}\nServices : {services}\nSynonymes : {aliasText}";
    }

    private static bool Contains(IReadOnlyList<CatalogTerm> terms, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string needle = Normalize(value);
        return terms.Any(term =>
            string.Equals(Normalize(term.Label), needle, StringComparison.Ordinal) ||
            term.Aliases.Any(alias => string.Equals(Normalize(alias), needle, StringComparison.Ordinal)));
    }

    public static string Normalize(string value)
    {
        string decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        string text = builder.ToString()
            .ToLowerInvariant()
            .Replace('+', ' ')
            .Replace('-', ' ')
            .Replace('’', '\'')
            .Replace('\'', ' ');
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    /// <summary>
    /// Normalisation utilisée pour le rapprochement lexical : la ponctuation devient
    /// un espace, afin qu'un mot suivi d'un point soit reconnu comme ce mot.
    /// Les chiffres et les espaces sont conservés (dates et références).
    /// </summary>
    public static string NormalizeWords(string value) =>
        Regex.Replace(Regex.Replace(Normalize(value), @"[^\p{L}\p{N}\s]", " "), @"\s+", " ").Trim();
}
