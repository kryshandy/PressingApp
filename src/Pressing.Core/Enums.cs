namespace Pressing.Core;

public enum UserRole
{
    Admin = 0,
    Manager = 1,
    Cashier = 2
}

public enum ServiceType
{
    Washing = 0,
    WashAndIron = 1,
    Ironing = 2,
    DryCleaning = 3,
    StainRemoval = 4
}

public enum OrderStatus
{
    Deposited = 0,
    InProgress = 1,
    Ready = 2,
    PartiallyPickedUp = 3,
    Completed = 4,
    Cancelled = 5
}

public enum ItemStatus
{
    Deposited = 0,
    InProgress = 1,
    Ready = 2,
    PickedUp = 3,
    Cancelled = 4
}

public enum PaymentMethod
{
    Cash = 0,
    MobileMoney = 1,
    Card = 2,
    BankTransfer = 3,
    Other = 4
}

public enum PaymentType
{
    Payment = 0,
    Refund = 1
}

public enum DeliveryType
{
    StorePickup = 0,
    HomeDelivery = 1
}

public enum EmailStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
    Simulated = 3,
    Cancelled = 4
}

public enum PromotionType
{
    Percentage = 0,
    FixedAmount = 1
}

public enum LoyaltyTransactionType
{
    Earned = 0,
    Redeemed = 1,
    Adjusted = 2
}

public enum NotificationKind
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Danger = 3
}

public static class EnumLabels
{
    public static string Label(this UserRole value) => value switch
    {
        UserRole.Admin => "Administrateur",
        UserRole.Manager => "Manager",
        UserRole.Cashier => "Caissier",
        _ => value.ToString()
    };

    public static string Label(this ServiceType value) => value switch
    {
        ServiceType.Washing => "Lavage",
        ServiceType.WashAndIron => "Lavage + repassage",
        ServiceType.Ironing => "Repassage",
        ServiceType.DryCleaning => "Nettoyage à sec",
        ServiceType.StainRemoval => "Détachage",
        _ => value.ToString()
    };

    public static string Label(this OrderStatus value) => value switch
    {
        OrderStatus.Deposited => "Déposé",
        OrderStatus.InProgress => "En traitement",
        OrderStatus.Ready => "Prêt",
        OrderStatus.PartiallyPickedUp => "Partiellement retiré",
        OrderStatus.Completed => "Terminé",
        OrderStatus.Cancelled => "Annulé",
        _ => value.ToString()
    };

    public static string Label(this ItemStatus value) => value switch
    {
        ItemStatus.Deposited => "Déposé",
        ItemStatus.InProgress => "En traitement",
        ItemStatus.Ready => "Prêt",
        ItemStatus.PickedUp => "Retiré",
        ItemStatus.Cancelled => "Annulé",
        _ => value.ToString()
    };

    public static string Label(this PaymentMethod value) => value switch
    {
        PaymentMethod.Cash => "Espèces",
        PaymentMethod.MobileMoney => "Mobile Money",
        PaymentMethod.Card => "Carte bancaire",
        PaymentMethod.BankTransfer => "Virement",
        PaymentMethod.Other => "Autre",
        _ => value.ToString()
    };

    public static string Label(this DeliveryType value) => value switch
    {
        DeliveryType.StorePickup => "Retrait en boutique",
        DeliveryType.HomeDelivery => "Livraison à domicile",
        _ => value.ToString()
    };

    public static string Label(this EmailStatus value) => value switch
    {
        EmailStatus.Pending => "En attente",
        EmailStatus.Sent => "Envoyé",
        EmailStatus.Failed => "Échec",
        EmailStatus.Simulated => "Simulé",
        _ => value.ToString()
    };

    public static string Label(this PromotionType value) => value switch
    {
        PromotionType.Percentage => "Pourcentage",
        PromotionType.FixedAmount => "Montant fixe",
        _ => value.ToString()
    };

    public static string Label(this PaymentType value) => value switch
    {
        PaymentType.Payment => "Encaissement",
        PaymentType.Refund => "Remboursement",
        _ => value.ToString()
    };

    public static string Label(this LoyaltyTransactionType value) => value switch
    {
        LoyaltyTransactionType.Earned => "Points gagnés",
        LoyaltyTransactionType.Redeemed => "Points utilisés",
        LoyaltyTransactionType.Adjusted => "Ajustement",
        _ => value.ToString()
    };
}
