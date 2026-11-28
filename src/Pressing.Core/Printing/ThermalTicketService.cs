using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Core.Options;
using Pressing.Core.Services;

namespace Pressing.Core.Printing;

/// <summary>Ticket thermique 80 mm : réglages de l'imprimante (lisibles par tout le personnel) et octets ESC/POS d'une commande.</summary>
public class ThermalTicketService
{
    private readonly SettingsService _settings;

    public ThermalTicketService(SettingsService settings)
    {
        _settings = settings;
    }

    public async Task<TicketPrinterSettingsDto> GetPrinterSettingsAsync(CancellationToken ct = default)
    {
        string name = (await _settings.GetAsync(SettingsKeys.TicketPrinterName, string.Empty, ct)).Trim();
        bool drawer = await _settings.GetBoolAsync(SettingsKeys.TicketPrinterOpenDrawer, false, ct);
        return new TicketPrinterSettingsDto(name, name.Length > 0, drawer);
    }

    /// <summary>Octets ESC/POS du ticket ; l'ouverture du tiroir suit le réglage de la boutique.</summary>
    public async Task<byte[]> BuildAsync(Order order, CancellationToken ct = default)
    {
        ShopInfo shop = await _settings.GetShopInfoAsync(ct);
        TicketPrinterSettingsDto printer = await GetPrinterSettingsAsync(ct);
        bool showQr = await _settings.GetBoolAsync(SettingsKeys.ReceiptShowQr, true, ct);
        return EscPosReceiptBuilder.Build(order, shop, new EscPosOptions
        {
            OpenCashDrawer = printer.OpenCashDrawer,
            PrintQr = showQr
        });
    }
}
