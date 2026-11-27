using System.Text.Json.Serialization;

namespace Pressing.Core.Dtos;

// ============================================================================================
// Contrat de l'assistant vocal (T2.1) — partagé entre le membre 1 (moteurs IA) et le membre 2
// (intégration). Référence : section 4.1 du brief « état vérifié et feuille de route v3 »
// et section « Assistant vocal » de docs/API.md.
//
// Deux familles de DTO, volontairement distinctes :
//   1. AssistantIntentDto  : ce que le modèle de langage PRODUIT (JSON en snake_case, valeurs de
//                            texte libres imposées par la grammaire). Aucun identifiant de base.
//   2. OrderDraftDto       : ce que le backend RENVOIE au Desktop après validation par les règles
//                            métier (JSON camelCase habituel de l'API, identifiants résolus,
//                            prix calculés, avertissements). L'IA ne touche jamais la base.
// ============================================================================================

/// <summary>Valeurs autorisées de <see cref="AssistantIntentDto.Intent"/>.</summary>
public static class AssistantIntents
{
    public const string CreateOrder = "create_order";
    public const string RegisterPickup = "register_pickup";
    public const string AddPayment = "add_payment";
    public const string UpdateItemStatus = "update_item_status";
    public const string FindOrder = "find_order";
    public const string Unknown = "unknown";

    public static readonly IReadOnlyList<string> All =
        [CreateOrder, RegisterPickup, AddPayment, UpdateItemStatus, FindOrder, Unknown];

    public static bool IsKnown(string? value) => value is not null && All.Contains(value);
}

/// <summary>Valeurs autorisées de <see cref="AssistantIntentDto.PaymentMethod"/>.</summary>
public static class AssistantPaymentMethods
{
    public const string Cash = "cash";
    public const string MobileMoney = "mobile_money";
    public const string Card = "card";
    public const string Transfer = "transfer";

    public static readonly IReadOnlyList<string> All = [Cash, MobileMoney, Card, Transfer];

    /// <summary>Correspondance vers l'énumération métier. <c>null</c> si la valeur est absente ou inconnue.</summary>
    public static PaymentMethod? ToPaymentMethod(string? value) => value switch
    {
        Cash => PaymentMethod.Cash,
        MobileMoney => PaymentMethod.MobileMoney,
        Card => PaymentMethod.Card,
        Transfer => PaymentMethod.BankTransfer,
        _ => null
    };
}

/// <summary>Valeurs autorisées de <see cref="AssistantIntentDto.ItemStatus"/>.</summary>
public static class AssistantItemStatuses
{
    public const string InProgress = "in_progress";
    public const string Ready = "ready";

    public static readonly IReadOnlyList<string> All = [InProgress, Ready];

    /// <summary>Correspondance vers l'énumération métier. <c>null</c> si la valeur est absente ou inconnue.</summary>
    public static ItemStatus? ToItemStatus(string? value) => value switch
    {
        InProgress => ItemStatus.InProgress,
        Ready => ItemStatus.Ready,
        _ => null
    };
}

/// <summary>Constantes de l'assistant.</summary>
public static class AssistantContract
{
    /// <summary>
    /// Seuil de confiance par défaut : en dessous, ou si l'intention est <c>unknown</c>, le Desktop affiche
    /// « je n'ai pas compris » et la transcription. Réglable dans les paramètres (T2.6).
    /// </summary>
    public const double DefaultConfidenceThreshold = 0.6;

    /// <summary>Taille maximale d'un enregistrement WAV accepté par <c>POST /api/assistant/transcribe</c> (2 Mo).</summary>
    public const int MaxAudioBytes = 2 * 1024 * 1024;

    /// <summary>Longueur maximale du texte accepté par <c>POST /api/assistant/interpret</c>.</summary>
    public const int MaxTextLength = 1000;
}

// ---------------------------------------------------------------------------------------------
// 1. Sortie du modèle de langage (contrat M1 → M2). JSON en snake_case, champ inconnu = null.
// ---------------------------------------------------------------------------------------------

/// <summary>Client cité à l'oral. Un champ non entendu vaut <c>null</c> et n'est jamais inventé.</summary>
public record AssistantClientDto(
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("phone")] string? Phone = null);

