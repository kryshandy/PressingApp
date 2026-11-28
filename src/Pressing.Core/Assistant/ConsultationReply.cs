namespace Pressing.Core.Assistant;

/// <summary>
/// T1.6 : formulation lisible d'une réponse de consultation, sans accès aux données métier.
/// Le moteur M1 ne lit jamais la base : il affiche uniquement le brouillon produit par le LLM,
/// à valider ensuite par M2 puis par l'humain.
/// </summary>
public static class ConsultationReply
{
    private const string NoData = "Aucune donnée métier consultée : réponse à confirmer par M2 puis par l'humain.";

    public static string Format(IntentPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (payload.Intent != AssistantIntentions.FindOrder)
        {
            return "Cette phrase ne demande pas une consultation.";
        }

        return string.IsNullOrWhiteSpace(payload.Ticket)
            ? $"Consultation : aucun ticket identifié dans la phrase. {NoData}"
            : $"Consultation du ticket {payload.Ticket} : brouillon prêt. {NoData}";
    }
}
