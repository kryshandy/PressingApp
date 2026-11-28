using System.Text.Json.Nodes;
using Pressing.Core.Assistant;

namespace Pressing.Tests.Unit.Assistant;

/// <summary>
/// Règles extraites de la mission : valeurs null, express, catalogue, ambiguïtés et schéma.
/// Ces tests sont unitaires : aucun moteur IA n'est contacté.
/// </summary>
public class AssistantRulesTests
{
    private static CatalogHints Catalog() => new(
        ["Chemise", "Pantalon", "Costume"],
        ["Lavage", "Lavage + repassage", "Nettoyage à sec"],
        new DateOnly(2026, 11, 27));

    private static JsonObject Item(
        string? category = "Chemise",
        string? service = "Lavage",
        int? quantity = 3,
        bool? express = null) => new()
    {
        ["category"] = category,
        ["service"] = service,
        ["quantity"] = quantity,
        ["express"] = express
    };

    private static JsonObject Base() => new()
    {
        ["intent"] = AssistantIntentions.CreateOrder,
        ["client"] = new JsonObject { ["name"] = null, ["phone"] = null },
        ["items"] = new JsonArray { Item() },
        ["ticket"] = null,
        ["amount"] = null,
        ["payment_method"] = null,
        ["deposit"] = null,
        ["pickup_date"] = null,
        ["item_status"] = null,
        ["confidence"] = 0.92,
        ["transcript"] = "Dépôt pour trois chemises."
    };

    private static string Json(Action<JsonObject>? mutate = null)
    {
        JsonObject root = Base();
        mutate?.Invoke(root);
        return root.ToJsonString();
    }

    private static IntentPayload Payload(Action<JsonObject>? mutate = null) =>
        IntentValidator.Validate(Json(mutate), Catalog()).Payload;

    private static IntentValidationResult Validate(Action<JsonObject>? mutate = null) =>
        IntentValidator.Validate(Json(mutate), Catalog());

    private static void SetItem(JsonObject root, int index, string key, JsonNode? value) =>
        ((JsonObject)((JsonArray)root["items"]!)[index]!)[key] = value;

    private static void SetClient(JsonObject root, string key, JsonNode? value) =>
        ((JsonObject)root["client"]!)[key] = value;

    // A. Gestion du caractère express

    [Fact]
    public void A1_Express_demande_explicitement_est_true()
    {
        IntentPayload payload = Payload(o => SetItem(o, 0, "express", true));

        Assert.True(payload.Items[0].Express);
    }

    [Fact]
    public void A2_Express_refuse_explicitement_est_false()
    {
        IntentPayload payload = Payload(o => SetItem(o, 0, "express", false));

        Assert.False(payload.Items[0].Express);
    }

    [Fact]
    public void A3_Express_non_mentionne_reste_null()
    {
        IntentPayload payload = Payload();

        Assert.Null(payload.Items[0].Express);
    }

    // B. Informations client

    [Fact]
    public void B4_Nom_et_telephone_communiques_sont_conserves()
    {
        IntentPayload payload = Payload(o =>
        {
            SetClient(o, "name", "Astride");
            SetClient(o, "phone", "699112233");
        });

        Assert.Equal("Astride", payload.Client.Name);
        Assert.Equal("699112233", payload.Client.Phone);
    }

    [Fact]
    public void B5_Nom_seul_est_conserve_sans_telephone_invente()
    {
        IntentPayload payload = Payload(o => SetClient(o, "name", "Astride"));

        Assert.Equal("Astride", payload.Client.Name);
        Assert.Null(payload.Client.Phone);
    }

    [Fact]
    public void B6_Aucune_information_personnelle_donnee_reste_null()
    {
        IntentPayload payload = Payload();

        Assert.Null(payload.Client.Name);
        Assert.Null(payload.Client.Phone);
    }

    [Fact]
    public void B7_Le_telephone_absent_n_est_jamais_invente()
    {
        string prompt = AssistantResources.SystemPrompt;

        IntentPayload payload = Payload(o => SetClient(o, "phone", null));

        Assert.Null(payload.Client.Phone);
        Assert.Contains("N'invente jamais de client, de téléphone", prompt, StringComparison.Ordinal);
    }

    // C. Articles et quantités

    [Fact]
    public void C8_Une_seule_categorie_est_conservee()
    {
        IntentPayload payload = Payload();

        Assert.Single(payload.Items);
        Assert.Equal("Chemise", payload.Items[0].Category);
    }

