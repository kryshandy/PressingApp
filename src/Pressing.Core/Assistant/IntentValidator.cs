using System.Globalization;
using System.Text.Json;

namespace Pressing.Core.Assistant;

public sealed record IntentValidationResult(IntentPayload Payload, IReadOnlyList<string> Warnings);

/// <summary>Validation stricte du contrat JSON et rapprochement avec le catalogue fourni.</summary>
public static class IntentValidator
{
    private static readonly string[] RootFields =
    [
        "intent", "client", "items", "ticket", "amount", "payment_method",
        "deposit", "pickup_date", "item_status", "confidence", "transcript"
    ];

    private static readonly string[] ClientFields = ["name", "phone"];
    private static readonly string[] ItemFields = ["category", "service", "quantity", "express"];

    public static IntentValidationResult Validate(string json, CatalogHints catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new AssistantContractException(AssistantMessages.InvalidContract);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new AssistantContractException(AssistantMessages.InvalidContract, ex);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new AssistantContractException(AssistantMessages.InvalidContract);
            }

            var errors = new List<string>();
            RejectUnknownProperties(root, RootFields, errors);

            string intent = ReadString(root, "intent", errors, required: true) ?? AssistantIntentions.Unknown;
            if (!AssistantIntentions.All.Contains(intent, StringComparer.Ordinal))
            {
                errors.Add($"Intention inconnue : {intent}");
            }

            JsonElement clientElement = ReadObject(root, "client", errors);
            RejectUnknownProperties(clientElement, ClientFields, errors, "client");
            string? clientName = ReadString(clientElement, "name", errors, required: true, path: "client");
            string? clientPhone = ReadString(clientElement, "phone", errors, required: true, path: "client");

            JsonElement itemsElement = ReadArray(root, "items", errors);
            var items = new List<IntentItem>();
            var warnings = new List<string>();
            int index = 0;
            IEnumerable<JsonElement> itemElements = itemsElement.ValueKind == JsonValueKind.Array
                ? itemsElement.EnumerateArray()
                : [];
            foreach (JsonElement itemElement in itemElements)
            {
                string path = $"items[{index++}]";
                if (itemElement.ValueKind != JsonValueKind.Object)
                {
                    errors.Add($"{path} doit être un objet.");
                    continue;
                }

                RejectUnknownProperties(itemElement, ItemFields, errors, path);
                string? category = ReadString(itemElement, "category", errors, required: true, path: path);
                string? service = ReadString(itemElement, "service", errors, required: true, path: path);
                int? quantity = ReadInt(itemElement, "quantity", errors, path: path, minimum: 1);
                bool? express = ReadBool(itemElement, "express", errors, path: path);

                if (!string.IsNullOrWhiteSpace(category) && !catalog.ContainsCategory(category))
                {
                    warnings.Add($"Catégorie absente du catalogue : {category}");
                }
                if (!string.IsNullOrWhiteSpace(service) && !catalog.ContainsService(service))
                {
                    warnings.Add($"Service absent du catalogue : {service}");
                }

                items.Add(new IntentItem(category, service, quantity, express));
            }

