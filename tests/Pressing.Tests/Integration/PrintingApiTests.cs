using System.Net;
using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Core.Printing;
using Pressing.Tests.Support;

namespace Pressing.Tests.Integration;

/// <summary>Ticket thermique : octets ESC/POS, réglages d'imprimante lisibles par le personnel, rôles.</summary>
[Collection("Api")]
public class PrintingApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public PrintingApiTests(ApiFactory factory) => _factory = factory;

    private static async Task<OrderDto> CreateOrderAsync(PressingApiClient api)
    {
        string phone = "+225 09 " + Random.Shared.NextInt64(10_000_000, 99_999_999).ToString().Insert(2, " ").Insert(5, " ").Insert(8, " ");
        ClientDto client = (await api.CreateClientAsync(new CreateClientRequest(
            "Ticket", "Thermique", phone, phone, $"th-{Guid.NewGuid():N}@example.com", "Cocody", null, null, false)))!;
        CategoryDto chemise = (await api.GetCatalogAsync())!.Single(c => c.Name == "Chemise");
        return (await api.CreateOrderAsync(new CreateOrderRequest(
            client.Id,
            [new CreateOrderItemRequest(chemise.Id, ServiceType.WashAndIron, 2, null, false, null, null, null)],
            false, null, DeliveryType.StorePickup, null, 0, 0, null, 0, null, 0, PaymentMethod.Cash, null)))!;
    }

    [Fact]
    public async Task Ticket_escpos_renvoie_des_octets_valides()
    {
        PressingApiClient cashier = await _factory.CashierAsync();
        OrderDto order = await CreateOrderAsync(cashier);

        byte[] data = await cashier.GetThermalTicketAsync(order.Id);

        Assert.True(data.Length > 100);
        Assert.Equal([0x1B, 0x40], data[..2]);                       // ESC @
        Assert.Equal([0x1D, 0x56, 66, 3], data[^4..]);               // coupe
        Assert.Contains(System.Text.Encoding.ASCII.GetBytes(order.TicketNumber).First(), data);
    }

    [Fact]
    public async Task Ticket_escpos_commande_inconnue_donne_404_et_client_portail_403()
    {
        PressingApiClient cashier = await _factory.CashierAsync();
        Assert.Equal(HttpStatusCode.NotFound,
            (await Assert.ThrowsAsync<ApiException>(() => cashier.GetThermalTicketAsync(987654))).StatusCode);

        PressingApiClient portal = await _factory.PortalClientAsync("+225 07 11 22 33 44");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Assert.ThrowsAsync<ApiException>(() => portal.GetThermalTicketAsync(1))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Assert.ThrowsAsync<ApiException>(() => portal.GetTicketPrinterAsync())).StatusCode);
    }

    [Fact]
    public async Task Reglages_imprimante_lisibles_par_le_caissier_et_modifiables_par_l_admin()
    {
        PressingApiClient cashier = await _factory.CashierAsync();
        PressingApiClient admin = await _factory.AdminAsync();

        TicketPrinterSettingsDto before = (await cashier.GetTicketPrinterAsync())!;
        Assert.False(before.Enabled);
        Assert.Equal(string.Empty, before.PrinterName);

        await admin.UpdateSettingsAsync(new Dictionary<string, string>
        {
            [SettingsKeys.TicketPrinterName] = "  EPSON TM-T20  ",
            [SettingsKeys.TicketPrinterOpenDrawer] = "true"
        });
        try
        {
            TicketPrinterSettingsDto after = (await cashier.GetTicketPrinterAsync())!;
            Assert.True(after.Enabled);
            Assert.Equal("EPSON TM-T20", after.PrinterName);
            Assert.True(after.OpenCashDrawer);

            OrderDto order = await CreateOrderAsync(cashier);
            byte[] data = await cashier.GetThermalTicketAsync(order.Id);
            Assert.Contains((byte)'p', data);                       // impulsion ESC p du tiroir
            Assert.Equal(EscPosReceiptBuilder.DrawerPulse(), data[^9..^4]);   // juste avant la coupe
        }
        finally
        {
            await admin.UpdateSettingsAsync(new Dictionary<string, string>
            {
                [SettingsKeys.TicketPrinterName] = string.Empty,
                [SettingsKeys.TicketPrinterOpenDrawer] = "false"
            });
        }
    }

    [Fact]
    public async Task Valeur_invalide_du_tiroir_refusee()
    {
        PressingApiClient admin = await _factory.AdminAsync();
        ApiException ex = await Assert.ThrowsAsync<ApiException>(() => admin.UpdateSettingsAsync(
            new Dictionary<string, string> { [SettingsKeys.TicketPrinterOpenDrawer] = "oui" }));
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }
}
