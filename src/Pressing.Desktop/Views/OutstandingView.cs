using Pressing.Client;
using Pressing.Core.Dtos;
using Pressing.Desktop.Services;
using Pressing.Desktop.Ui;

namespace Pressing.Desktop.Views;

/// <summary>Commandes avec un solde à encaisser.</summary>
public sealed class OutstandingView : BaseView
{
    private readonly StatCard _total = new("Total des impayés", "—", Theme.Amber);
    private readonly StatCard _orders = new("Commandes concernées", "—", Theme.Blue);
    private readonly StatCard _oldest = new("Plus ancien impayé", "—", Theme.Red);
    private readonly GridCard _card = new("Aucun impayé : tous les comptes sont à jour.");
    private readonly TextLabel _limit = new() { ForeColor = Theme.Amber, Font = Theme.F(9.5f), Visible = false };

    public OutstandingView(IAppHost host) : base(host)
    {
        PageHeader header = new("Impayés", "Double-cliquez sur une commande pour encaisser le solde");
        header.AddAction("Actualiser", ButtonKind.Secondary, Icons.Refresh, async (_, _) => await RunAsync(RefreshAsync));

        ThemedGrid grid = _card.Grid;
        grid.AddText("ticket", "TICKET", nameof(OrderRow.Ticket), fixedWidth: 164);
        grid.AddText("client", "CLIENT", nameof(OrderRow.Client), 100, minWidth: 150);
        grid.AddText("created", "DÉPOSÉE LE", nameof(OrderRow.Created), fixedWidth: 120);
        grid.AddText("due", "PRÉVUE LE", nameof(OrderRow.Due), fixedWidth: 120);
        grid.AddText("total", "TOTAL", nameof(OrderRow.Total), right: true, fixedWidth: 120);
        grid.AddText("balance", "RESTE À PAYER", nameof(OrderRow.Balance), right: true, fixedWidth: 148);
        grid.AddPill("status", "STATUT", fixedWidth: 148);
        grid.CellDoubleClick += (_, e) => OpenRow(e.RowIndex);
        grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                OpenRow(grid.CurrentRow?.Index ?? -1);
                e.Handled = true;
            }
        };

        TitledCard list = new("Soldes à encaisser", _card, "Double-clic ou Entrée pour ouvrir la commande", flushContent: true) { MinimumSize = new Size(0, Theme.S(360)) };
        VStack page = new VStack()
            .Add(new GridRow().Add(_total, 4).Add(_orders, 4).Add(_oldest, 4))
            .Add(list, StackSize.Fill)
            .Add(_limit);
        SetPage(header, page, scroll: true);
    }

    public override string Title => "Impayés";

    public override Task ReactivateAsync() => LoadAsync();

    private void OpenRow(int index)
    {
        ThemedGrid grid = _card.Grid;
        if (index >= 0 && index < grid.Rows.Count && grid.Rows[index].DataBoundItem is OrderRow row)
        {
            Host.OpenOrder(row.Id);
        }
    }

    public override async Task LoadAsync()
    {
        const int take = 200;
        List<OrderSummaryDto>? orders = await Api.GetOutstandingOrdersAsync(take);
        if (!Alive || orders is null)
        {
            return;
        }

        decimal total = orders.Sum(o => o.Balance);
        _total.Set(Fmt.Money(total), "à encaisser");
        _orders.Set(orders.Count.ToString(), "commande(s) non soldée(s)");
        if (orders.Count > 0)
        {
            OrderSummaryDto oldest = orders.OrderBy(o => o.CreatedAt).First();
            _oldest.Set(Fmt.Date(oldest.CreatedAt), $"{oldest.TicketNumber} · {Fmt.Money(oldest.Balance)}");
        }
        else
        {
            _oldest.Set("—", "rien à relancer");
        }

        _card.Grid.SetRows(orders.OrderByDescending(o => o.Balance).Select(o => new OrderRow(o)).ToList());
        _card.UpdateEmpty();
        _limit.Visible = orders.Count >= take;
        _limit.Text = $"Seules les {take} premières commandes impayées sont affichées.";
    }
}
