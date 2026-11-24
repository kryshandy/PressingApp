using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Tests.Support;
using ClientEntity = Pressing.Core.Entities.Client;

namespace Pressing.Tests.Unit;

/// <summary>
/// Régression : une recherche textuelle (sans chiffre) produisait le motif LIKE "%%" sur le téléphone normalisé
/// et renvoyait donc tous les clients / toutes les commandes.
/// </summary>
public class SearchTests : IDisposable
{
    private readonly TestDb _t = new();

    public void Dispose() => _t.Dispose();

    private async Task<OrderDto> OrderForAsync(ClientEntity client)
    {
        Category chemise = await _t.CategoryAsync("Chemise");
        return await _t.Orders.CreateAsync(new CreateOrderRequest(
            client.Id,
            [new CreateOrderItemRequest(chemise.Id, ServiceType.WashAndIron, 1, null, false, null, null, null)],
            false, null, DeliveryType.StorePickup, null, 0, 0, null, 0, null, 0, PaymentMethod.Cash, null), _t.Cashier);
    }

    [Fact]
    public async Task Recherche_de_clients_par_nom_ne_retourne_que_les_correspondances()
    {
        await _t.AddClientAsync("Awa", "Koné", "+225 07 11 22 33 44");
        await _t.AddClientAsync("Koffi", "Yao", "+225 05 22 33 44 55");
        await _t.AddClientAsync("Mariam", "Traoré", "+225 01 33 44 55 66");

        PagedResult<ClientDto> byName = await _t.Clients.SearchAsync("Yao");
        Assert.Equal(1, byName.Total);
        Assert.Equal("Yao", byName.Items.Single().LastName);

        Assert.Equal(0, (await _t.Clients.SearchAsync("Introuvable")).Total);
    }

    [Fact]
    public async Task Recherche_de_clients_par_telephone_accepte_n_importe_quel_format()
    {
        await _t.AddClientAsync("Awa", "Koné", "+225 07 11 22 33 44");
        await _t.AddClientAsync("Koffi", "Yao", "+225 05 22 33 44 55");

        PagedResult<ClientDto> result = await _t.Clients.SearchAsync("05-22 33");
        Assert.Equal("Yao", result.Items.Single().LastName);
    }

    [Fact]
    public async Task Recherche_de_commandes_par_nom_ou_ticket_ne_retourne_que_les_correspondances()
    {
        ClientEntity awa = await _t.AddClientAsync("Awa", "Koné", "+225 07 11 22 33 44");
        ClientEntity koffi = await _t.AddClientAsync("Koffi", "Yao", "+225 05 22 33 44 55");
        OrderDto awaOrder = await OrderForAsync(awa);
        OrderDto koffiOrder = await OrderForAsync(koffi);

        PagedResult<OrderSummaryDto> byName = await _t.Orders.SearchAsync(search: "Yao");
        Assert.Equal(koffiOrder.Id, byName.Items.Single().Id);

        PagedResult<OrderSummaryDto> byTicket = await _t.Orders.SearchAsync(search: awaOrder.TicketNumber);
        Assert.Equal(awaOrder.Id, byTicket.Items.Single().Id);

        PagedResult<OrderSummaryDto> byPhone = await _t.Orders.SearchAsync(search: "0711223344");
        Assert.Equal(awaOrder.Id, byPhone.Items.Single().Id);
    }
}
