using Microsoft.EntityFrameworkCore;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Core.Security;
using Pressing.Core.Services;
using Pressing.Tests.Support;
using ClientEntity = Pressing.Core.Entities.Client;

namespace Pressing.Tests.Unit;

/// <summary>Clôture de caisse journalière : attendus, écart, unicité, immuabilité après validation, PDF, audit.</summary>
public class CashClosingServiceTests : IDisposable
{
    private readonly TestDb _t = new();
    private readonly CurrentUser _manager = new() { StaffId = 1, Name = "Manager Test", Role = "Manager" };

    private CashClosingService Service => _t.Get<CashClosingService>();

    public void Dispose() => _t.Dispose();

    private static DateTime Yesterday => DateTime.Today.AddDays(-1);

    private async Task<OrderDto> NewOrderAsync(ClientEntity client)
    {
        Category chemise = await _t.CategoryAsync("Chemise");
        Category pantalon = await _t.CategoryAsync("Pantalon");
        return await _t.Orders.CreateAsync(new CreateOrderRequest(
            client.Id,
            new List<CreateOrderItemRequest>
            {
                new(chemise.Id, ServiceType.WashAndIron, 2, null, false, null, null, null),
                new(pantalon.Id, ServiceType.WashAndIron, 1, null, false, null, null, null)
            },
            false, null, DeliveryType.StorePickup, null, 0, 0, null, 0, null, 0, PaymentMethod.Cash, null), _t.Cashier);
    }

    private Task PayAsync(OrderDto order, decimal amount, PaymentMethod method, DateTime? paidAt = null) =>
        _t.Payments.AddPaymentAsync(order.Id, new AddPaymentRequest(amount, method, null, null, paidAt), _t.Cashier);

    /// <summary>Aujourd'hui : espèces 1 000 + 500 − 400 remboursés, Mobile Money 700, carte 300, virement 200 ; hier : 2 000 espèces.</summary>
    private async Task SeedDayAsync()
    {
        ClientEntity client = await _t.AddClientAsync();
        OrderDto first = await NewOrderAsync(client);
        await PayAsync(first, 1000, PaymentMethod.Cash);
        await PayAsync(first, 500, PaymentMethod.Cash);
        await PayAsync(first, 700, PaymentMethod.MobileMoney);
        await PayAsync(first, 300, PaymentMethod.Card);
        await PayAsync(first, 200, PaymentMethod.BankTransfer);
        await _t.Payments.RefundAsync(first.Id, 400, PaymentMethod.Cash, "geste", _t.Cashier);

        OrderDto second = await NewOrderAsync(client);
        await PayAsync(second, 2000, PaymentMethod.Cash, Yesterday.AddHours(10));
    }

    [Fact]
    public async Task Apercu_calcule_les_attendus_par_mode_net_des_remboursements_et_exclut_les_autres_jours()
    {
        await SeedDayAsync();

        CashClosingPreviewDto today = await Service.PreviewAsync(DateTime.Today);

        Assert.Equal(1100m, today.ExpectedCash);
        Assert.Equal(700m, today.ExpectedMobileMoney);
        Assert.Equal(300m, today.ExpectedCard);
        Assert.Equal(200m, today.ExpectedTransfer);
        Assert.Equal(0m, today.ExpectedOther);
        Assert.Equal(2300m, today.TotalExpected);
        Assert.Equal(5, today.PaymentsCount);
        Assert.Equal(1, today.RefundsCount);
        Assert.Null(today.ExistingClosingId);

        CashClosingPreviewDto yesterday = await Service.PreviewAsync(Yesterday);
        Assert.Equal(2000m, yesterday.ExpectedCash);
        Assert.Equal(2000m, yesterday.TotalExpected);
        Assert.False(await _t.Db.CashClosings.AnyAsync(), "l'aperçu n'enregistre rien");
    }