/// <summary>
/// Article cité à l'oral. <see cref="Category"/> et <see cref="Service"/> sont du texte : leur rapprochement avec le
/// catalogue (synonymes, fautes) est fait par le backend, jamais par le modèle.
/// </summary>
public record AssistantItemDto(
    [property: JsonPropertyName("category")] string? Category = null,
    [property: JsonPropertyName("service")] string? Service = null,
    [property: JsonPropertyName("quantity")] int Quantity = 1,
    [property: JsonPropertyName("express")] bool Express = false);

/// <summary>
/// Intention structurée extraite d'une phrase par le modèle de langage (sortie imposée par grammaire JSON).
/// Exemple de référence : voir tests/Pressing.Tests/Unit/AssistantContractTests.cs.
/// </summary>
public record AssistantIntentDto(
    // <summary>Une valeur de <see cref="AssistantIntents"/>.</summary>
    [property: JsonPropertyName("intent")] string Intent = AssistantIntents.Unknown,
    [property: JsonPropertyName("client")] AssistantClientDto? Client = null,
    [property: JsonPropertyName("items")] IReadOnlyList<AssistantItemDto>? Items = null,
    // <summary>Ticket cité, par exemple « PRESS-2026-00142 ». Le backend complète un numéro court (« 142 »).</summary>
    [property: JsonPropertyName("ticket")] string? Ticket = null,
    // <summary>Montant d'un paiement, en FCFA.</summary>
    [property: JsonPropertyName("amount")] decimal? Amount = null,
    // <summary>Une valeur de <see cref="AssistantPaymentMethods"/>.</summary>
    [property: JsonPropertyName("payment_method")] string? PaymentMethod = null,
    // <summary>Acompte versé au dépôt, en FCFA.</summary>
    [property: JsonPropertyName("deposit")] decimal? Deposit = null,
    // <summary>Date de retrait demandée (jour calendaire, déjà résolue : « mardi » → date).</summary>
    [property: JsonPropertyName("pickup_date")] DateOnly? PickupDate = null,
    // <summary>Une valeur de <see cref="AssistantItemStatuses"/>.</summary>
    [property: JsonPropertyName("item_status")] string? ItemStatus = null,
    // <summary>De 0,0 à 1,0.</summary>
    [property: JsonPropertyName("confidence")] double Confidence = 0,
    // <summary>Texte reconnu par la reconnaissance vocale.</summary>
    [property: JsonPropertyName("transcript")] string Transcript = "");

// ---------------------------------------------------------------------------------------------
// 2. Brouillon validé (contrat M2 → Desktop). JSON camelCase habituel de l'API.
// ---------------------------------------------------------------------------------------------

/// <summary>Écran que le Desktop doit ouvrir, pré-rempli, pour confirmation par le caissier.</summary>
public enum AssistantTarget
{
    /// <summary>« Je n'ai pas compris » : afficher uniquement la transcription.</summary>
    NotUnderstood = 0,
    /// <summary>Écran Nouveau dépôt (<c>NewOrderView</c>).</summary>
    NewOrder = 1,
    /// <summary>Écran Retraits.</summary>
    Pickup = 2,
    /// <summary>Écran Paiement.</summary>
    Payment = 3,
    /// <summary>Changement de statut d'articles.</summary>
    UpdateStatus = 4,
    /// <summary>Recherche : ouvrir le détail de la commande ou la liste filtrée.</summary>
    FindOrder = 5
}

/// <summary>Client retrouvé (ou non) dans la base.</summary>
/// <param name="ClientId">Identifiant du client retrouvé ; <c>null</c> si aucun client ne correspond.</param>
/// <param name="IsNew">Vrai si le client est inconnu : le Desktop propose la création (avertissement orange).</param>
/// <param name="Candidates">Homonymes à départager par le caissier lorsque le nom est ambigu.</param>
public record AssistantClientMatchDto(
    int? ClientId,
    string? FullName,
    string? Phone,
    bool IsNew,
    IReadOnlyList<AssistantClientCandidateDto> Candidates);

public record AssistantClientCandidateDto(int ClientId, string FullName, string? Phone);

/// <summary>Ligne de brouillon : article rapproché du catalogue et prix calculé par le catalogue existant.</summary>
/// <param name="CategoryId">Catégorie du catalogue ; <c>null</c> si introuvable (le backend ne devine pas).</param>
/// <param name="ServiceType">Service du catalogue ; <c>null</c> si introuvable.</param>
/// <param name="HeardAs">Ce qui a été entendu (« costard »), pour l'affichage à côté de la valeur retenue.</param>
/// <param name="UnitPrice">Prix unitaire issu du catalogue ; <c>null</c> si l'article est incomplet.</param>
/// <param name="IsUncertain">Vrai : le Desktop surligne la ligne en orange.</param>
public record OrderDraftItemDto(
    int? CategoryId,
    string? CategoryName,
    ServiceType? ServiceType,
    string? ServiceLabel,
    int Quantity,
    bool IsExpress,
    decimal? UnitPrice,
    decimal? LineTotal,
    string? HeardAs,
    bool IsUncertain);

