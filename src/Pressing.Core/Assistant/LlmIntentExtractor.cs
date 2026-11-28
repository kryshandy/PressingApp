using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pressing.Core.Dtos;

namespace Pressing.Core.Assistant;

/// <summary>Client HTTP du llama.cpp server (/v1/chat/completions) avec schéma JSON imposé.</summary>
public sealed class LlmIntentExtractor : HttpAssistantClientBase, IIntentExtractor, IM2IntentExtractor
{
    private readonly AssistantOptions _options;
    private readonly string _endpoint;

    public LlmIntentExtractor(HttpClient httpClient, AssistantOptions? options = null)
        : base(httpClient)
    {
        _options = options ?? AssistantOptions.FromEnvironment();
        _options.EnsureLoopbackUrls();
        _endpoint = _options.LlmUrl.TrimEnd('/') + "/v1/chat/completions";
    }

    public async Task<IntentExtraction> ExtractAsync(
        string texte,
        CatalogHints catalog,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texte);
        ArgumentNullException.ThrowIfNull(catalog);
        if (string.IsNullOrWhiteSpace(texte))
        {
            throw new ArgumentException("Le texte à interpréter est vide.", nameof(texte));
        }

        string system = AssistantPrompt.BuildSystemPrompt(catalog);
        var requestBody = new
        {
            model = _options.LlmModelAlias,
            messages = new[]
            {
                new { role = "system", content = system },
                new { role = "user", content = texte }
            },
            temperature = _options.LlmTemperature,
            max_tokens = 512,
            response_format = new
            {
                type = "json_schema",
                json_schema = new
                {
                    name = "pressing_intent",
                    strict = true,
                    schema = JsonNode.Parse(AssistantResources.IntentSchema)
                }
            }
        };
        string body = JsonSerializer.Serialize(requestBody);

