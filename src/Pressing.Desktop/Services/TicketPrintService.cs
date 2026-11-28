using Pressing.Client;
using Pressing.Core.Dtos;
using Pressing.Core.Printing;
using Pressing.Desktop.Ui;

namespace Pressing.Desktop.Services;

/// <summary>Résultat d'une demande d'impression thermique.</summary>
public enum TicketPrintOutcome
{
    /// <summary>Octets envoyés à l'imprimante.</summary>
    Printed,

    /// <summary>Aucune imprimante configurée : l'appelant explique comment la configurer.</summary>
    NotConfigured,

    /// <summary>Mode autotest (sans interface réelle) : le ticket est généré mais jamais imprimé.</summary>
    Skipped
}

/// <summary>Impression du ticket 80 mm : réglages et octets viennent de l'API, l'envoi est fait par <see cref="RawPrinter"/>.</summary>
public static class TicketPrintService
{
    public const string HowToConfigure =
        "Aucune imprimante de tickets n'est configurée.\n\n"
        + "Un responsable ouvre Paramètres › Reçus › « Imprimante ticket 80 mm », saisit le nom exact de l'imprimante "
        + "tel qu'il apparaît dans Windows (Paramètres › Imprimantes et scanners), puis enregistre. "
        + "Laissé vide, le ticket 80 mm reste désactivé ; le reçu PDF reste disponible.";

    /// <summary>Récupère le ticket ESC/POS de la commande et l'envoie à l'imprimante configurée.</summary>
    public static async Task<(TicketPrintOutcome Outcome, string PrinterName)> PrintAsync(
        PressingApiClient api, int orderId, string ticketNumber)
    {
        TicketPrinterSettingsDto? settings = await api.GetTicketPrinterAsync();
        if (settings is null || !settings.Enabled)
        {
            return (TicketPrintOutcome.NotConfigured, string.Empty);
        }

        byte[] data = await api.GetThermalTicketAsync(orderId);
        if (AppDialogs.Headless)
        {
            return (TicketPrintOutcome.Skipped, settings.PrinterName);
        }

        await RawPrinter.SendAsync(settings.PrinterName, data, "Ticket " + ticketNumber);
        return (TicketPrintOutcome.Printed, settings.PrinterName);
    }

    /// <summary>Petit ticket de test (accents, colonnes, QR, coupe) pour vérifier le nom de l'imprimante et l'encodage.</summary>
    public static async Task<TicketPrintOutcome> PrintTestAsync(string printerName, bool openDrawer)
    {
        if (string.IsNullOrWhiteSpace(printerName))
        {
            return TicketPrintOutcome.NotConfigured;
        }

        EscPosWriter w = new();
        w.Initialize();
        w.Align(EscPosAlign.Center);
        w.Bold(true);
        w.Line("Pressing Éclat");
        w.Bold(false);
        w.Line("Test d'impression ticket 80 mm");
        w.Separator('-');
        w.Align(EscPosAlign.Left);
        w.Line("Accents : é è à ù ç ê î ô û ï ë € œ");
        w.Columns("Libellé à gauche", EscPosReceiptBuilder.Money(12500m, "FCFA"));
        w.Separator('-');
        w.Align(EscPosAlign.Center);
        w.QrCode("PRESS-TEST");
        w.Line("Si ce ticket est lisible, tout est prêt.");
        w.Feed(3);
        if (openDrawer)
        {
            w.Raw(EscPosReceiptBuilder.DrawerPulse());
        }

        w.Cut();

        if (AppDialogs.Headless)
        {
            return TicketPrintOutcome.Skipped;
        }

        await RawPrinter.SendAsync(printerName, w.ToArray(), "Test ticket 80 mm");
        return TicketPrintOutcome.Printed;
    }
}
