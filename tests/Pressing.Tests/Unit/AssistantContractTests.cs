using System.Text.Json;
using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;

namespace Pressing.Tests.Unit;

/// <summary>
/// Verrouille le contrat d'intention partagé entre le membre 1 (moteurs IA) et le membre 2 (intégration).
/// Si un de ces tests casse, le contrat a changé : prévenir l'autre membre et mettre à jour docs/API.md.
/// </summary>
public class AssistantContractTests
{
    // Exemple de référence : phrase « Dépôt pour Mme Ngo, 3 chemises lavage repassage,
    // 1 costume nettoyage à sec express, acompte 5 000 » (section 4 du brief).
    private const string DepositSentenceJson = """
    {
      "intent": "create_order",
      "client": { "name": "Ngo", "phone": null },
      "items": [
        { "category": "Chemise", "service": "Lavage + repassage", "quantity": 3, "express": false },
        { "category": "Costume", "service": "Nettoyage à sec", "quantity": 1, "express": true }
      ],
      "ticket": null,
      "amount": null,
      "payment_method": null,
      "deposit": 5000,
      "pickup_date": "2026-12-02",
      "item_status": null,
      "confidence": 0.93,
      "transcript": "Dépôt pour Mme Ngo, 3 chemises lavage repassage, 1 costume nettoyage à sec express, acompte 5000"
    }
    """;

    private static AssistantIntentDto Parse(string json) =>
        JsonSerializer.Deserialize<AssistantIntentDto>(json, ApiJson.Options)!;

    [Fact]
    public void Le_json_du_contrat_est_lu_champ_par_champ()
    {
        var intent = Parse(DepositSentenceJson);

        Assert.Equal(AssistantIntents.CreateOrder, intent.Intent);
        Assert.Equal("Ngo", intent.Client!.Name);
        Assert.Null(intent.Client.Phone);
        Assert.Equal(2, intent.Items!.Count);
        Assert.Equal("Chemise", intent.Items[0].Category);
        Assert.Equal("Lavage + repassage", intent.Items[0].Service);
        Assert.Equal(3, intent.Items[0].Quantity);
        Assert.False(intent.Items[0].Express);
        Assert.True(intent.Items[1].Express);
        Assert.Null(intent.Ticket);
        Assert.Null(intent.Amount);
        Assert.Null(intent.PaymentMethod);
        Assert.Equal(5000m, intent.Deposit);
        Assert.Equal(new DateOnly(2026, 12, 2), intent.PickupDate);
        Assert.Null(intent.ItemStatus);
        Assert.Equal(0.93, intent.Confidence);
        Assert.StartsWith("Dépôt pour Mme Ngo", intent.Transcript);
    }

    [Fact]
    public void Un_champ_absent_vaut_null_et_n_est_jamais_invente()
    {
        var intent = Parse("""{ "intent": "unknown", "confidence": 0.2, "transcript": "euh" }""");

        Assert.Equal(AssistantIntents.Unknown, intent.Intent);
        Assert.Null(intent.Client);
        Assert.Null(intent.Items);
        Assert.Null(intent.Ticket);
        Assert.Null(intent.Amount);
        Assert.Null(intent.Deposit);
        Assert.Null(intent.PickupDate);
        Assert.Equal(0.2, intent.Confidence);
    }

    [Fact]
    public void Un_json_vide_donne_une_intention_inconnue()
    {
        var intent = Parse("{}");

        Assert.Equal(AssistantIntents.Unknown, intent.Intent);
        Assert.Equal(0, intent.Confidence);
        Assert.Equal(string.Empty, intent.Transcript);
    }

    [Fact]
    public void La_serialisation_garde_les_noms_snake_case_du_contrat()
    {
        var json = JsonSerializer.Serialize(Parse(DepositSentenceJson), ApiJson.Options);

        Assert.Contains("\"payment_method\"", json);
        Assert.Contains("\"pickup_date\"", json);
        Assert.Contains("\"item_status\"", json);
        Assert.Contains("\"pickup_date\":\"2026-12-02\"", json);
        Assert.DoesNotContain("paymentMethod", json);
        Assert.DoesNotContain("pickupDate", json);
    }

    [Fact]
    public void La_serialisation_puis_la_lecture_redonnent_la_meme_intention()
    {
        var original = Parse(DepositSentenceJson);

        var copy = Parse(JsonSerializer.Serialize(original, ApiJson.Options));

        Assert.Equal(original.Intent, copy.Intent);
        Assert.Equal(original.Deposit, copy.Deposit);
        Assert.Equal(original.PickupDate, copy.PickupDate);
        Assert.Equal(original.Items!.Count, copy.Items!.Count);
        Assert.Equal(original.Items[1].Service, copy.Items[1].Service);
        Assert.Equal(original.Confidence, copy.Confidence);
    }

