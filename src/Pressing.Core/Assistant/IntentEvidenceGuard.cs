namespace Pressing.Core.Assistant;

/// <summary>
/// Désambiguïsation de l'action demandée. Une intention n'est conservée ou corrigée
/// que si un marqueur explicite de la transcription la justifie. En l'absence de
/// marqueur, la proposition du modèle est conservée : aucun faux unknown n'est créé.
/// </summary>
public static class IntentEvidenceGuard
{
    public static IntentPayload Apply(
        IntentPayload payload,
        string texte,
        CatalogHints catalog,
        ICollection<string> warnings)
    {
        string text = CatalogHints.NormalizeWords(texte);
        string? detected = Detect(text, catalog);

        if (detected is null || string.Equals(detected, payload.Intent, StringComparison.Ordinal))
        {
            return payload;
        }

        warnings.Add($"Intention recalée sur la transcription : {payload.Intent} → {detected}.");
        IReadOnlyList<IntentItem> items = detected is AssistantIntentions.AddPayment
            or AssistantIntentions.RegisterPickup
            or AssistantIntentions.FindOrder
            ? [] : payload.Items;

        return payload with { Intent = detected, Items = items };
    }

    /// <summary>
    /// Marqueurs explicites, testés dans un ordre déterministe. Les marqueurs d'hésitation
    /// ne s'appliquent qu'en l'absence d'action explicite, pour éviter les faux unknown.
    /// </summary>
    private static string? Detect(string text, CatalogHints catalog)
    {
        bool hasDeposit = ContainsAny(text, "depot", "depose", "faire laver", "a laver", "je laisse");
        bool hasUpdate = ContainsAny(text, "est pret", "est prete", "est en cours", "en cours", "marquer", "passer en", "mettre en");
        bool hasPickup = ContainsAny(text, "retrait", "retirer") ||
                         (ContainsAny(text, "recuperer", "recupere", "chercher") && !hasDeposit);
        bool hasPayment = ContainsAny(
            text, "acompte", "paiement", "je paie", "je verse", "regler", "virement",
            "solde", "par carte", "en especes", "mobile money");
        bool hasFind = ContainsAny(
            text, "o en est", "ou en est", "quel est le statut", "statut de",
            "je voudrais savoir", "me dire", "cherche le statut");
        bool hasHesitation = ContainsAny(
            text, "euh", "peut etre", "je ne sais pas", "je ne comprends pas",
            "quelque chose", "je ne suis pas", "pas sur de moi", "pas certain",
            "complique", "reflechir", "bonjour", "saluer", "quel temps",
            "sans rapport", "nouvelles du quartier", "parler un peu", "reviens plus tard");

        if (hasHesitation && !hasDeposit && !hasPickup && !hasUpdate && !hasPayment && !hasFind)
        {
            return AssistantIntentions.Unknown;
        }

        if (hasUpdate) return AssistantIntentions.UpdateItemStatus;
        if (hasPickup) return AssistantIntentions.RegisterPickup;
        if (hasPayment) return AssistantIntentions.AddPayment;
        if (hasFind) return AssistantIntentions.FindOrder;
        if (hasDeposit) return AssistantIntentions.CreateOrder;

        // Une mention d'article sans verbe d'action n'est pas une commande.
        if (CatalogMatcher.MentionedCategories(catalog, text).Count > 0 &&
            payloadHasNoAction(text))
        {
            return AssistantIntentions.Unknown;
        }

        return null;
    }

    private static bool payloadHasNoAction(string text) =>
        !ContainsAny(text, "je veux", "je voudrais", "donnez", "apportez", "faites");

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(needle => Contains(text, CatalogHints.Normalize(needle)));

    private static bool Contains(string haystack, string normalizedNeedle) =>
        !string.IsNullOrWhiteSpace(normalizedNeedle) &&
        $" {CatalogHints.NormalizeWords(haystack)} ".Contains($" {normalizedNeedle} ", StringComparison.Ordinal);
}