    [Fact]
    public void C9_Plusieurs_categories_sont_conservees()
    {
        IntentPayload payload = Payload(o =>
        {
            var items = (JsonArray)o["items"]!;
            items.Clear();
            items.Add(Item("Chemise", "Lavage", 3));
            items.Add(Item("Pantalon", "Lavage", 1));
        });

        Assert.Equal(2, payload.Items.Count);
        Assert.Equal("Chemise", payload.Items[0].Category);
        Assert.Equal("Pantalon", payload.Items[1].Category);
    }

    [Fact]
    public void C10_Plusieurs_articles_de_la_meme_categorie_sont_conserves()
    {
        IntentPayload payload = Payload(o =>
        {
            var items = (JsonArray)o["items"]!;
            items.Clear();
            items.Add(Item("Chemise", "Lavage", 3));
            items.Add(Item("Chemise", "Repassage", 2));
        });

        Assert.Equal(2, payload.Items.Count);
        Assert.Equal("Chemise", payload.Items[0].Category);
        Assert.Equal("Chemise", payload.Items[1].Category);
        Assert.Equal(2, payload.Items[1].Quantity);
    }

    [Fact]
    public void C11_Quantites_exprimees_en_chiffres_sont_conservees()
    {
        IntentPayload payload = Payload(o =>
        {
            var items = (JsonArray)o["items"]!;
            items.Clear();
            items.Add(Item("Chemise", "Lavage", 3));
            items.Add(Item("Pantalon", "Lavage", 1));
        });

        Assert.Equal(3, payload.Items[0].Quantity);
        Assert.Equal(1, payload.Items[1].Quantity);
    }

    [Fact]
    public void C12_Quantite_en_lettres_sans_chiffre_reste_null_sans_invention()
    {
        IntentPayload payload = Payload(o => SetItem(o, 0, "quantity", null));

        Assert.Null(payload.Items[0].Quantity);
        Assert.Equal("Chemise", payload.Items[0].Category);
    }

    [Fact]
    public void C13_Article_sans_quantite_claire_reste_null()
    {
        IntentPayload payload = Payload(o => SetItem(o, 0, "quantity", null));

        Assert.Null(payload.Items[0].Quantity);
    }

    // D. Informations commerciales

    [Fact]
    public void D14_Prix_non_communique_reste_null()
    {
        Assert.Null(Payload().Amount);
    }

    [Fact]
    public void D15_Acompte_non_communique_reste_null()
    {
        Assert.Null(Payload().Deposit);
    }

    [Fact]
    public void D16_Mode_de_paiement_non_communique_reste_null()
    {
        Assert.Null(Payload().PaymentMethod);
    }

    [Fact]
    public void D17_Date_de_retrait_non_communiquee_reste_null()
    {
        Assert.Null(Payload().PickupDate);
    }

    [Fact]
    public void D18_Ticket_non_communique_reste_null()
    {
        Assert.Null(Payload().Ticket);
    }

    // E. Ambiguïtés et erreurs

    [Fact]
    public async Task E19_Demande_ambigue_avec_faible_confiance_ne_cree_pas_de_commande()
    {
        var llm = new FakeIntentExtractor();
        llm.Enqueue(AssistantTestData.Extraction(
            AssistantTestData.ValidJson(intent: AssistantIntentions.CreateOrder, confidence: 0.55)));
        var pipeline = new LocalAssistantPipeline(new FakeSpeechToText(), llm, new AssistantOptions());

        PipelineResult result = await pipeline.InterpretAsync("Je ne sais pas trop.", AssistantTestData.Catalog());

        Assert.Equal(PipelineStatus.NotUnderstood, result.Status);
        Assert.Equal(AssistantMessages.NotUnderstood, result.Message);
        Assert.Equal("Je ne sais pas trop.", result.Transcript);
    }

    [Fact]
    public void E20_Acompte_superieur_au_prix_est_rejete()
    {
        string json = Json(o =>
        {
            o["amount"] = 5000;
            o["deposit"] = 9000;
        });

        Assert.Throws<AssistantContractException>(() => IntentValidator.Validate(json, Catalog()));
    }

    [Fact]
    public void E21_Categorie_inexistante_est_signalee()
    {
        IntentValidationResult result = Validate(o => SetItem(o, 0, "category", "Dragons"));

        Assert.Contains(result.Warnings, w => w.Contains("Dragons", StringComparison.Ordinal));
    }

    [Fact]
    public void E22_Service_inexistant_est_signale()
    {
        IntentValidationResult result = Validate(o => SetItem(o, 0, "service", "Polissage"));

        Assert.Contains(result.Warnings, w => w.Contains("Polissage", StringComparison.Ordinal));
    }

