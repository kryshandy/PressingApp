using Pressing.Core.Assistant;

namespace Pressing.Tests.Unit.Assistant;

public class IntentValidatorTests
{
    [Fact]
    public void Valide_le_contrat_complet_avec_catalogue_reel()
    {
        IntentValidationResult result = IntentValidator.Validate(AssistantTestData.ValidJson(), AssistantTestData.Catalog());

        Assert.Equal(AssistantIntentions.CreateOrder, result.Payload.Intent);
        Assert.Equal(5000m, result.Payload.Amount);
        Assert.Single(result.Payload.Items);
        Assert.Equal("Chemise", result.Payload.Items[0].Category);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Les_champs_inconnus_restent_null_sans_valeur_inventee()
    {
        string json = AssistantTestData.ValidJson()
            .Replace("\"name\": null", "\"name\": null")
            .Replace("\"amount\": 5000", "\"amount\": null")
            .Replace("\"deposit\": 5000", "\"deposit\": null");

        IntentPayload payload = IntentValidator.Validate(json, AssistantTestData.Catalog()).Payload;

        Assert.Null(payload.Amount);
        Assert.Null(payload.Deposit);
        Assert.Null(payload.Client.Name);
        Assert.Null(payload.Ticket);
    }

    [Fact]
    public void Refuse_une_intention_hors_contrat()
    {
        string json = AssistantTestData.ValidJson(intent: "delete_everything");

        Assert.Throws<AssistantContractException>(() =>
            IntentValidator.Validate(json, AssistantTestData.Catalog()));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Refuse_une_confidence_hors_borne(double confidence)
    {
        string json = AssistantTestData.ValidJson(confidence: confidence);

        Assert.Throws<AssistantContractException>(() =>
            IntentValidator.Validate(json, AssistantTestData.Catalog()));
    }

    [Fact]
    public void Refuse_un_mode_de_paiement_non_contractuel()
    {
        string json = AssistantTestData.ValidJson().Replace("\"payment_method\": null", "\"payment_method\": \"cheque\"");

        Assert.Throws<AssistantContractException>(() =>
            IntentValidator.Validate(json, AssistantTestData.Catalog()));
    }

    [Fact]
    public void Signale_une_categorie_et_un_service_absents_du_catalogue()
    {
        string json = AssistantTestData.ValidJson(category: "Dragons", service: "Polissage");

        IntentValidationResult result = IntentValidator.Validate(json, AssistantTestData.Catalog());

        Assert.Contains(result.Warnings, w => w.Contains("Dragons", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, w => w.Contains("Polissage", StringComparison.Ordinal));
    }

    [Fact]
    public void Accepte_un_synonyme_declare_dans_le_catalogue()
    {
        var catalog = new CatalogHints(
            [new CatalogTerm("Costume", ["costard"])],
            [new CatalogTerm("Nettoyage à sec", [])],
            new DateOnly(2026, 11, 27));
        string json = AssistantTestData.ValidJson(category: "costard", service: "Nettoyage à sec");

        IntentValidationResult result = IntentValidator.Validate(json, catalog);

        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Refuse_une_date_de_retrait_mal_formatee()
    {
        string json = AssistantTestData.ValidJson()
            .Replace("\"pickup_date\": null", "\"pickup_date\": \"02/12/2026\"");

        Assert.Throws<AssistantContractException>(() =>
            IntentValidator.Validate(json, AssistantTestData.Catalog()));
    }
}
