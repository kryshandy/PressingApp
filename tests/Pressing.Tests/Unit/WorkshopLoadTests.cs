using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Core.Services;
using Pressing.Tests.Support;
using ClientEntity = Pressing.Core.Entities.Client;

namespace Pressing.Tests.Unit;

/// <summary>Charge prévisionnelle de l'atelier : calcul par jour et par service, capacité, suggestion de date.</summary>
public class WorkshopLoadTests : IDisposable
{
    // Mardi 1er décembre 2026 : tous les calculs partent de ce jour fixe.
    private static readonly DateTime Today = new(2026, 12, 1);

    private readonly TestDb _t = new();
    private readonly WorkshopLoadService _load;
    private int _ticket;

    public WorkshopLoadTests()
    {
        _load = new WorkshopLoadService(_t.Db, _t.Settings);
    }

    public void Dispose() => _t.Dispose();

    private async Task<Order> AddOrderAsync(
        DateTime due, OrderStatus status = OrderStatus.InProgress, params (ServiceType Service, int Quantity, ItemStatus Status)[] lines)
    {
        int n = ++_ticket;
        ClientEntity client = await _t.AddClientAsync("Test", "Client" + n, $"+225 07 10 00 {n / 100:00} {n % 100:00}");
        Category chemise = await _t.CategoryAsync("Chemise");
        var order = new Order
        {
            TicketNumber = $"PRESS-2026-9{n:0000}",
            ClientId = client.Id,
            DueDate = due,
            Status = status
        };
        foreach (var (service, quantity, itemStatus) in lines)
        {
            order.Items.Add(new OrderItem { CategoryId = chemise.Id, ServiceType = service, Quantity = quantity, Status = itemStatus });
        }

        _t.Db.Orders.Add(order);
        await _t.Db.SaveChangesAsync();
        return order;
    }

    private static (ServiceType, int, ItemStatus) Line(ServiceType service, int quantity, ItemStatus status = ItemStatus.Deposited) =>
        (service, quantity, status);

    private static int Count(WorkshopLoadDayDto day, ServiceType service) =>
        day.ByService.Single(s => s.Service == service).Items;

    private async Task<Category> ChemiseAsync() => await _t.CategoryAsync("Chemise");

    [Fact]
    public async Task Charge_par_jour_et_par_service_avec_les_quantites()
    {
        await AddOrderAsync(Today, OrderStatus.Deposited,
            Line(ServiceType.Washing, 3), Line(ServiceType.WashAndIron, 2), Line(ServiceType.WashAndIron, 5));
        await AddOrderAsync(Today.AddDays(2), OrderStatus.InProgress,
            Line(ServiceType.DryCleaning, 4, ItemStatus.InProgress), Line(ServiceType.Ironing, 1, ItemStatus.Ready));
        await AddOrderAsync(Today.AddDays(13), OrderStatus.Deposited, Line(ServiceType.StainRemoval, 6));

        WorkshopLoadDto load = await _load.GetLoadAsync(14, Today);

        Assert.Equal(14, load.Days.Count);
        Assert.Equal(Today, load.Days[0].Date);
        Assert.True(load.Days[0].IsToday);
        Assert.Equal(10, load.Days[0].Items);
        Assert.Equal(1, load.Days[0].Orders);
        Assert.Equal(3, Count(load.Days[0], ServiceType.Washing));
        Assert.Equal(7, Count(load.Days[0], ServiceType.WashAndIron));
        Assert.Equal(0, Count(load.Days[0], ServiceType.DryCleaning));
        Assert.Equal(5, load.Days[2].Items);
        Assert.Equal(4, Count(load.Days[2], ServiceType.DryCleaning));
        Assert.Equal(1, Count(load.Days[2], ServiceType.Ironing));
        Assert.Equal(6, load.Days[13].Items);
        Assert.Equal(21, load.TotalItems);
        Assert.Equal(6, load.TotalsByService.Single(s => s.Service == ServiceType.StainRemoval).Items);
        Assert.Equal(5, load.Days[0].ByService.Count);
        Assert.Equal(0, load.OverdueItems);
    }

    [Fact]
    public async Task Les_articles_retires_ou_annules_et_les_commandes_annulees_sont_exclus()
    {
        await AddOrderAsync(Today.AddDays(1), OrderStatus.PartiallyPickedUp,
            Line(ServiceType.Washing, 2, ItemStatus.Ready),
            Line(ServiceType.Washing, 7, ItemStatus.PickedUp),
            Line(ServiceType.Washing, 9, ItemStatus.Cancelled));
        await AddOrderAsync(Today.AddDays(1), OrderStatus.Completed, Line(ServiceType.Ironing, 4, ItemStatus.PickedUp));
        await AddOrderAsync(Today.AddDays(1), OrderStatus.Cancelled, Line(ServiceType.DryCleaning, 8));

        WorkshopLoadDto load = await _load.GetLoadAsync(14, Today);

        Assert.Equal(2, load.Days[1].Items);
        Assert.Equal(1, load.Days[1].Orders);
        Assert.Equal(2, load.TotalItems);
    }