    [Fact]
    public void E23_Json_invalide_est_rejete()
    {
        Assert.Throws<AssistantContractException>(() => IntentValidator.Validate("{ ceci n'est pas du JSON", Catalog()));
    }

    [Fact]
    public void E24a_Champ_obligatoire_manquant_est_rejete()
    {
        string json = Json(o => o.Remove("confidence"));

        Assert.Throws<AssistantContractException>(() => IntentValidator.Validate(json, Catalog()));
    }

    [Fact]
    public void E24b_Valeur_incompatible_avec_le_schema_est_rejetee()
    {
        string quantityZero = Json(o => SetItem(o, 0, "quantity", 0));
        string expressInvalide = Json(o => SetItem(o, 0, "express", "oui"));

        Assert.Throws<AssistantContractException>(() => IntentValidator.Validate(quantityZero, Catalog()));
        Assert.Throws<AssistantContractException>(() => IntentValidator.Validate(expressInvalide, Catalog()));
    }

    [Fact]
    public void E24c_Champ_hors_contrat_est_rejete()
    {
        string rootExtra = Json(o => o["champ_supplementaire"] = 1);
        string clientExtra = Json(o => ((JsonObject)o["client"]!)["extra"] = 1);
        string itemExtra = Json(o => ((JsonObject)((JsonArray)o["items"]!)[0]!)["extra"] = 1);

        Assert.Throws<AssistantContractException>(() => IntentValidator.Validate(rootExtra, Catalog()));
        Assert.Throws<AssistantContractException>(() => IntentValidator.Validate(clientExtra, Catalog()));
        Assert.Throws<AssistantContractException>(() => IntentValidator.Validate(itemExtra, Catalog()));
    }

    // Cohérence du prompt et du catalogue dynamique

    [Fact]
    public void Le_prompt_impose_la_regle_express_sans_valeur_par_defaut()
    {
        string prompt = AssistantResources.SystemPrompt;

        Assert.Contains("{{CATALOGUE}}", prompt, StringComparison.Ordinal);
        Assert.Contains("{{DATE_REFERENCE}}", prompt, StringComparison.Ordinal);
        Assert.Contains("Ne mets jamais false par défaut", prompt, StringComparison.Ordinal);
        Assert.Contains("pas express", prompt, StringComparison.Ordinal);
        Assert.Contains("\"quantity\":3,\"express\":null", prompt, StringComparison.Ordinal);
        Assert.Contains("\"quantity\":1,\"express\":true", prompt, StringComparison.Ordinal);
        Assert.Contains("\"quantity\":1,\"express\":null", prompt, StringComparison.Ordinal);
        Assert.Contains("ne change ni les accents", prompt, StringComparison.Ordinal);
        Assert.Contains("La chemise est prête.", prompt, StringComparison.Ordinal);
        Assert.Contains("Où en est le dossier de Marie ?", prompt, StringComparison.Ordinal);
        Assert.Contains("Les synonymes ambigus", prompt, StringComparison.Ordinal);
        Assert.Contains("marqueurs d'hésitation", prompt, StringComparison.Ordinal);
        Assert.Contains("Quand tu choisis unknown, renvoie items vide", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Le_vocabulaire_stt_embarque_contient_un_contexte_pas_seulement_une_liste()
    {
        string vocab = AssistantResources.DefaultVocabulary;

        Assert.Contains("Faire laver trois chemises", vocab, StringComparison.Ordinal);
        Assert.Contains("Je depose un manteau", vocab, StringComparison.Ordinal);
        Assert.Contains("Acompte de cinq mille en especes", vocab, StringComparison.Ordinal);
        Assert.Contains("Lavage + repassage", vocab, StringComparison.Ordinal);
    }

    [Fact]
    public void Le_catalogue_est_injecte_dynamiquement_avec_les_libelles_exacts()
    {
        string prompt = AssistantPrompt.BuildSystemPrompt(Catalog());

        Assert.DoesNotContain("{{CATALOGUE}}", prompt, StringComparison.Ordinal);
        Assert.Contains("2026-11-27", prompt, StringComparison.Ordinal);
        Assert.Contains("Catégories : Chemise, Pantalon, Costume", prompt, StringComparison.Ordinal);
        Assert.Contains("Services : Lavage, Lavage + repassage, Nettoyage à sec", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Costume 4 pièces", prompt, StringComparison.Ordinal);
    }
}
