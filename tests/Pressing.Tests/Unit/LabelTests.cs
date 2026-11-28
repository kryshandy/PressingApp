using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Core.Labels;
using Pressing.Tests.Support;
using ClientEntity = Pressing.Core.Entities.Client;

namespace Pressing.Tests.Unit;

/// <summary>Codes d'étiquette (format, décodage) et planche PDF.</summary>
public class LabelTests : IDisposable
{
    private readonly TestDb _t = new();

    public void Dispose() => _t.Dispose();

    private async Task<Order> CreateOrderAsync(int chemises, int pantalons)
    {
        ClientEntity client = await _t.AddClientAsync("Awa", "Koné");
        Category chemise = await _t.CategoryAsync("Chemise");
        Category pantalon = await _t.CategoryAsync("Pantalon");
        var items = new List<CreateOrderItemRequest>
        {
            new(chemise.Id, ServiceType.WashAndIron, chemises, null, false, null, null, null),
            new(pantalon.Id, ServiceType.WashAndIron, pantalons, null, false, null, null, null)
        };
        OrderDto dto = await _t.Orders.CreateAsync(new CreateOrderRequest(
            client.Id, items, false, null, DeliveryType.StorePickup, null, 0, 0, null, 0, null, 0,
            PaymentMethod.Cash, null), _t.Cashier);
        return await _t.Orders.LoadFullAsync(dto.Id);
    }

    [Theory]
    [InlineData("PRESS-2026-00042", 1, null, "PRESS-2026-00042-01")]
    [InlineData("PRESS-2026-00042", 12, "c", "PRESS-2026-00042-12-c")]
    [InlineData("PRESS-2027-123456", 3, "aa", "PRESS-2027-123456-03-aa")]
    public void Code_aller_retour(string ticket, int rank, string? unit, string expected)
    {
        string code = ItemCodes.Format(ticket, rank, unit);
        Assert.Equal(expected, code);

        Assert.True(ItemCodes.TryParseItemCode(code, out ItemCode? parsed));
        Assert.Equal(new ItemCode(ticket, rank, unit), parsed);
        Assert.Equal(code, parsed!.FullCode);
    }

    [Theory]
    [InlineData("  press-2026-00042-01-A  ", "PRESS-2026-00042-01-a")]
    [InlineData("PRESS‑2026‑00042‑02", "PRESS-2026-00042-02")]
    public void Decodage_tolere_casse_espaces_et_tirets_typographiques(string scanned, string expected)
    {
        Assert.True(ItemCodes.TryParseItemCode(scanned, out ItemCode? parsed));
        Assert.Equal(expected, parsed!.FullCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("PRESS-2026-00042")]
    [InlineData("PRESS-2026-00042-00")]
    [InlineData("PRESS-26-00042-01")]
    [InlineData("TICKET:PRESS-2026-00042|CLIENT:+225")]
    [InlineData("PRESS-2026-00042-01-1")]
    [InlineData("5449000000996")]
    public void Decodage_refuse_les_valeurs_invalides(string? scanned)
    {
        Assert.False(ItemCodes.TryParseItemCode(scanned, out ItemCode? parsed));
        Assert.Null(parsed);
    }

    [Theory]
    [InlineData(1, "a")]
    [InlineData(2, "b")]
    [InlineData(26, "z")]
    [InlineData(27, "aa")]
    [InlineData(52, "az")]
    [InlineData(53, "ba")]
    public void Suffixe_de_piece(int index, string expected) =>
        Assert.Equal(expected, ItemCodes.UnitSuffix(index));

    [Fact]
    public async Task Une_etiquette_par_piece_avec_rang_et_suffixe()
    {
        Order order = await CreateOrderAsync(chemises: 3, pantalons: 1);

        IReadOnlyList<LabelInfo> labels = LabelService.BuildLabels(order);

        Assert.Equal(4, labels.Count);
        string t = order.TicketNumber;
        Assert.Equal(
            [$"{t}-01-a", $"{t}-01-b", $"{t}-01-c", $"{t}-02"],
            labels.Select(l => l.Code).ToArray());
        Assert.Equal("Pièce 2/3", labels[1].PieceText);
        Assert.Equal("Awa Koné", labels[0].ClientName);
        Assert.All(labels, l => Assert.True(ItemCodes.TryParseItemCode(l.Code, out _)));
    }

    [Fact]
    public async Task Planche_pdf_non_vide()
    {
        Order order = await CreateOrderAsync(chemises: 3, pantalons: 2);

        byte[] pdf = new LabelService().GeneratePdf(order);

        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public async Task Plus_de_24_etiquettes_donnent_une_seconde_page()
    {
        Order order = await CreateOrderAsync(chemises: 20, pantalons: 10);

        Assert.Equal(30, LabelService.BuildLabels(order).Count);
        byte[] pdf = new LabelService().GeneratePdf(order);

        // Une seule page ne peut en contenir que LabelsPerPage ; le document doit déclarer au moins 2 pages.
        string text = System.Text.Encoding.Latin1.GetString(pdf);
        Assert.True(System.Text.RegularExpressions.Regex.Matches(text, @"/Type\s*/Page\b").Count >= 2);
        Assert.True(LabelService.LabelsPerPage < 30);
    }
}