    [Fact]
    public async Task Une_ligne_partiellement_retiree_ne_compte_que_les_pieces_restantes()
    {
        Order order = await AddOrderAsync(Today.AddDays(3), OrderStatus.PartiallyPickedUp, Line(ServiceType.Washing, 6, ItemStatus.Ready));
        order.Items.Single().PickedUpQuantity = 4;
        await _t.Db.SaveChangesAsync();

        Assert.Equal(2, (await _load.GetLoadAsync(14, Today)).Days[3].Items);
    }

    [Fact]
    public async Task Les_retards_sont_les_articles_non_prets_dont_la_date_est_depassee()
    {
        await AddOrderAsync(Today.AddDays(-1), OrderStatus.InProgress,
            Line(ServiceType.Washing, 3, ItemStatus.Deposited), Line(ServiceType.Ironing, 2, ItemStatus.InProgress),
            Line(ServiceType.Ironing, 5, ItemStatus.Ready), Line(ServiceType.Ironing, 6, ItemStatus.PickedUp));
        await AddOrderAsync(Today.AddDays(-10), OrderStatus.Deposited, Line(ServiceType.DryCleaning, 4));
        await AddOrderAsync(Today.AddDays(-2), OrderStatus.Cancelled, Line(ServiceType.Washing, 9));
        await AddOrderAsync(Today, OrderStatus.Deposited, Line(ServiceType.Washing, 1));

        WorkshopLoadDto load = await _load.GetLoadAsync(14, Today);

        Assert.Equal(9, load.OverdueItems);
        Assert.Equal(2, load.OverdueOrders);
        Assert.Equal(1, load.Days[0].Items);
        Assert.Equal(1, load.TotalItems);
    }

    [Fact]
    public async Task Capacite_par_defaut_60_surcharge_au_dela_de_la_capacite()
    {
        await AddOrderAsync(Today, OrderStatus.Deposited, Line(ServiceType.Washing, 60));
        await AddOrderAsync(Today.AddDays(1), OrderStatus.Deposited, Line(ServiceType.Washing, 61));
        await AddOrderAsync(Today.AddDays(2), OrderStatus.Deposited, Line(ServiceType.Washing, 30));

        WorkshopLoadDto load = await _load.GetLoadAsync(14, Today);

        Assert.Equal(60, load.DailyCapacity);
        Assert.False(load.Days[0].Overloaded);
        Assert.Equal(1.0, load.Days[0].LoadRate);
        Assert.True(load.Days[1].Overloaded);
        Assert.False(load.Days[2].Overloaded);
        Assert.Equal(0.5, load.Days[2].LoadRate);
        Assert.Equal(1, load.OverloadedDays);
    }

    [Fact]
    public async Task La_capacite_est_parametrable_et_les_reglages_invalides_sont_refuses()
    {
        await AddOrderAsync(Today, OrderStatus.Deposited, Line(ServiceType.Washing, 11));
        await _load.SetCapacityAsync(10, _t.Cashier);

        WorkshopLoadDto load = await _load.GetLoadAsync(14, Today);
        Assert.Equal(10, load.DailyCapacity);
        Assert.True(load.Days[0].Overloaded);

        foreach (string bad in new[] { "0", "-5", "abc", "10001", "", "12,5" })
        {
            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _t.Settings.UpdateAsync(new UpdateSettingsRequest(new Dictionary<string, string>
                {
                    [SettingsKeys.WorkshopDailyCapacity] = bad
                }), _t.Cashier));
            Assert.Contains("capacité journalière", ex.Message);
        }

