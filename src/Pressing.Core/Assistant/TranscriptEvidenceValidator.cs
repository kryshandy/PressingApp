using System.Globalization;
using System.Text.RegularExpressions;

namespace Pressing.Core.Assistant;

/// <summary>
/// Garde-fou sémantique : aucune information métier n'est conservée si elle n'est pas
/// justifiée par la transcription, par un alias de catalogue ou par une normalisation
/// déterministe documentée.
/// </summary>
public static class TranscriptEvidenceValidator
{
    private static readonly string[] AdvanceMarkers =
        ["acompte", "avance", "deja paye", "j ai paye"];

    private static readonly string[] PaymentMarkers =
    [
        "acompte", "avance", "solde", "paiement", "je paie", "je verse", "verse",
        "regle", "virement", "montant", "total", "coute", "a payer", "prix"
    ];

    private static readonly string[] AmountMarkers =
        ["montant", "total", "coute", "a payer", "solde", "prix"];

    private static readonly string[] DepositMarkers =
        ["acompte", "avance", "verse", "deja paye", "je paie", "j ai paye"];

    /// <summary>
    /// Un nom n'est une donnée métier que s'il est introduit explicitement. Une simple
    /// occurrence du mot dans la phrase (« Bonjour, je voudrais... ») ne suffit pas.
    /// </summary>
    private static readonly string[] NameIntroducers =
        ["appelle", "nomme", "pour", "commande de", "dossier de", "au nom de", "client",
         "madame", "monsieur", "mme", "mr"];

    public static IntentPayload Apply(
        IntentPayload payload,
        string texte,
        CatalogHints catalog,
        ICollection<string> warnings)
    {
        string text = CatalogHints.Normalize(texte);
        (decimal? amount, decimal? deposit) = ReadMoney(text, payload.Intent);

        IntentClient client = ValidateClient(payload.Client, text, payload.Intent, warnings);
        string? ticket = ValidateTicket(payload.Ticket, text, catalog, warnings);
        decimal? amountValue = ValidateMoney(payload.Amount, amount, "amount", warnings);
        decimal? depositValue = ValidateMoney(payload.Deposit, deposit, "deposit", warnings);
        string? payment = ValidatePayment(payload.PaymentMethod, text, warnings);
        string? pickupDate = ValidateDate(payload.PickupDate, text, catalog.ReferenceDate, payload.Intent, warnings);
        string? itemStatus = ValidateStatus(payload.ItemStatus, text, warnings);

        IReadOnlyList<IntentItem> sourceItems = payload.Items;
        if (sourceItems.Count == 0 && payload.Intent == AssistantIntentions.UpdateItemStatus)
        {
            // Le modèle omet souvent l'article sur un changement d'état. Un seul
            // libellé de catalogue prononcé suffit à le retrouver sans invention.
            IReadOnlyList<string> mentioned = CatalogMatcher.MentionedCategories(catalog, texte);
            if (mentioned.Count == 1)
            {
                warnings.Add($"Article retrouvé dans la transcription : {mentioned[0]}.");
                sourceItems = [new IntentItem(mentioned[0], null, null, null)];
            }
        }

        IReadOnlyList<IntentItem> items = ValidateItems(
            sourceItems, texte, text, catalog, payload.Intent, warnings);

        return payload with
        {
            Client = client,
            Items = items,
            Ticket = ticket,
            Amount = amountValue,
            Deposit = depositValue,
            PaymentMethod = payment,
            PickupDate = pickupDate,
            ItemStatus = itemStatus
        };
    }

    private static IntentClient ValidateClient(
        IntentClient client,
        string text,
        string intent,
        ICollection<string> warnings)
    {
        string? name = client.Name;
        // Règle documentée : une consultation (find_order) s'identifie par ticket ou
        // statut, jamais par le nom cité dans la phrase (« dossier de Marie »).
        if (intent == AssistantIntentions.FindOrder && name is not null)
        {
            warnings.Add($"Client non retenu sur une consultation : client.name ({name}) supprimé.");
            name = null;
        }
        else if (name is not null && !NameIsSpoken(name, text))
        {
            warnings.Add($"Preuve textuelle absente : client.name ({name}) supprimé.");
            name = null;
        }

        string? phone = client.Phone;
        if (phone is not null)
        {
            string digits = Digits(phone);
            if (digits.Length == 0 || !Digits(text).Contains(digits, StringComparison.Ordinal))
            {
                warnings.Add($"Preuve textuelle absente : client.phone ({phone}) supprimé.");
                phone = null;
            }
        }

        return new IntentClient(name, phone);
    }

