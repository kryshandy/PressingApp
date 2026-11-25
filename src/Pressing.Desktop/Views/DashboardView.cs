using System.Globalization;
using Pressing.Client;
using Pressing.Core.Dtos;
using Pressing.Desktop.Services;
using Pressing.Desktop.Ui;

namespace Pressing.Desktop.Views;

/// <summary>Tableau de bord : chiffres clés, graphique des 7 jours, statuts, commandes récentes et prêtes.</summary>
public sealed class DashboardView : BaseView
{
    private readonly PageHeader _header;
    private readonly StatCard _revenueToday;
    private readonly StatCard _revenueMonth;
    private readonly StatCard _active;
    private readonly StatCard _ready;
    private readonly StatCard _overdue;
    private readonly StatCard _outstanding;
    private readonly TableLayoutPanel _cards;
    private readonly TitledCard _chartCard;
    private readonly RevenueChart _chart = new();
    private readonly StatusBars _status = new();
    private readonly GridCard _recent = new("Aucune commande récente.");
    private readonly GridCard _readyList = new("Aucune commande prête pour le moment.");
    private int _columns;

    public DashboardView(IAppHost host) : base(host)
    {
        string firstName = Session.Staff.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? Session.Staff.FullName;
        string today = CultureInfo.GetCultureInfo("fr-FR").TextInfo.ToTitleCase(DateTime.Now.ToString("dddd d MMMM yyyy", CultureInfo.GetCultureInfo("fr-FR")));
        _header = new PageHeader("Tableau de bord", $"Bonjour {firstName} — {today}");
        _header.AddAction("Actualiser", ButtonKind.Secondary, Icons.Refresh, async (_, _) => await RunAsync(RefreshAsync));
        _header.AddAction("Nouveau dépôt", ButtonKind.Primary, Icons.Add, (_, _) => Host.StartNewOrder());

        _revenueToday = new StatCard("CA du jour", "—", Theme.Accent);
        _revenueMonth = new StatCard("CA du mois", "—", Theme.Blue);
        _active = new StatCard("Commandes actives", "—", Theme.Violet, "En cours de traitement");
        _ready = new StatCard("Prêtes à retirer", "—", Theme.Green, "À remettre aux clients");
        _overdue = new StatCard("En retard", "—", Theme.Red, "Date de retrait dépassée");
        _outstanding = new StatCard("Impayés", "—", Theme.Amber, "Soldes à encaisser");
        _active.MakeClickable(() => Host.Navigate(NavRegistry.Orders));
        _ready.MakeClickable(() => Host.Navigate(NavRegistry.Orders, new OrdersFilter { OnlyReady = true }));
        _overdue.MakeClickable(() => Host.Navigate(NavRegistry.Orders));
        _outstanding.MakeClickable(() => Host.Navigate(NavRegistry.Unpaid));

        _cards = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };

        _chartCard = new TitledCard("Chiffre d'affaires — 7 derniers jours", _chart, "Encaissements par jour");
        TitledCard statusCard = new("Répartition par statut", _status, "Commandes en base");
        TableLayoutPanel row2 = TwoColumns(_chartCard, statusCard, 58, Theme.S(330));

        ConfigureGrid(_recent.Grid, true);
        ConfigureGrid(_readyList.Grid, false);
        TitledCard recentCard = new("Commandes récentes", _recent, "Double-clic pour ouvrir", flushContent: true);
        TitledCard readyCard = new("Prêtes à retirer", _readyList, "Double-clic pour ouvrir", flushContent: true);
        TableLayoutPanel row3 = TwoColumns(recentCard, readyCard, 50, Theme.S(360));

        TableLayoutPanel page = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Margin = Padding.Empty
        };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.Controls.Add(_cards, 0, 0);
        page.Controls.Add(row2, 0, 1);
        page.Controls.Add(row3, 0, 2);

        ScrollPanel scroll = new();
        scroll.Controls.Add(page);
        Controls.Add(scroll);
        Controls.Add(_header);
        scroll.BringToFront();
        SizeChanged += (_, _) => ApplyCardLayout();
        ApplyCardLayout();
    }

    public override string Title => "Tableau de bord";

    private static TableLayoutPanel TwoColumns(Control left, Control right, int leftPercent, int height)
    {
        TableLayoutPanel row = new()
        {
            Dock = DockStyle.Top,
            Height = height,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, leftPercent));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100 - leftPercent));
        row.Controls.Add(left, 0, 0);
        row.Controls.Add(right, 1, 0);
        return row;
    }

    private void ApplyCardLayout()
    {
        int columns = Width >= Theme.S(1560) ? 6 : 3;
        if (columns == _columns)
        {
            return;
        }

        _columns = columns;
        _cards.SuspendLayout();
        _cards.Controls.Clear();
        _cards.ColumnStyles.Clear();
        _cards.RowStyles.Clear();
        _cards.ColumnCount = columns;
        for (int i = 0; i < columns; i++)
        {
            _cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
        }

        StatCard[] all = { _revenueToday, _revenueMonth, _active, _ready, _overdue, _outstanding };
        for (int i = 0; i < all.Length; i++)
        {
            int row = i / columns;
            while (_cards.RowStyles.Count <= row)
            {
                _cards.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(112)));
            }

            _cards.Controls.Add(all[i], i % columns, row);
        }

        _cards.Height = Theme.S(112) * ((all.Length + columns - 1) / columns) + Theme.S(8);
        _cards.ResumeLayout(true);
    }

    private void ConfigureGrid(ThemedGrid grid, bool recent)
    {
        grid.AddText("ticket", "TICKET", nameof(OrderRow.Ticket), 24);
        grid.AddText("client", "CLIENT", nameof(OrderRow.Client), 32);
        if (recent)
        {
            grid.AddText("total", "TOTAL", nameof(OrderRow.Total), 22, true);
            grid.AddPill("status", "STATUT", 28, 120);
        }
        else
        {
            grid.AddText("due", "PRÉVU LE", nameof(OrderRow.Due), 22);
            grid.AddText("balance", "RESTE", nameof(OrderRow.Balance), 22, true);
        }

        grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && grid.Rows[e.RowIndex].DataBoundItem is OrderRow row)
            {
                Host.OpenOrder(row.Id);
            }
        };
        grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && grid.SelectedItem<OrderRow>() is { } row)
            {
                Host.OpenOrder(row.Id);
                e.Handled = true;
            }
        };
    }

    public override async Task LoadAsync()
    {
        DashboardDto? data = await Api.GetDashboardAsync();
        if (!Alive || data is null)
        {
            return;
        }

        _revenueToday.Set(Fmt.Money(data.RevenueToday), $"{data.OrdersToday} commande(s) aujourd'hui");
        _revenueMonth.Set(Fmt.Money(data.RevenueMonth), $"{data.NewClientsMonth} nouveau(x) client(s) ce mois");
        _active.Set(data.ActiveOrders.ToString());
        _ready.Set(data.ReadyOrders.ToString());
        _overdue.Set(data.OverdueOrders.ToString());
        _outstanding.Set(Fmt.Money(data.OutstandingTotal));
        _chart.SetData(data.Last7Days);
        _chartCard.Subtitle = $"Total de la semaine : {Fmt.Money(data.Last7Days.Sum(p => p.Revenue))}";
        _status.SetData(data.StatusBreakdown);
        _recent.Grid.SetRows(data.RecentOrders.Select(o => new OrderRow(o)).ToList());
        _readyList.Grid.SetRows(data.ReadyOrdersList.Select(o => new OrderRow(o)).ToList());
        _recent.UpdateEmpty();
        _readyList.UpdateEmpty();
    }
}