        Assert.Equal(10, await _load.GetCapacityAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => _load.SetCapacityAsync(0, _t.Cashier));
    }

    [Fact]
    public async Task Un_nombre_de_jours_hors_limites_est_refuse()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _load.GetLoadAsync(0, Today));
        await Assert.ThrowsAsync<ArgumentException>(() => _load.GetLoadAsync(61, Today));
        Assert.Equal(7, (await _load.GetLoadAsync(7, Today)).Days.Count);
    }

    [Fact]
    public async Task Suggestion_applique_le_delai_du_service_le_plus_long()
    {
        Category chemise = await ChemiseAsync();
        WorkshopDueDateSuggestionDto lavage = await _load.SuggestDueDateAsync(
            [new WorkshopSuggestLineRequest(chemise.Id, ServiceType.WashAndIron, 2)], false, Today);
        Assert.Equal(Today.AddDays(3), lavage.SuggestedDate);
        Assert.Equal(3, lavage.LeadDays);
        Assert.Equal(2, lavage.NewItems);
        Assert.True(lavage.CapacityRespected);

        WorkshopDueDateSuggestionDto mixte = await _load.SuggestDueDateAsync(
            [
                new WorkshopSuggestLineRequest(chemise.Id, ServiceType.WashAndIron, 1),
                new WorkshopSuggestLineRequest(chemise.Id, ServiceType.DryCleaning, 1)
            ], false, Today);
        Assert.Equal(Today.AddDays(5), mixte.SuggestedDate);
        Assert.Equal(5, mixte.LeadDays);
    }

    [Fact]
    public async Task Suggestion_saute_les_jours_pleins_et_explique_pourquoi()
    {
        Category chemise = await ChemiseAsync();
        // Délai de 3 jours → vendredi 4 déc. ; vendredi et samedi sont complets, dimanche a de la place.
        await AddOrderAsync(Today.AddDays(3), OrderStatus.Deposited, Line(ServiceType.Washing, 58));
        await AddOrderAsync(Today.AddDays(4), OrderStatus.Deposited, Line(ServiceType.Washing, 60));

        WorkshopDueDateSuggestionDto one = await _load.SuggestDueDateAsync(
            [new WorkshopSuggestLineRequest(chemise.Id, ServiceType.WashAndIron, 3)], false, Today);

        Assert.Equal(Today.AddDays(5), one.SuggestedDate);
        Assert.Equal(Today.AddDays(3), one.EarliestDate);
        Assert.Equal(2, one.SkippedDays);
        Assert.True(one.CapacityRespected);
        Assert.StartsWith("dimanche 6 déc.", one.Explanation);
        Assert.Contains("délai de 3 jours", one.Explanation);
        Assert.Contains("vendredi et samedi complets", one.Explanation);

        // 58 + 2 = 60 : le vendredi est encore acceptable pour 2 articles.
        WorkshopDueDateSuggestionDto fits = await _load.SuggestDueDateAsync(
            [new WorkshopSuggestLineRequest(chemise.Id, ServiceType.WashAndIron, 2)], false, Today);
        Assert.Equal(Today.AddDays(3), fits.SuggestedDate);
        Assert.Equal(0, fits.SkippedDays);
    }

    [Fact]
    public async Task Suggestion_un_seul_jour_complet_est_formule_au_singulier()
    {
        Category chemise = await ChemiseAsync();
        await AddOrderAsync(Today.AddDays(3), OrderStatus.Deposited, Line(ServiceType.Washing, 60));

        WorkshopDueDateSuggestionDto s = await _load.SuggestDueDateAsync(
            [new WorkshopSuggestLineRequest(chemise.Id, ServiceType.WashAndIron, 1)], false, Today);

        Assert.Equal("samedi 5 déc. : délai de 3 jours, vendredi complet.", s.Explanation);
    }

    [Fact]
    public async Task Suggestion_express_ignore_la_capacite()
    {
        Category chemise = await ChemiseAsync();
        await AddOrderAsync(Today.AddDays(1), OrderStatus.Deposited, Line(ServiceType.Washing, 500));

        WorkshopDueDateSuggestionDto s = await _load.SuggestDueDateAsync(
            [new WorkshopSuggestLineRequest(chemise.Id, ServiceType.DryCleaning, 4)], true, Today);

        Assert.Equal(Today.AddDays(1), s.SuggestedDate);
        Assert.True(s.IsExpress);
        Assert.Equal(1, s.LeadDays);
        Assert.Equal(0, s.SkippedDays);
        Assert.Contains("express", s.Explanation);

        await _t.SetSettingAsync(SettingsKeys.ExpressProcessingDays, "2");
        Assert.Equal(Today.AddDays(2), (await _load.SuggestDueDateAsync(
            [new WorkshopSuggestLineRequest(chemise.Id, ServiceType.DryCleaning, 4)], true, Today)).SuggestedDate);
    }

    [Fact]
    public async Task Suggestion_un_depot_plus_gros_que_la_capacite_prend_le_premier_jour_vide()
    {
        Category chemise = await ChemiseAsync();
        await AddOrderAsync(Today.AddDays(3), OrderStatus.Deposited, Line(ServiceType.Washing, 5));

        WorkshopDueDateSuggestionDto s = await _load.SuggestDueDateAsync(
            [new WorkshopSuggestLineRequest(chemise.Id, ServiceType.WashAndIron, 80)], false, Today);

        Assert.Equal(Today.AddDays(4), s.SuggestedDate);
        Assert.True(s.CapacityRespected);
    }

    [Fact]
    public async Task Suggestion_refuse_une_saisie_invalide()
    {
        Category chemise = await ChemiseAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _load.SuggestDueDateAsync([], false, Today));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _load.SuggestDueDateAsync(
            [new WorkshopSuggestLineRequest(chemise.Id, ServiceType.Washing, 0)], false, Today));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _load.SuggestDueDateAsync(
            [new WorkshopSuggestLineRequest(987654, ServiceType.Washing, 1)], false, Today));
    }
}