/// <summary>
/// Brouillon renvoyé au Desktop par <c>POST /api/assistant/interpret</c>. Il n'est jamais enregistré : le caissier
/// le vérifie, le corrige, puis le valide par les routes existantes (<c>POST /api/orders</c>, retraits, paiements).
/// </summary>
/// <param name="Intent">Une valeur de <see cref="AssistantIntents"/>.</param>
/// <param name="Target">Écran à ouvrir.</param>
/// <param name="Client">Client retrouvé ; <c>null</c> si la phrase n'en cite aucun.</param>
/// <param name="Items">Articles du dépôt (intention <c>create_order</c>).</param>
/// <param name="OrderId">Commande retrouvée par son ticket (retrait, paiement, statut, recherche).</param>
/// <param name="Ticket">Ticket complet de la commande retrouvée ; <c>null</c> si inexistant.</param>
/// <param name="Balance">Solde restant de la commande retrouvée (contrôle du montant d'un paiement).</param>
/// <param name="PaymentAmount">Montant du paiement dicté.</param>
/// <param name="PaymentMethod">Mode de paiement dicté (ou du dépôt).</param>
/// <param name="DepositAmount">Acompte dicté au dépôt.</param>
/// <param name="DueDate">Date de retrait demandée.</param>
/// <param name="ItemStatus">Statut demandé pour les articles (<c>InProgress</c> ou <c>Ready</c>).</param>
/// <param name="Quote">Devis calculé par <c>CatalogService</c> pour les articles reconnus ; <c>null</c> sinon.</param>
/// <param name="UncertainFields">
/// Champs à surligner en orange, noms stables : <c>client</c>, <c>items[0].category</c>, <c>items[0].service</c>,
/// <c>ticket</c>, <c>amount</c>, <c>paymentMethod</c>, <c>deposit</c>, <c>dueDate</c>, <c>itemStatus</c>.
/// </param>
/// <param name="Warnings">Avertissements lisibles en français (« Client inconnu : création proposée »).</param>
/// <param name="Confidence">Confiance du modèle, de 0,0 à 1,0.</param>
/// <param name="Transcript">Texte reconnu, affiché à l'écran.</param>
public record OrderDraftDto(
    string Intent,
    AssistantTarget Target,
    AssistantClientMatchDto? Client,
    IReadOnlyList<OrderDraftItemDto> Items,
    int? OrderId,
    string? Ticket,
    decimal? Balance,
    decimal? PaymentAmount,
    PaymentMethod? PaymentMethod,
    decimal? DepositAmount,
    DateTime? DueDate,
    ItemStatus? ItemStatus,
    OrderQuoteDto? Quote,
    IReadOnlyList<string> UncertainFields,
    IReadOnlyList<string> Warnings,
    double Confidence,
    string Transcript);

// ---------------------------------------------------------------------------------------------
// 3. Corps et réponses des routes /api/assistant/*
// ---------------------------------------------------------------------------------------------

/// <summary>Corps de <c>POST /api/assistant/interpret</c> (texte vers brouillon).</summary>
public record AssistantInterpretRequest(string Text);

/// <summary>Réponse de <c>POST /api/assistant/transcribe</c> (WAV vers texte). L'audio n'est jamais stocké.</summary>
public record AssistantTranscriptionDto(string Text, int DurationMs);

/// <summary>Réponse de <c>GET /api/assistant/status</c>.</summary>
/// <param name="Enabled">Interrupteur <c>ASSISTANT_ENABLED</c> / paramètre Admin.</param>
/// <param name="SpeechToTextReady">Le moteur de reconnaissance vocale répond.</param>
/// <param name="LanguageModelReady">Le modèle de langage répond.</param>
/// <param name="Message">Message lisible, par exemple « L'assistant vocal n'est pas démarré ».</param>
public record AssistantStatusDto(
    bool Enabled,
    bool SpeechToTextReady,
    bool LanguageModelReady,
    double ConfidenceThreshold,
    string? Message);
