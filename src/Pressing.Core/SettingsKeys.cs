namespace Pressing.Core;

public static class SettingsKeys
{
    public const string ShopName = "Shop.Name";
    public const string ShopLegalName = "Shop.LegalName";
    public const string ShopAddress = "Shop.Address";
    public const string ShopPhone = "Shop.Phone";
    public const string ShopWhatsApp = "Shop.WhatsApp";
    public const string ShopEmail = "Shop.Email";
    public const string ShopCity = "Shop.City";
    public const string ShopSlogan = "Shop.Slogan";
    public const string Currency = "Shop.Currency";
    public const string ReceiptFooter = "Shop.ReceiptFooter";
    public const string OpeningHours = "Shop.OpeningHours";

    public const string DefaultProcessingDays = "Orders.DefaultProcessingDays";
    public const string ExpressProcessingDays = "Orders.ExpressProcessingDays";
    public const string AllowPickupWithBalance = "Orders.AllowPickupWithBalance";
    public const string MaxDiscountPercent = "Orders.MaxDiscountPercent";

    public const string LoyaltyPointsPerAmount = "Loyalty.PointsPerAmount";
    public const string LoyaltyPointValue = "Loyalty.PointValue";
    public const string LoyaltyEnabled = "Loyalty.Enabled";

    public const string EmailNotifications = "Email.Notifications";
    public const string EmailOrderConfirmation = "Email.OrderConfirmation";
    public const string EmailOrderReady = "Email.OrderReady";
    public const string EmailReceipt = "Email.Receipt";

    public const string ReceiptShowQr = "Receipt.ShowQr";
    public const string ReceiptShowBarcode = "Receipt.ShowBarcode";
    public const string ReceiptPaperSize = "Receipt.PaperSize";

    /// <summary>Nom Windows de l'imprimante de tickets 80 mm (ESC/POS) ; vide = impression thermique désactivée.</summary>
    public const string TicketPrinterName = "Printer.TicketName";

    /// <summary>Ouvre le tiroir-caisse (ESC p) à l'impression du ticket thermique.</summary>
    public const string TicketPrinterOpenDrawer = "Printer.OpenCashDrawer";

    /// <summary>Double authentification (TOTP) exigée des comptes Manager (Administrateur uniquement pour la modifier). Activée par défaut.</summary>
    public const string RequireManagerTwoFactor = "Security.RequireManagerTwoFactor";

    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        [ShopName] = "Pressing Éclat",
        [ShopLegalName] = "Pressing Éclat SARL",
        [ShopAddress] = "12, avenue de la Paix",
        [ShopPhone] = "+225 07 00 00 00 00",
        [ShopWhatsApp] = "+225 07 00 00 00 00",
        [ShopEmail] = "contact@pressing-eclat.local",
        [ShopCity] = "Abidjan",
        [ShopSlogan] = "Le soin du détail, l'élégance au quotidien",
        [Currency] = "FCFA",
        [ReceiptFooter] = "Merci de votre confiance. Présentez ce ticket pour retirer vos articles.",
        [OpeningHours] = "Lun–Sam : 7h30 – 19h30",
        [DefaultProcessingDays] = "3",
        [ExpressProcessingDays] = "1",
        [AllowPickupWithBalance] = "false",
        [MaxDiscountPercent] = "30",
        [LoyaltyPointsPerAmount] = "1000",
        [LoyaltyPointValue] = "10",
        [LoyaltyEnabled] = "true",
        [EmailNotifications] = "true",
        [EmailOrderConfirmation] = "true",
        [EmailOrderReady] = "true",
        [EmailReceipt] = "true",
        [ReceiptShowQr] = "true",
        [ReceiptShowBarcode] = "true",
        [ReceiptPaperSize] = "A5",
        [TicketPrinterName] = string.Empty,
        [TicketPrinterOpenDrawer] = "false",
        [RequireManagerTwoFactor] = "true"
    };
}
