using System.Net;
using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Tests.Support;

namespace Pressing.Tests.Integration;

/// <summary>Routes /api/cash-closings : droits (403), parcours caissier puis manager, clôture validée figée, PDF.</summary>
[Collection("Api")]
public class CashClosingApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public CashClosingApiTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Parcours_caissier_cree_manager_valide_puis_la_cloture_est_figee()
    {
        PressingApiClient cashier = await _factory.CashierAsync();
        PressingApiClient manager = await _factory.ManagerAsync();
        DateTime day = DateTime.Today.AddDays(-3);

        CashClosingPreviewDto preview = (await cashier.PreviewCashClosingAsync(day))!;
        Assert.Equal(day, preview.Date);
        Assert.Null(preview.ExistingClosingId);

        decimal counted = 10_000 + preview.ExpectedCash;
        CashClosingDto draft = (await cashier.CreateCashClosingAsync(new CashClosingRequest(day, 10_000, counted, null)))!;
        Assert.Equal(CashClosingStatus.Draft, draft.Status);
        Assert.Equal(0m, draft.Variance);

        // Le caissier ne valide pas et ne consulte pas l'historique.
        ApiException forbidden = await Assert.ThrowsAsync<ApiException>(() => cashier.ValidateCashClosingAsync(draft.Id));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        forbidden = await Assert.ThrowsAsync<ApiException>(() => cashier.GetCashClosingsAsync());
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        CashClosingDto validated = (await manager.ValidateCashClosingAsync(draft.Id))!;
        Assert.Equal(CashClosingStatus.Validated, validated.Status);
        Assert.False(string.IsNullOrWhiteSpace(validated.ValidatedBy));

        ApiException frozen = await Assert.ThrowsAsync<ApiException>(() =>
            cashier.UpdateCashClosingAsync(draft.Id, new CashClosingRequest(day, 10_000, counted + 100, "tentative")));
        Assert.Equal(HttpStatusCode.BadRequest, frozen.StatusCode);

        ApiException duplicate = await Assert.ThrowsAsync<ApiException>(() =>
            cashier.CreateCashClosingAsync(new CashClosingRequest(day, 0, 0, null)));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        List<CashClosingDto> history = (await manager.GetCashClosingsAsync(day, day))!;
        Assert.Equal(draft.Id, Assert.Single(history).Id);
        Assert.Equal(draft.Id, (await cashier.GetCashClosingAsync(draft.Id))!.Id);

        byte[] pdf = await cashier.GetCashClosingPdfAsync(draft.Id);
        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public async Task Ecart_sans_commentaire_donne_400_et_cloture_inconnue_404()
    {
        PressingApiClient cashier = await _factory.CashierAsync();
        DateTime day = DateTime.Today.AddDays(-4);

        ApiException ex = await Assert.ThrowsAsync<ApiException>(() =>
            cashier.CreateCashClosingAsync(new CashClosingRequest(day, 0, 12_345, null)));
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("commentaire", ex.Message);

        ex = await Assert.ThrowsAsync<ApiException>(() => cashier.GetCashClosingAsync(987_654));
        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task Routes_exigent_une_authentification_personnel()
    {
        PressingApiClient anonymous = _factory.NewApiClient();
        ApiException ex = await Assert.ThrowsAsync<ApiException>(() => anonymous.PreviewCashClosingAsync(DateTime.Today));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);

        PressingApiClient customer = await _factory.PortalClientAsync("+225 07 11 22 33 44");
        ex = await Assert.ThrowsAsync<ApiException>(() => customer.PreviewCashClosingAsync(DateTime.Today));
        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }
}
