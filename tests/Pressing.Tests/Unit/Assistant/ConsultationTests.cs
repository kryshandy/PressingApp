using System.Text.Json.Nodes;
using Pressing.Core.Assistant;

namespace Pressing.Tests.Unit.Assistant;

/// <summary>T1.6 : consultation en lecture seule, sans base de données ni modification.</summary>
public class ConsultationTests
{
    [Fact]
    public async Task Une_consultation_avec_ticket_est_lisible_sans_modifier_de_donnees()
    {
        const string transcript = "Où en est le ticket 142 ?";
        IntentExtraction extraction = Extraction(
            intent: AssistantIntentions.FindOrder,
            ticket: "142",
            transcript: transcript,
            clientName: null);
        var fake = new FakeIntentExtractor();
        fake.Enqueue(extraction);
        var pipeline = new LocalAssistantPipeline(new FakeSpeechToText(), fake, new AssistantOptions());

        PipelineResult result = await pipeline.InterpretAsync(transcript, AssistantTestData.Catalog());

        Assert.Equal(PipelineStatus.Ok, result.Status);
        Assert.Equal(AssistantIntentions.FindOrder, result.Extraction!.Payload.Intent);
        Assert.Equal("142", result.Extraction.Payload.Ticket);
        Assert.Null(result.Extraction.Payload.Client.Name);
        Assert.Empty(result.Extraction.Payload.Items);

        string reply = ConsultationReply.Format(result.Extraction.Payload);
        Assert.Contains("ticket 142", reply, StringComparison.Ordinal);
        Assert.Contains("Aucune donnée métier consultée", reply, StringComparison.Ordinal);
        Assert.Contains("confirmer par M2", reply, StringComparison.Ordinal);
    }

    [Fact]
    public void Une_consultation_sans_ticket_ne_sinvente_pas_de_client()
    {
        IntentPayload payload = Extraction(
            intent: AssistantIntentions.FindOrder,
            ticket: null,
            transcript: "Qu'est-ce que Mme Ngo doit ?",
            clientName: null).Payload;

        Assert.Null(payload.Client.Name);
        Assert.Null(payload.Ticket);
        Assert.Empty(payload.Items);
        Assert.Contains("aucun ticket identifié", ConsultationReply.Format(payload), StringComparison.Ordinal);
    }

    [Fact]
    public void Une_creations_de_commande_n_est_pas_une_consultation()
    {
        IntentPayload payload = AssistantTestData.Extraction().Payload;

        Assert.Equal(
            "Cette phrase ne demande pas une consultation.",
            ConsultationReply.Format(payload));
    }

    private static IntentExtraction Extraction(
        string intent,
        string? ticket,
        string transcript,
        string? clientName)
    {
        var root = new JsonObject
        {
            ["intent"] = intent,
            ["client"] = new JsonObject
            {
                ["name"] = clientName,
                ["phone"] = null
            },
            ["items"] = new JsonArray(),
            ["ticket"] = ticket,
            ["amount"] = null,
            ["payment_method"] = null,
            ["deposit"] = null,
            ["pickup_date"] = null,
            ["item_status"] = null,
            ["confidence"] = 0.9,
            ["transcript"] = transcript
        };
        IntentValidationResult validation = IntentValidator.Validate(
            root.ToJsonString(), AssistantTestData.Catalog());
        return new IntentExtraction(root.ToJsonString(), validation.Payload, validation.Warnings);
    }
}
