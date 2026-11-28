using Pressing.Core.Assistant;

namespace Pressing.Tests.Unit.Assistant;

public class TranscriptEvidenceValidatorTests
{
    private static CatalogHints Catalog() => new(
        new[]
        {
            new CatalogTerm("Chemise", []),
            new CatalogTerm("Tapis", []),
            new CatalogTerm("Manteau", []),
            new CatalogTerm("Rideau", []),
            new CatalogTerm("Costume 2 pièces", ["costard", "costume 2 pièces", "costume 2 pieces"]),
            new CatalogTerm("Costume 3 pièces", ["costard", "costume 3 pièces", "costume 3 pieces"])
        },
        new[]
        {
            new CatalogTerm("Lavage", []),
            new CatalogTerm("Repassage", [])
        },
        new DateOnly(2026, 11, 27));

    private static IntentPayload Payload(
        string intent = AssistantIntentions.CreateOrder,
        string? category = "Chemise",
        string? service = "Lavage",
        int? quantity = 2,
        string? name = null,
        decimal? amount = null,
        decimal? deposit = null,
        string? payment = null,
        string? pickupDate = null,
        string? itemStatus = null,
        string transcript = "Dépôt de deux chemises à laver.") =>
        new(
            intent,
            new IntentClient(name, null),
            category is null && service is null
                ? []
                : [new IntentItem(category, service, quantity, null)],
            null, amount, payment, deposit, pickupDate, itemStatus, 0.9, transcript);

    [Fact]
    public void Un_paiement_reconstruit_la_preuve_du_montant_et_du_mode()
    {
        var warnings = new List<string>();

        IntentPayload result = TranscriptEvidenceValidator.Apply(
            Payload(
                intent: AssistantIntentions.AddPayment,
                category: null,
                service: null,
                transcript: "Acompte de cinq mille en espèces."),
            "Acompte de cinq mille en espèces.",
            Catalog(),
            warnings);

        Assert.Equal(5000m, result.Amount);
        Assert.Equal(5000m, result.Deposit);
        Assert.Equal("cash", result.PaymentMethod);
        Assert.Contains(warnings, w => w.Contains("Montant retrouvé", StringComparison.Ordinal));
    }

    [Fact]
    public void Une_date_relative_n_est_extraite_que_sur_un_depot()
    {
        string depositText = "Dépôt de deux chemises à laver, à récupérer demain.";
        IntentPayload deposit = TranscriptEvidenceValidator.Apply(
            Payload(transcript: depositText), depositText, Catalog(), []);
        string pickupText = "Je récupère ma commande aujourd'hui.";
        IntentPayload pickup = TranscriptEvidenceValidator.Apply(
            Payload(
                intent: AssistantIntentions.RegisterPickup,
                category: null,
                service: null,
                pickupDate: "2026-11-27",
                transcript: pickupText),
            pickupText,
            Catalog(),
            []);

        Assert.Equal("2026-11-28", deposit.PickupDate);
        Assert.Null(pickup.PickupDate);
    }

    [Fact]
    public void Un_nom_cite_dans_une_consultation_est_supprime()
    {
        string text = "O en est le dossier de Marie ?";
        IntentPayload result = TranscriptEvidenceValidator.Apply(
            Payload(
                intent: AssistantIntentions.FindOrder,
                category: null,
                service: null,
                name: "Marie",
                transcript: text),
            text,
            Catalog(),
            []);

        Assert.Null(result.Client.Name);
    }

    [Fact]
    public void Un_article_omis_est_retrouve_sur_un_changement_detat()
    {
        string text = "La chemise est prête.";
        IntentPayload result = TranscriptEvidenceValidator.Apply(
            Payload(
                intent: AssistantIntentions.UpdateItemStatus,
                category: null,
                service: null,
                quantity: null,
                itemStatus: null,
                transcript: text),
            text,
            Catalog(),
            []);

        Assert.Single(result.Items);
        Assert.Equal("Chemise", result.Items[0].Category);
        Assert.Equal(1, result.Items[0].Quantity);
        Assert.Equal("ready", result.ItemStatus);
    }

    [Fact]
    public void Une_categorie_sans_preuve_est_supprimee_sans_inventer_de_valeur()
    {
        string text = "Dépôt à laver.";
        IntentPayload result = TranscriptEvidenceValidator.Apply(
            Payload(category: "Tapis", service: null, transcript: text),
            text,
            Catalog(),
            []);

        Assert.Single(result.Items);
        Assert.Null(result.Items[0].Category);
        Assert.Equal("Lavage", result.Items[0].Service);
        Assert.Null(result.Items[0].Quantity);
    }

    [Fact]
    public void Un_salut_nest_pas_un_nom_client()
    {
        string text = "Bonjour, je voudrais faire laver trois chemises.";
        var warnings = new List<string>();

        IntentPayload result = TranscriptEvidenceValidator.Apply(
            Payload(name: "Bonjour", transcript: text),
            text,
            Catalog(),
            warnings);

        Assert.Null(result.Client.Name);
        Assert.Contains(warnings, w => w.Contains("client.name", StringComparison.Ordinal));
    }

    [Fact]
    public void Un_alias_ambigu_ne_prouve_pas_la_categorie_mais_conserve_la_quantite()
    {
        string text = "Dépôt d'un costard à laver.";
        var warnings = new List<string>();

        IntentPayload result = TranscriptEvidenceValidator.Apply(
            Payload(category: "Costume 2 pièces", quantity: 1, transcript: text),
            text,
            Catalog(),
            warnings);

        Assert.Single(result.Items);
        Assert.Null(result.Items[0].Category);
        Assert.Equal("Lavage", result.Items[0].Service);
        Assert.Equal(1, result.Items[0].Quantity);
        Assert.Contains(warnings, w => w.Contains("Alias ambigu", StringComparison.Ordinal));
    }

    [Fact]
    public void Une_categorie_chiffree_peut_etre_prononcee_en_toutes_lettres()
    {
        string text = "Dépôt d'un costume deux pièces à laver.";

        IntentPayload result = TranscriptEvidenceValidator.Apply(
            Payload(category: "Costume 2 pièces", quantity: 1, transcript: text),
            text,
            Catalog(),
            []);

        Assert.Single(result.Items);
        Assert.Equal("Costume 2 pièces", result.Items[0].Category);
        Assert.Equal(1, result.Items[0].Quantity);
    }

    [Fact]
    public void Le_chiffre_du_libelle_nest_pas_une_quantite_sur_changement_detat()
    {
        string text = "Le costume deux pièces est prêt.";

        IntentPayload result = TranscriptEvidenceValidator.Apply(
            Payload(
                intent: AssistantIntentions.UpdateItemStatus,
                category: "Costume 2 pièces",
                service: null,
                quantity: null,
                itemStatus: null,
                transcript: text),
            text,
            Catalog(),
            []);

        Assert.Single(result.Items);
        Assert.Equal("Costume 2 pièces", result.Items[0].Category);
        Assert.Equal(1, result.Items[0].Quantity);
    }

    [Fact]
    public void Un_nom_introduit_explicitement_est_conserve()
    {
        string text = "Dépôt de trois chemises à laver, je m'appelle Astride.";

        IntentPayload result = TranscriptEvidenceValidator.Apply(
            Payload(name: "Astride", transcript: text),
            text,
            Catalog(),
            []);

        Assert.Equal("Astride", result.Client.Name);
    }
}
