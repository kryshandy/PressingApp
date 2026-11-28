using Pressing.Core.Assistant;

namespace Pressing.Tests.Unit.Assistant;

public class IntentEvidenceGuardTests
{
    private static readonly CatalogHints Catalog = new(
        new[] { "Chemise", "Pantalon" },
        new[] { "Lavage", "Repassage" },
        new DateOnly(2026, 11, 27));

    private static IntentPayload Payload(string intent, string text) =>
        new(intent, new IntentClient(null, null), [], null, null, null, null,
            null, null, 0.9, text);

    [Fact]
    public void Un_changement_detat_est_reconnu_meme_si_le_modele_propose_un_retrait()
    {
        string text = "La chemise est prête.";
        var warnings = new List<string>();

        IntentPayload result = IntentEvidenceGuard.Apply(
            Payload(AssistantIntentions.RegisterPickup, text), text, Catalog, warnings);

        Assert.Equal(AssistantIntentions.UpdateItemStatus, result.Intent);
        Assert.Contains(warnings, w => w.Contains("Intention recalée", StringComparison.Ordinal));
    }

    [Fact]
    public void Une_hesitation_sans_action_impose_unknown()
    {
        string text = "Euh, je ne sais pas trop, peut-être quelque chose avec ce pantalon.";

        IntentPayload result = IntentEvidenceGuard.Apply(
            Payload(AssistantIntentions.CreateOrder, text), text, Catalog, []);

        Assert.Equal(AssistantIntentions.Unknown, result.Intent);
    }

    [Fact]
    public void Une_action_explicite_prend_le_pas_sur_la_hesitation()
    {
        string text = "Je voudrais faire laver une chemise, euh, s il vous plait.";

        IntentPayload result = IntentEvidenceGuard.Apply(
            Payload(AssistantIntentions.CreateOrder, text), text, Catalog, []);

        Assert.Equal(AssistantIntentions.CreateOrder, result.Intent);
    }

    [Fact]
    public void Sans_marqueur_la_proposition_du_modele_est_conservee()
    {
        string text = "Une question quelconque.";

        IntentPayload result = IntentEvidenceGuard.Apply(
            Payload(AssistantIntentions.FindOrder, text), text, Catalog, []);

        Assert.Equal(AssistantIntentions.FindOrder, result.Intent);
    }

    [Fact]
    public void Une_mention_darticle_sans_action_ne_devient_pas_une_commande()
    {
        string text = "Une chemise, peut-être.";

        IntentPayload result = IntentEvidenceGuard.Apply(
            Payload(AssistantIntentions.CreateOrder, text), text, Catalog, []);

        Assert.Equal(AssistantIntentions.Unknown, result.Intent);
    }
}