    [Fact]
    public void Les_six_intentions_du_contrat_sont_reconnues()
    {
        string[] expected = ["create_order", "register_pickup", "add_payment", "update_item_status", "find_order", "unknown"];

        Assert.Equal(expected, AssistantIntents.All);
        foreach (var value in expected) Assert.True(AssistantIntents.IsKnown(value));
        Assert.False(AssistantIntents.IsKnown("delete_everything"));
        Assert.False(AssistantIntents.IsKnown(null));
        Assert.False(AssistantIntents.IsKnown("CREATE_ORDER"));
    }

    [Fact]
    public void Les_modes_de_paiement_du_contrat_correspondent_a_l_enumeration_metier()
    {
        Assert.Equal(PaymentMethod.Cash, AssistantPaymentMethods.ToPaymentMethod("cash"));
        Assert.Equal(PaymentMethod.MobileMoney, AssistantPaymentMethods.ToPaymentMethod("mobile_money"));
        Assert.Equal(PaymentMethod.Card, AssistantPaymentMethods.ToPaymentMethod("card"));
        Assert.Equal(PaymentMethod.BankTransfer, AssistantPaymentMethods.ToPaymentMethod("transfer"));
        Assert.Null(AssistantPaymentMethods.ToPaymentMethod("bitcoin"));
        Assert.Null(AssistantPaymentMethods.ToPaymentMethod(null));
        // Chaque valeur annoncée par le contrat doit avoir une correspondance.
        foreach (var value in AssistantPaymentMethods.All)
            Assert.NotNull(AssistantPaymentMethods.ToPaymentMethod(value));
    }

    [Fact]
    public void Les_statuts_d_article_du_contrat_se_limitent_a_en_traitement_et_pret()
    {
        Assert.Equal(ItemStatus.InProgress, AssistantItemStatuses.ToItemStatus("in_progress"));
        Assert.Equal(ItemStatus.Ready, AssistantItemStatuses.ToItemStatus("ready"));
        // Un retrait ou une annulation ne passe jamais par l'assistant de statut (règle de docs/API.md).
        Assert.Null(AssistantItemStatuses.ToItemStatus("picked_up"));
        Assert.Null(AssistantItemStatuses.ToItemStatus("cancelled"));
        Assert.Null(AssistantItemStatuses.ToItemStatus(null));
    }

    [Fact]
    public void Le_brouillon_est_serialise_en_camel_case_avec_enumerations_en_texte()
    {
        var draft = new OrderDraftDto(
            Intent: AssistantIntents.CreateOrder,
            Target: AssistantTarget.NewOrder,
            Client: new AssistantClientMatchDto(null, "Ngo", null, true, []),
            Items:
            [
                new OrderDraftItemDto(1, "Chemise", ServiceType.WashAndIron, "Lavage + repassage",
                    3, false, 500m, 1500m, "chemises", false)
            ],
            OrderId: null, Ticket: null, Balance: null, PaymentAmount: null, PaymentMethod: null,
            DepositAmount: 5000m, DueDate: null, ItemStatus: null, Quote: null,
            UncertainFields: ["client"],
            Warnings: ["Client inconnu : création proposée."],
            Confidence: 0.93,
            Transcript: "dépôt pour Mme Ngo");

        var json = JsonSerializer.Serialize(draft, ApiJson.Options);

        Assert.Contains("\"target\":\"NewOrder\"", json);
        Assert.Contains("\"serviceType\":\"WashAndIron\"", json);
        Assert.Contains("\"isNew\":true", json);
        Assert.Contains("\"uncertainFields\":[\"client\"]", json);
        Assert.Contains("\"depositAmount\":5000", json);

        var copy = JsonSerializer.Deserialize<OrderDraftDto>(json, ApiJson.Options)!;
        Assert.Equal(AssistantTarget.NewOrder, copy.Target);
        Assert.Equal(ServiceType.WashAndIron, copy.Items[0].ServiceType);
        Assert.Equal(1500m, copy.Items[0].LineTotal);
        Assert.Equal("Client inconnu : création proposée.", copy.Warnings.Single());
    }

    [Fact]
    public void Les_limites_du_contrat_correspondent_au_brief()
    {
        Assert.Equal(0.6, AssistantContract.DefaultConfidenceThreshold);
        Assert.Equal(2 * 1024 * 1024, AssistantContract.MaxAudioBytes);
    }
}