    [Fact]
    public async Task Creation_sans_ecart_ne_demande_pas_de_commentaire()
    {
        await SeedDayAsync();

        // Attendu en tiroir : fond 5 000 + espèces nettes 1 100.
        CashClosingDto closing = await Service.CreateAsync(new CashClosingRequest(DateTime.Today, 5000, 6100, null), _t.Cashier);

        Assert.Equal(CashClosingStatus.Draft, closing.Status);
        Assert.Equal(6100m, closing.ExpectedCashInDrawer);
        Assert.Equal(0m, closing.Variance);
        Assert.Equal(2300m, closing.TotalExpected);
        Assert.Equal("Test", closing.CreatedBy);
        Assert.Null(closing.ValidatedAt);
    }

    [Theory]
    [InlineData(6000, -100)]
    [InlineData(6250, 150)]
    public async Task Ecart_non_nul_exige_un_commentaire(decimal counted, decimal expectedVariance)
    {
        await SeedDayAsync();

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service.CreateAsync(new CashClosingRequest(DateTime.Today, 5000, counted, "   "), _t.Cashier));
        Assert.Contains("commentaire", ex.Message);
        Assert.False(await _t.Db.CashClosings.AnyAsync());

        CashClosingDto closing = await Service.CreateAsync(
            new CashClosingRequest(DateTime.Today, 5000, counted, "  Erreur de rendu monnaie  "), _t.Cashier);
        Assert.Equal(expectedVariance, closing.Variance);
        Assert.Equal("Erreur de rendu monnaie", closing.Comment);
    }

    [Fact]
    public async Task Jour_futur_et_montants_invalides_sont_refuses()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service.CreateAsync(new CashClosingRequest(DateTime.Today.AddDays(1), 0, 0, null), _t.Cashier));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service.CreateAsync(new CashClosingRequest(Yesterday, -1, 0, null), _t.Cashier));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service.CreateAsync(new CashClosingRequest(Yesterday, 0, -5, null), _t.Cashier));
    }

    [Fact]
    public async Task Validation_fige_la_cloture_et_une_seule_cloture_validee_par_jour()
    {
        await SeedDayAsync();
        CashClosingDto draft = await Service.CreateAsync(new CashClosingRequest(DateTime.Today, 5000, 6100, null), _t.Cashier);

        // Un brouillon par jour au plus.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service.CreateAsync(new CashClosingRequest(DateTime.Today, 5000, 6100, null), _t.Cashier));

        CashClosingDto validated = await Service.ValidateAsync(draft.Id, _manager);
        Assert.Equal(CashClosingStatus.Validated, validated.Status);
        Assert.Equal("Manager Test", validated.ValidatedBy);
        Assert.NotNull(validated.ValidatedAt);

        InvalidOperationException again = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service.CreateAsync(new CashClosingRequest(DateTime.Today, 0, 0, null), _t.Cashier));
        Assert.Contains("déjà clôturée", again.Message);

        CashClosingPreviewDto preview = await Service.PreviewAsync(DateTime.Today);
        Assert.Equal(draft.Id, preview.ExistingClosingId);
        Assert.Equal(CashClosingStatus.Validated, preview.ExistingStatus);
    }

    [Fact]
    public async Task Index_unique_empeche_deux_clotures_validees_le_meme_jour_mais_pas_deux_brouillons_historiques()
    {
        CashClosing Make(CashClosingStatus status) => new()
        {
            BusinessDate = Yesterday,
            Status = status,
            CreatedByName = "Test"
        };

        _t.Db.CashClosings.AddRange(Make(CashClosingStatus.Draft), Make(CashClosingStatus.Draft), Make(CashClosingStatus.Validated));
        await _t.Db.SaveChangesAsync();

        _t.Db.CashClosings.Add(Make(CashClosingStatus.Validated));
        await Assert.ThrowsAsync<DbUpdateException>(() => _t.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Cloture_validee_n_est_plus_modifiable()
    {
        await SeedDayAsync();
        CashClosingDto draft = await Service.CreateAsync(new CashClosingRequest(DateTime.Today, 5000, 6100, null), _t.Cashier);

        CashClosingDto edited = await Service.UpdateAsync(draft.Id, new CashClosingRequest(DateTime.Today, 5000, 6050, "Billet douteux"), _t.Cashier);
        Assert.Equal(-50m, edited.Variance);

        await Service.ValidateAsync(draft.Id, _manager);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service.UpdateAsync(draft.Id, new CashClosingRequest(DateTime.Today, 5000, 6100, null), _t.Cashier));
        Assert.Contains("validée", ex.Message);

        CashClosingDto stored = await Service.GetAsync(draft.Id);
        Assert.Equal(6050m, stored.CountedCash);
        Assert.Equal("Billet douteux", stored.Comment);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.ValidateAsync(draft.Id, _manager));
    }

    [Fact]
    public async Task Validation_refusee_a_un_caissier()
    {
        await SeedDayAsync();
        CashClosingDto draft = await Service.CreateAsync(new CashClosingRequest(DateTime.Today, 5000, 6100, null), _t.Cashier);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.ValidateAsync(draft.Id, _t.Cashier));

        Assert.Equal(CashClosingStatus.Draft, (await Service.GetAsync(draft.Id)).Status);
    }

    [Fact]
    public async Task Validation_recalcule_les_attendus_et_exige_un_commentaire_si_un_ecart_apparait()
    {
        ClientEntity client = await _t.AddClientAsync();
        OrderDto order = await NewOrderAsync(client);
        await PayAsync(order, 1000, PaymentMethod.Cash);
        CashClosingDto draft = await Service.CreateAsync(new CashClosingRequest(DateTime.Today, 0, 1000, null), _t.Cashier);
        Assert.Equal(0m, draft.Variance);

        await PayAsync(order, 500, PaymentMethod.Cash); // encaissé après le brouillon

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Service.ValidateAsync(draft.Id, _manager));
        Assert.Contains("commentaire", ex.Message);

        await Service.UpdateAsync(draft.Id, new CashClosingRequest(DateTime.Today, 0, 1500, null), _t.Cashier);
        CashClosingDto validated = await Service.ValidateAsync(draft.Id, _manager);
        Assert.Equal(1500m, validated.ExpectedCash);
        Assert.Equal(0m, validated.Variance);
    }

    [Fact]
    public async Task Creation_et_validation_sont_auditees()
    {
        await SeedDayAsync();
        CashClosingDto draft = await Service.CreateAsync(new CashClosingRequest(DateTime.Today, 5000, 6100, null), _t.Cashier);
        await Service.ValidateAsync(draft.Id, _manager);

        List<string> actions = await _t.Db.AuditLogs
            .Where(a => a.EntityType == nameof(CashClosing))
            .OrderBy(a => a.Id)
            .Select(a => a.Action)
            .ToListAsync();
        Assert.Equal(new[] { "cash-closing-create", "cash-closing-validate" }, actions);
    }

    [Fact]
    public async Task Historique_filtre_par_periode_et_pdf_non_vide()
    {
        await SeedDayAsync();
        CashClosingDto today = await Service.CreateAsync(new CashClosingRequest(DateTime.Today, 5000, 6100, null), _t.Cashier);
        await Service.CreateAsync(new CashClosingRequest(Yesterday, 0, 1990, "Dix francs de moins"), _t.Cashier);

        Assert.Equal(2, (await Service.ListAsync(null, null)).Count);
        IReadOnlyList<CashClosingDto> onlyToday = await Service.ListAsync(DateTime.Today, DateTime.Today);
        Assert.Equal(today.Id, Assert.Single(onlyToday).Id);

        (byte[] bytes, string fileName) = await Service.GetPdfAsync(today.Id);
        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal($"cloture-caisse-{DateTime.Today:yyyyMMdd}.pdf", fileName);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => Service.GetPdfAsync(9999));
    }
}
