using System.Net;
using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Tests.Support;

namespace Pressing.Tests.Integration;

/// <summary>Routes de charge de l'atelier : nominal, droits (401/403) et refus des valeurs invalides.</summary>
[Collection("Api")]
public class WorkshopLoadApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public WorkshopLoadApiTests(ApiFactory factory) => _factory = factory;

    private async Task<WorkshopSuggestLineRequest> LineAsync(PressingApiClient api, ServiceType service, int quantity)
    {
        CategoryDto chemise = (await api.GetCatalogAsync())!.Single(c => c.Name == "Chemise");
        return new WorkshopSuggestLineRequest(chemise.Id, service, quantity);
    }

    [Fact]
    public async Task La_charge_est_calculee_pour_14_jours_par_defaut_et_aujourd_hui_en_premier()
    {
        PressingApiClient api = await _factory.CashierAsync();

        WorkshopLoadDto load = (await api.GetWorkshopLoadAsync())!;

        Assert.Equal(14, load.Days.Count);
        Assert.Equal(DateTime.Today, load.Days[0].Date);
        Assert.True(load.Days[0].IsToday);
        Assert.True(load.DailyCapacity >= 1);
        Assert.Equal(5, load.TotalsByService.Count);

        Assert.Equal(3, (await api.GetWorkshopLoadAsync(3))!.Days.Count);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Assert.ThrowsAsync<ApiException>(() => api.GetWorkshopLoadAsync(0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Assert.ThrowsAsync<ApiException>(() => api.GetWorkshopLoadAsync(500))).StatusCode);
    }

    [Fact]
    public async Task Un_depot_cree_ajoute_sa_charge_au_jour_prevu()
    {
        PressingApiClient api = await _factory.CashierAsync();
        int before = (await api.GetWorkshopLoadAsync())!.TotalItems;

        string phone = "+225 09 " + Random.Shared.NextInt64(10_000_000, 99_999_999).ToString().Insert(2, " ").Insert(5, " ").Insert(8, " ");
        ClientDto client = (await api.CreateClientAsync(new CreateClientRequest(
            "Charge", "Atelier", phone, phone, $"charge-{Guid.NewGuid():N}@example.com", "Cocody", null, null, false)))!;
        WorkshopSuggestLineRequest line = await LineAsync(api, ServiceType.WashAndIron, 4);
        OrderDto order = (await api.CreateOrderAsync(new CreateOrderRequest(
            client.Id, [new CreateOrderItemRequest(line.CategoryId, line.ServiceType, 4, null, false, null, null, null)],
            false, null, DeliveryType.StorePickup, null, 0, 0, null, 0, null, 0, PaymentMethod.Cash, null)))!;

        WorkshopLoadDto after = (await api.GetWorkshopLoadAsync())!;
        WorkshopLoadDayDto day = after.Days.Single(d => d.Date == order.DueDate.Date);
        Assert.Equal(before + 4, after.TotalItems);
        Assert.True(day.Items >= 4);
        Assert.True(day.ByService.Single(s => s.Service == ServiceType.WashAndIron).Items >= 4);
    }

    [Fact]
    public async Task La_suggestion_renvoie_une_date_et_son_explication()
    {
        PressingApiClient api = await _factory.CashierAsync();
        WorkshopSuggestLineRequest line = await LineAsync(api, ServiceType.DryCleaning, 2);

        WorkshopDueDateSuggestionDto normal = (await api.SuggestDueDateAsync([line], false))!;
        Assert.True(normal.SuggestedDate >= DateTime.Today.AddDays(normal.LeadDays));
        Assert.Contains("délai de", normal.Explanation);

        WorkshopDueDateSuggestionDto express = (await api.SuggestDueDateAsync([line], true))!;
        Assert.True(express.IsExpress);
        Assert.Equal(DateTime.Today.AddDays(express.LeadDays), express.SuggestedDate);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await Assert.ThrowsAsync<ApiException>(() => api.SuggestDueDateAsync([], false))).StatusCode);
    }

    [Fact]
    public async Task Routes_de_charge_refusees_sans_jeton_et_a_un_client_du_portail()
    {
        PressingApiClient anonymous = _factory.NewApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Assert.ThrowsAsync<ApiException>(() => anonymous.GetWorkshopLoadAsync())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Assert.ThrowsAsync<ApiException>(() => anonymous.SuggestDueDateAsync([new WorkshopSuggestLineRequest(1, ServiceType.Washing, 1)], false))).StatusCode);

        PressingApiClient portal = await _factory.PortalClientAsync("+225 07 11 22 33 44");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Assert.ThrowsAsync<ApiException>(() => portal.GetWorkshopLoadAsync())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Assert.ThrowsAsync<ApiException>(() => portal.SuggestDueDateAsync([new WorkshopSuggestLineRequest(1, ServiceType.Washing, 1)], false))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Assert.ThrowsAsync<ApiException>(() => portal.SetWorkshopCapacityAsync(50))).StatusCode);
    }

    [Fact]
    public async Task La_capacite_se_modifie_en_manager_ou_admin_pas_en_caisse_et_refuse_les_valeurs_invalides()
    {
        PressingApiClient cashier = await _factory.CashierAsync();
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Assert.ThrowsAsync<ApiException>(() => cashier.SetWorkshopCapacityAsync(50))).StatusCode);

        PressingApiClient manager = await _factory.ManagerAsync();
        int original = (await manager.GetWorkshopLoadAsync())!.DailyCapacity;
        try
        {
            Assert.Equal(45, (await manager.SetWorkshopCapacityAsync(45))!.DailyCapacity);
            Assert.Equal(45, (await cashier.GetWorkshopLoadAsync())!.DailyCapacity);

            foreach (int bad in new[] { 0, -3, 10_001 })
            {
                ApiException ex = await Assert.ThrowsAsync<ApiException>(() => manager.SetWorkshopCapacityAsync(bad));
                Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
                Assert.Contains("capacité journalière", ex.Message);
            }

            Assert.Equal(45, (await cashier.GetWorkshopLoadAsync())!.DailyCapacity);

            PressingApiClient admin = await _factory.AdminAsync();
            ApiException viaSettings = await Assert.ThrowsAsync<ApiException>(() => admin.UpdateSettingsAsync(
                new Dictionary<string, string> { [SettingsKeys.WorkshopDailyCapacity] = "abc" }));
            Assert.Equal(HttpStatusCode.BadRequest, viaSettings.StatusCode);
            Assert.Equal(80, (await admin.SetWorkshopCapacityAsync(80))!.DailyCapacity);
        }
        finally
        {
            await manager.SetWorkshopCapacityAsync(original);
        }

        Assert.Equal(original, (await cashier.GetWorkshopLoadAsync())!.DailyCapacity);
    }
}
