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
    private readonly Label _limit = new() { AutoSize = true, ForeColor = Theme.Amber, Font = Theme.F(9.5f), Visible = false, Margin = new Padding(0, Theme.S(6), 0, 0) };

    public OutstandingView(IAppHost host) : base(host)
    {
        PageHeader header = new("Impayés", "Double-cliquez sur une commande pour encaisser le solde");
        header.AddAction("Actualiser", ButtonKind.Secondary, Icons.Refresh, async (_, _) => await RunAsync(RefreshAsync));

        TableLayoutPanel stats = new() { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
        for (int i = 0; i < 3; i++)
        {
            stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        }

        stats.Controls.Add(_total, 0, 0);
        stats.Controls.Add(_orders, 1, 0);
        stats.Controls.Add(_oldest, 2, 0);
        Panel statsWrap = new() { Height = Theme.S(116), Margin = Padding.Empty, Padding = new Padding(0, 0, 0, Theme.S(8)) };
        statsWrap.Controls.Add(stats);

        ThemedGrid grid = _card.Grid;
        grid.AddText("ticket", "TICKET", nameof(OrderRow.Ticket), 17, minWidth: 130);
        grid.AddText("client", "CLIENT", nameof(OrderRow.Client), 22, minWidth: 130);
        grid.AddText("created", "DÉPOSÉE LE", nameof(OrderRow.Created), 12, minWidth: 100);
        grid.AddText("due", "PRÉVUE LE", nameof(OrderRow.Due), 12, minWidth: 100);
        grid.AddText("total", "TOTAL", nameof(OrderRow.Total), 12, true, 100);
        grid.AddText("balance", "RESTE À PAYER", nameof(OrderRow.Balance), 14, true, 120);
        grid.AddPill("status", "STATUT", 14, 130);
        grid.CellDoubleClick += (_, e) => OpenRow(e.RowIndex);
        grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                OpenRow(grid.CurrentRow?.Index ?? -1);
                e.Handled = true;
            }
        };

        Controls.Add(UiKit.Rows((header, false), (statsWrap, false), (_card, true), (_limit, false)));
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