            string? ticket = ReadString(root, "ticket", errors, required: true);
            decimal? amount = ReadDecimal(root, "amount", errors);
            string? payment = ReadString(root, "payment_method", errors, required: true);
            if (payment is not null && !AssistantPaymentMethods.All.Contains(payment, StringComparer.Ordinal))
            {
                errors.Add($"Mode de paiement inconnu : {payment}");
            }
            decimal? deposit = ReadDecimal(root, "deposit", errors);
            if (amount is not null && deposit is not null && deposit > amount)
            {
                errors.Add("deposit ne peut pas dépasser amount.");
            }
            string? pickupDate = ReadString(root, "pickup_date", errors, required: true);
            if (pickupDate is not null && !DateOnly.TryParseExact(pickupDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                errors.Add("pickup_date doit être au format AAAA-MM-JJ.");
            }
            string? itemStatus = ReadString(root, "item_status", errors, required: true);
            if (itemStatus is not null && !AssistantItemStatuses.All.Contains(itemStatus, StringComparer.Ordinal))
            {
                errors.Add($"État d'article inconnu : {itemStatus}");
            }
            double? confidence = ReadDouble(root, "confidence", errors);
            if (confidence is < 0 or > 1)
            {
                errors.Add("confidence doit être comprise entre 0 et 1.");
            }
            string transcript = ReadString(root, "transcript", errors, required: true) ?? "";

            if (errors.Count > 0)
            {
                throw new AssistantContractException($"{AssistantMessages.InvalidContract} : {string.Join(" ; ", errors)}");
            }

            var payload = new IntentPayload(
                intent,
                new IntentClient(clientName, clientPhone),
                items,
                ticket,
                amount,
                payment,
                deposit,
                pickupDate,
                itemStatus,
                confidence!.Value,
                transcript);

            return new IntentValidationResult(payload, warnings.Distinct(StringComparer.Ordinal).ToArray());
        }
    }

    private static void RejectUnknownProperties(
        JsonElement element,
        IReadOnlyCollection<string> allowed,
        List<string> errors,
        string? path = null)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        string prefix = path is null ? "" : path + ".";
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
            {
                errors.Add($"{prefix}{property.Name} n'est pas autorisé par le contrat.");
            }
        }
    }

    private static JsonElement ReadObject(JsonElement parent, string name, List<string> errors)
    {
        if (!parent.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{name} doit être un objet.");
            return default;
        }
        return value;
    }

    private static JsonElement ReadArray(JsonElement parent, string name, List<string> errors)
    {
        if (!parent.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"{name} doit être un tableau.");
            return default;
        }
        return value;
    }

    private static string? ReadString(JsonElement parent, string name, List<string> errors, bool required = false, string? path = null)
    {
        string fullName = path is null ? name : $"{path}.{name}";
        if (!parent.ValueKind.Equals(JsonValueKind.Object) || !parent.TryGetProperty(name, out JsonElement value))
        {
            if (required) errors.Add($"{fullName} est requis.");
            return null;
        }
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{fullName} doit être une chaîne ou null.");
            return null;
        }
        return value.GetString();
    }

    private static decimal? ReadDecimal(JsonElement parent, string name, List<string> errors)
    {
        if (parent.ValueKind != JsonValueKind.Object) return null;
        if (!parent.TryGetProperty(name, out JsonElement value))
        {
            errors.Add($"{name} est requis.");
            return null;
        }
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out decimal number))
        {
            errors.Add($"{name} doit être un nombre ou null.");
            return null;
        }
        if (number < 0)
        {
            errors.Add($"{name} ne peut pas être négatif.");
        }
        return number;
    }

    private static double? ReadDouble(JsonElement parent, string name, List<string> errors)
    {
        if (parent.ValueKind != JsonValueKind.Object) return null;
        if (!parent.TryGetProperty(name, out JsonElement value))
        {
            errors.Add($"{name} est requis.");
            return null;
        }
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double number))
        {
            errors.Add($"{name} doit être un nombre.");
            return null;
        }
        return number;
    }

    private static int? ReadInt(JsonElement parent, string name, List<string> errors, string? path, int minimum)
    {
        string fullName = path is null ? name : $"{path}.{name}";
        if (parent.ValueKind != JsonValueKind.Object) return null;
        if (!parent.TryGetProperty(name, out JsonElement value))
        {
            errors.Add($"{fullName} est requis.");
            return null;
        }
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number))
        {
            errors.Add($"{fullName} doit être un entier ou null.");
            return null;
        }
        if (number < minimum)
        {
            errors.Add($"{fullName} doit être ≥ {minimum}.");
        }
        return number;
    }

    private static bool? ReadBool(JsonElement parent, string name, List<string> errors, string? path)
    {
        string fullName = path is null ? name : $"{path}.{name}";
        if (parent.ValueKind != JsonValueKind.Object) return null;
        if (!parent.TryGetProperty(name, out JsonElement value))
        {
            errors.Add($"{fullName} est requis.");
            return null;
        }
        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => AddBoolError(errors, fullName)
        };
    }

    private static bool? AddBoolError(List<string> errors, string fullName)
    {
        errors.Add($"{fullName} doit être un booléen ou null.");
        return null;
    }
}
