using System.Text.Json.Serialization;
using Pressing.Core.Assistant;

namespace Pressing.Core.Dtos;

/// <summary>
/// DTO officiel M2 du brouillon produit par l'assistant local (contrat T2.1).
/// DTO à utiliser par les futures routes <c>/api/assistant/*</c> du Membre 2 : aucune duplication côté M1.
/// </summary>
public sealed record AssistantIntentDto
{
    [JsonPropertyName("intent")]
    public string Intent { get; init; } = "unknown";

    [JsonPropertyName("client")]
    public AssistantClientDto Client { get; init; } = new(null, null);

    [JsonPropertyName("items")]
    public IReadOnlyList<AssistantItemDto> Items { get; init; } = [];

    [JsonPropertyName("ticket")]
    public string? Ticket { get; init; }

    [JsonPropertyName("amount")]
    public decimal? Amount { get; init; }

    [JsonPropertyName("payment_method")]
    public string? PaymentMethod { get; init; }

    [JsonPropertyName("deposit")]
    public decimal? Deposit { get; init; }

    [JsonPropertyName("pickup_date")]
    public string? PickupDate { get; init; }

    [JsonPropertyName("item_status")]
    public string? ItemStatus { get; init; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    [JsonPropertyName("transcript")]
    public string Transcript { get; init; } = "";
}

public sealed record AssistantClientDto(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("phone")] string? Phone);

public sealed record AssistantItemDto(
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("service")] string? Service,
    [property: JsonPropertyName("quantity")] int? Quantity,
    [property: JsonPropertyName("express")] bool? Express);

/// <summary>Indice de catalogue exigé par le contrat M2.</summary>
public sealed record AssistantCatalogHints(
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Services);

/// <summary>Conversion explicite vers le type riche utilisé par les garde-fous M1.</summary>
public static class AssistantDtoMapping
{
    public static AssistantIntentDto ToDto(this IntentPayload payload) => new()
    {
        Intent = payload.Intent,
        Client = new AssistantClientDto(payload.Client.Name, payload.Client.Phone),
        Items = payload.Items.Select(item =>
            new AssistantItemDto(item.Category, item.Service, item.Quantity, item.Express)).ToArray(),
        Ticket = payload.Ticket,
        Amount = payload.Amount,
        PaymentMethod = payload.PaymentMethod,
        Deposit = payload.Deposit,
        PickupDate = payload.PickupDate,
        ItemStatus = payload.ItemStatus,
        Confidence = payload.Confidence,
        Transcript = payload.Transcript
    };

    public static IntentPayload FromDto(this AssistantIntentDto dto) => new(
        dto.Intent,
        new IntentClient(dto.Client.Name, dto.Client.Phone),
        dto.Items.Select(item =>
            new IntentItem(item.Category, item.Service, item.Quantity, item.Express)).ToArray(),
        dto.Ticket,
        dto.Amount,
        dto.PaymentMethod,
        dto.Deposit,
        dto.PickupDate,
        dto.ItemStatus,
        dto.Confidence,
        dto.Transcript);
}