    private static bool NameIsSpoken(string name, string text)
    {
        string[] tokens = CatalogHints.Normalize(name)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token is not ("mme" or "m" or "mr" or "monsieur" or "madame"))
            .ToArray();
        if (tokens.Length == 0)
        {
            return false;
        }

        string haystack = CatalogHints.NormalizeWords(text);
        string pattern = string.Join(@"\s+", tokens.Select(Regex.Escape));
        foreach (Match match in Regex.Matches(haystack, pattern, RegexOptions.CultureInvariant))
        {
            int from = Math.Max(0, match.Index - 44);
            string before = haystack[from..match.Index];
            foreach (string introducer in NameIntroducers)
            {
                string marker = CatalogHints.Normalize(introducer);
                int markerIndex = before.LastIndexOf(marker, StringComparison.Ordinal);
                if (markerIndex >= 0 && before.Length - markerIndex - marker.Length <= 24)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string? ValidateTicket(string? value, string text, CatalogHints catalog, ICollection<string> warnings)
    {
        string? spoken = SpokenTicket(text, catalog.ReferenceDate);
        if (spoken is null)
        {
            if (value is null)
            {
                return null;
            }

            warnings.Add($"Preuve textuelle absente : ticket ({value}) supprimé.");
            return null;
        }

        if (value is null || !string.Equals(value, spoken, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(value is null
                ? $"Ticket retrouvé dans la transcription : {spoken}."
                : $"Ticket normalisé depuis la transcription : {spoken}");
        }

        return spoken;
    }

    private static decimal? ValidateMoney(decimal? value, decimal? spoken, string field, ICollection<string> warnings)
    {
        if (spoken is null)
        {
            if (value is not null)
            {
                warnings.Add($"Preuve textuelle absente : {field} ({value}) supprimé.");
            }

            return null;
        }

        if (value is null || spoken.Value != value.Value)
        {
            warnings.Add(value is null
                ? $"Montant retrouvé dans la transcription : {field} = {spoken}."
                : $"Montant normalisé depuis la transcription : {field} = {spoken}.");
        }

        return spoken;
    }

    private static string? ValidatePayment(string? value, string text, ICollection<string> warnings)
    {
        string? spoken = null;
        if (ContainsAny(text, "cash", "espece", "especes", "liquide")) spoken = "cash";
        else if (ContainsAny(text, "mobile money", "orange money", "mtn money", "wave")) spoken = "mobile_money";
        else if (ContainsAny(text, "card", "carte")) spoken = "card";
        else if (ContainsAny(text, "transfer", "virement")) spoken = "transfer";

        if (spoken is null)
        {
            if (value is null) return null;
            warnings.Add($"Preuve textuelle absente : payment_method ({value}) supprimé.");
            return null;
        }

        if (value is null || !string.Equals(value, spoken, StringComparison.Ordinal))
        {
            warnings.Add(value is null
                ? $"Mode de paiement retrouvé dans la transcription : {spoken}."
                : $"Mode de paiement normalisé depuis la transcription : {spoken}");
        }

        return spoken;
    }

    private static string? ValidateDate(
        string? value,
        string text,
        DateOnly reference,
        string intent,
        ICollection<string> warnings)
    {
        // Règle documentée : une date de retrait n'est extraite que sur un dépôt.
        // Sur un retrait (« je récupère aujourd'hui »), le moment de l'action n'est
        // pas une donnée métier : la vérité terrain attend pickup_date = null.
        if (intent != AssistantIntentions.CreateOrder)
        {
            if (value is not null)
            {
                warnings.Add($"Date non retenue hors dépôt : pickup_date ({value}) supprimée.");
            }

            return null;
        }

        string? spoken = SpokenDate(text, reference);
        if (spoken is null)
        {
            if (value is null) return null;
            warnings.Add($"Preuve textuelle absente : pickup_date ({value}) supprimé.");
            return null;
        }

        if (value is null || !string.Equals(value, spoken, StringComparison.Ordinal))
        {
            warnings.Add(value is null
                ? $"Date retrouvée dans la transcription : {spoken}."
                : $"Date normalisée depuis la transcription : {spoken}");
        }

        return spoken;
    }

    private static string? ValidateStatus(string? value, string text, ICollection<string> warnings)
    {
        string? spoken = null;
        if (ContainsAny(text, "pret", "prete", "prets", "pretes", "ready", "termine", "lave")) spoken = "ready";
        else if (ContainsAny(text, "en cours", "encours", "in progress", "en traitement")) spoken = "in_progress";

        if (spoken is null)
        {
            if (value is null) return null;
            warnings.Add($"Preuve textuelle absente : item_status ({value}) supprimé.");
            return null;
        }

        if (value is null || !string.Equals(value, spoken, StringComparison.Ordinal))
        {
            warnings.Add(value is null
                ? $"État d'article retrouvé dans la transcription : {spoken}."
                : $"État d'article normalisé depuis la transcription : {spoken}");
        }

        return spoken;
    }

    private static IReadOnlyList<IntentItem> ValidateItems(
        IReadOnlyList<IntentItem> items,
        string originalText,
        string normalizedText,
        CatalogHints catalog,
        string intent,
        ICollection<string> warnings)
    {
        var result = new List<IntentItem>();
        foreach (IntentItem item in items)
        {
            CatalogMatch category = CatalogMatcher.ResolveCategory(catalog, item.Category, originalText);
            CatalogMatch service = CatalogMatcher.ResolveServiceForItem(
                catalog, item.Service, originalText, category.Value ?? item.Category);
            WarnCatalog(category, "category", warnings);
            WarnCatalog(service, "service", warnings);

            string? spokenCategory = CatalogMatcher.SpokenAlias(catalog, item.Category, originalText);
            string? quantitySupport = spokenCategory ?? category.Value ?? service.Value ?? item.Service;
            decimal? spoken = quantitySupport is null
                ? null
                : TranscriptNumbers.NearSafe(
                    normalizedText,
                    quantitySupport,
                    excludeNumberInsideTerm: spokenCategory is not null);
            int? quantity = null;

            if (spoken is not null && spoken >= 1 && spoken <= int.MaxValue)
            {
                if (item.Quantity is null || item.Quantity.Value != (int)spoken.Value)
                {
                    warnings.Add($"Quantité normalisée depuis la transcription : {spoken}.");
                }

                quantity = (int)spoken.Value;
            }
            else if (quantitySupport is not null && intent == AssistantIntentions.UpdateItemStatus)
            {
                // Règle documentée : un article explicitement cité dans une demande de
                // changement d'état concerne une seule pièce, sauf nombre contraire.
                // Elle s'applique aussi quand le modèle avait proposé 1 sans preuve chiffrée.
                quantity = 1;
                if (item.Quantity != 1)
                {
                    warnings.Add("Quantité 1 appliquée à l'article cité dans un changement d'état.");
                }
            }
            else if (item.Quantity is not null)
            {
                warnings.Add($"Preuve textuelle absente : items[].quantity ({item.Quantity}) supprimée.");
            }

            if (category.Value is null && service.Value is null)
            {
                warnings.Add("Article supprimé : ni catégorie ni service prouvé par la transcription.");
                continue;
            }

            result.Add(item with
            {
                Category = category.Value,
                Service = service.Value,
                Quantity = quantity
            });
        }

        return result;
    }

    private static void WarnCatalog(CatalogMatch match, string field, ICollection<string> warnings)
    {
        if (match.Reason is null)
        {
            return;
        }

        warnings.Add(match.Reason.Contains("catalogue", StringComparison.OrdinalIgnoreCase)
            ? $"Erreur catalogue {field} : {match.Reason}"
            : $"Preuve textuelle absente : {field} — {match.Reason}");
    }

    /// <summary>
    /// Règles documentées : sur un paiement, le montant prononcé est amount, et il est
    /// aussi deposit quand le mot « acompte » ou « avance » est prononcé. Sur un dépôt,
    /// un acompte ne justifie jamais le montant total de la commande.
    /// </summary>
    private static (decimal? Amount, decimal? Deposit) ReadMoney(string text, string intent)
    {
        decimal? amount = null;
        decimal? deposit = null;
        bool isPayment = intent == AssistantIntentions.AddPayment;
        string[] moneyMarkers = isPayment ? PaymentMarkers : AmountMarkers;

        foreach (NumberMatch match in TranscriptNumbers.Find(text))
        {
            int from = Math.Max(0, match.Index - 28);
            int to = Math.Min(text.Length, match.Index + match.Length + 14);
            string context = text[from..to];
            bool isAdvance = AdvanceMarkers.Any(marker => Contains(context, CatalogHints.Normalize(marker)));
            bool isAmount = moneyMarkers.Any(marker => Contains(context, CatalogHints.Normalize(marker)));
            bool isDepositOnly = !isPayment && DepositMarkers.Any(marker => Contains(context, CatalogHints.Normalize(marker)));

            if (isPayment && isAmount)
            {
                amount ??= match.Value;
                if (isAdvance) deposit ??= match.Value;
            }
            else if (!isPayment && isDepositOnly)
            {
                deposit ??= match.Value;
            }
            else if (!isPayment && isAmount)
            {
                amount ??= match.Value;
            }
        }

        return (amount, deposit);
    }

    private static string? SpokenTicket(string text, DateOnly reference)
    {
        Match canonical = Regex.Match(text, @"press\s+(\d{4})\s+(\d{1,5})", RegexOptions.CultureInvariant);
        if (canonical.Success &&
            int.TryParse(canonical.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int year) &&
            int.TryParse(canonical.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int number))
        {
            return $"PRESS-{year}-{number:D5}";
        }

        Match simple = Regex.Match(
            text,
            @"(?:ticket|commande|numero|reference|ref)\s+(\d{1,6})",
            RegexOptions.CultureInvariant);
        if (simple.Success && int.TryParse(simple.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int id))
        {
            return $"PRESS-{reference.Year}-{id:D5}";
        }

        return null;
    }

    private static string? SpokenDate(string text, DateOnly reference)
    {
        Match iso = Regex.Match(text, @"\b(\d{4})-(\d{1,2})-(\d{1,2})\b", RegexOptions.CultureInvariant);
        if (iso.Success &&
            DateOnly.TryParseExact(iso.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly isoDate))
        {
            return isoDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        Match french = Regex.Match(text, @"\b(\d{1,2})[/-](\d{1,2})[/-](\d{4})\b", RegexOptions.CultureInvariant);
        if (french.Success &&
            DateOnly.TryParseExact(
                $"{french.Groups[3].Value}-{french.Groups[2].Value}-{french.Groups[1].Value}",
                "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly frenchDate))
        {
            return frenchDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (Contains(text, "aujourd hui")) return reference.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (Contains(text, "apres demain")) return reference.AddDays(2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (Contains(text, "demain")) return reference.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        string[] weekdays = ["lundi", "mardi", "mercredi", "jeudi", "vendredi", "samedi", "dimanche"];
        for (int i = 0; i < weekdays.Length; i++)
        {
            if (!Contains(text, weekdays[i])) continue;
            DayOfWeek target = (DayOfWeek)((i + 1) % 7);
            int delta = ((int)target - (int)reference.DayOfWeek + 7) % 7;
            if (delta == 0) delta = 7;
            return reference.AddDays(delta).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(needle => Contains(text, CatalogHints.Normalize(needle)));

    private static bool Contains(string haystack, string normalizedNeedle)
    {
        if (string.IsNullOrWhiteSpace(normalizedNeedle))
        {
            return false;
        }

        string text = CatalogHints.NormalizeWords(haystack);
        return $" {text} ".Contains($" {normalizedNeedle} ", StringComparison.Ordinal);
    }

    private static string Digits(string value) =>
        new(value.Where(char.IsDigit).ToArray());
}