        using HttpResponseMessage response = await SendWithOneRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            },
            _options.LlmTimeoutSeconds,
            cancellationToken);

        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new AssistantEngineUnavailableException(
                $"Le moteur LLM a répondu {(int)response.StatusCode}. {responseBody}");
        }

        string? content;
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            JsonElement choices = document.RootElement.GetProperty("choices");
            if (choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            {
                throw new AssistantContractException("Réponse LLM sans choix.");
            }
            content = choices[0].GetProperty("message").GetProperty("content").GetString();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new AssistantContractException("Réponse LLM illisible.", ex);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new AssistantContractException("Réponse LLM vide.");
        }

        return ApplyGuardrails(content, texte, catalog);
    }

    /// <summary>Contrat M2 : extraction du DTO officiel à partir du catalogue simple.</summary>
    public async Task<AssistantIntentDto> ExtractAsync(
        string text,
        AssistantCatalogHints hints,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(hints);
        var catalog = new CatalogHints(hints.Categories, hints.Services);
        IntentExtraction extraction = await ExtractAsync(text, catalog, ct);
        return extraction.Payload.ToDto();
    }

    /// <summary>Vérifie que le serveur LLM local répond, sans génération.</summary>
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response = await SendWithOneRetryAsync(
                () => new HttpRequestMessage(HttpMethod.Get, _options.LlmUrl.TrimEnd('/') + "/health"),
                _options.LlmTimeoutSeconds,
                cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (AssistantEngineUnavailableException)
        {
            return false;
        }
    }

    /// <summary>
    /// Applique la chaîne complète des garde-fous M1 à une réponse JSON brute.
    /// Méthode publique afin que le banc d'évaluation puisse rejouer exactement
    /// le code de production sur des sorties déjà enregistrées, sans appel réseau.
    /// </summary>
    public static IntentExtraction ApplyGuardrails(
        string rawJson,
        string texte,
        CatalogHints catalog)
    {
        // Le modele peut produire une date non conforme au contrat (par exemple un
        // libelle relatif). Ce n'est pas une information metier prouvee : on la met
        // a null avant la validation, plutot que de refuser toute la reponse.
        string rawForValidation = rawJson;
        bool pickupDateRepaired = false;
        IntentValidationResult validation;
        try
        {
            validation = IntentValidator.Validate(rawForValidation, catalog);
        }
        catch (AssistantContractException) when (TryRepairPickupDate(rawJson, out string repaired))
        {
            pickupDateRepaired = true;
            rawForValidation = repaired;
            validation = IntentValidator.Validate(rawForValidation, catalog);
        }

        var warnings = validation.Warnings.ToList();
        if (pickupDateRepaired)
        {
            warnings.Add("pickup_date invalide remplacé par null : aucun format AAAA-MM-JJ prouvé.");
        }

        // Le texte d'origine est la source de verite : le modele peut normaliser
        // les accents ou la ponctuation malgre la consigne. On garantit la recopie exacte,
        // et on recale express sur les mots reellement prononces (jamais false par defaut).
        IntentPayload payload = validation.Payload with
        {
            Transcript = texte,
            Items = validation.Payload.Items
                .Select(item => item with { Express = ReconcileExpress(item.Express, texte) })
                .ToArray()
        };

        // P3 : l'action est vérifiée avant les champs, afin que les règles de preuve
        // métier utilisent la bonne intention (notamment amount sur un paiement).
        payload = IntentEvidenceGuard.Apply(payload, texte, catalog, warnings);

        // P1/P2 : aucune information métier n'est conservée sans preuve textuelle,
        // alias de catalogue non ambigu ou normalisation deterministe documentée.
        payload = TranscriptEvidenceValidator.Apply(payload, texte, catalog, warnings);

        // Dernière barrière anti-invention : une phrase incompréhensible ne peut
        // entraîner aucune création, aucun article et aucune information client.
        if (payload.Intent == AssistantIntentions.Unknown)
        {
            payload = payload with
            {
                Client = new IntentClient(null, null),
                Items = [],
                Ticket = null,
                Amount = null,
                PaymentMethod = null,
                Deposit = null,
                PickupDate = null,
                ItemStatus = null
            };
        }

        // Le payload assaini doit rester conforme au contrat figé.
        try
        {
            _ = IntentValidator.Validate(
                System.Text.Json.JsonSerializer.Serialize(payload), catalog);
        }
        catch (AssistantContractException ex)
        {
            throw new AssistantContractException("Le payload assaini viole le contrat.", ex);
        }

        return new IntentExtraction(rawJson, payload, warnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// Répare uniquement un `pickup_date` non conforme, sans toucher aux autres champs.
    /// Le JSON d'origine reste disponible dans `IntentExtraction.RawJson`.
    /// </summary>
    private static bool TryRepairPickupDate(string rawJson, out string repaired)
    {
        repaired = rawJson;
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(rawJson);
        }
        catch (JsonException)
        {
            return false;
        }

        if (node is not JsonObject root || root["pickup_date"] is not JsonValue value ||
            !value.TryGetValue<string>(out string? date) || string.IsNullOrWhiteSpace(date) ||
            DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            return false;
        }

        root["pickup_date"] = null;
        repaired = root.ToJsonString();
        return true;
    }

    private static bool? ReconcileExpress(bool? modelExpress, string texte)
    {
        string normalized = NormalizeText(texte);
        bool explicitRefusal =
            normalized.Contains("pas express", StringComparison.Ordinal) ||
            normalized.Contains("pas d'express", StringComparison.Ordinal) ||
            normalized.Contains("sans express", StringComparison.Ordinal) ||
            normalized.Contains("non express", StringComparison.Ordinal) ||
            (normalized.Contains("ne veux pas", StringComparison.Ordinal) &&
             normalized.Contains("express", StringComparison.Ordinal));
        if (explicitRefusal)
        {
            return false;
        }

        return normalized.Contains("express", StringComparison.Ordinal) ? true : null;
    }

    private static string NormalizeText(string value)
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

        return builder.ToString().ToLowerInvariant();
    }
}
