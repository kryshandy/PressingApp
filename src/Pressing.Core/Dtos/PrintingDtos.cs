namespace Pressing.Core.Dtos;

/// <summary>Réglages d'impression du ticket thermique 80 mm (lisibles par tout le personnel, pas seulement les managers).</summary>
/// <param name="PrinterName">Nom Windows de l'imprimante ; vide si l'impression thermique n'est pas configurée.</param>
/// <param name="Enabled">Vrai si un nom d'imprimante est renseigné.</param>
/// <param name="OpenCashDrawer">Ouvre le tiroir-caisse (ESC p) avec le ticket.</param>
public record TicketPrinterSettingsDto(string PrinterName, bool Enabled, bool OpenCashDrawer);
