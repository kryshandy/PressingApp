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
    private readonly TitledCard _chartCard;
    private readonly RevenueChart _chart = new();
    private readonly StatusBars _status = new();
    private readonly GridCard _recent = new("Aucune commande récente.");
    private readonly GridCard _readyList = new("Aucune commande prête pour le moment.");
    private readonly WorkshopLoadChart _load = new() { Compact = true, DesiredHeight = 104 };
    private readonly TextLabel _loadSummary = new() { Wrap = true, Font = Theme.F(10f), ForeColor = Theme.TextMuted, Text = "Chargement de la charge…" };
    private readonly TitledCard _loadCard;

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

        _chartCard = new TitledCard("Chiffre d'affaires — 7 derniers jours", _chart, "Encaissements par jour");
        TitledCard statusCard = new("Répartition par statut", _status, "Commandes en base");

        ConfigureGrid(_recent.Grid, true);
        ConfigureGrid(_readyList.Grid, false);
        _recent.AutoHeightRows = 5;
        _recent.MaxRows = 8;
        _readyList.AutoHeightRows = 5;
        _readyList.MaxRows = 8;
        TitledCard recentCard = new("Commandes récentes", _recent, "Double-clic pour ouvrir", flushContent: true);
        TitledCard readyCard = new("Prêtes à retirer", _readyList, "Double-clic pour ouvrir", flushContent: true);

        // Carte « Charge » compacte : 14 jours de charge atelier ; un clic ouvre l'Atelier.
        VStack loadContent = new VStack(8).Add(_load, StackSize.Fixed, 104).Add(_loadSummary);
        _loadCard = new TitledCard("Charge de l'atelier", loadContent, "Articles attendus sur les 14 prochains jours");
        FlatButton openWorkshop = new() { Text = "Ouvrir l'atelier", Kind = ButtonKind.Secondary, IconGlyph = Icons.Workshop };
        openWorkshop.Click += (_, _) => Host.Navigate(NavRegistry.Workshop);
        _loadCard.AddAction(openWorkshop);

        // Grille de 12 : KPI 4+4+4 sur deux lignes de même hauteur, 8+4 (graphique + statuts), 12 (charge atelier), 6+6 (listes).
        VStack page = new VStack()
            .Add(new GridRow().Add(_revenueToday, 4).Add(_revenueMonth, 4).Add(_outstanding, 4))
            .Add(new GridRow().Add(_active, 4).Add(_ready, 4).Add(_overdue, 4))
            .Add(new GridRow(352).Add(_chartCard, 8).Add(statusCard, 4))
            .Add(_loadCard)
            .Add(new GridRow().Add(recentCard, 6).Add(readyCard, 6));
        SetPage(_header, page, scroll: true);
    }

    public override string Title => "Tableau de bord";

    private void ConfigureGrid(ThemedGrid grid, bool recent)
    {
        grid.AddText("ticket", "TICKET", nameof(OrderRow.Ticket), 30, minWidth: 140);
        grid.AddText("client", "CLIENT", nameof(OrderRow.Client), 40, minWidth: 120);
        if (recent)
        {
            grid.AddText("total", "TOTAL", nameof(OrderRow.Total), 22, true, fixedWidth: 112);
            grid.AddPill("status", "STATUT", 28, fixedWidth: 132);
        }
        else
        {
            grid.AddText("due", "PRÉVU LE", nameof(OrderRow.Due), 22, fixedWidth: 108);
            grid.AddText("balance", "RESTE", nameof(OrderRow.Balance), 22, true, fixedWidth: 112);
        }

        grid.SetOptional("total", "due");
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
        await LoadWorkshopLoadAsync();
    }

    /// <summary>Charge atelier : une erreur ici ne doit jamais empêcher l'affichage du reste du tableau de bord.</summary>
    private async Task LoadWorkshopLoadAsync()
    {
        try
        {
            WorkshopLoadDto? load = await Api.GetWorkshopLoadAsync(14);
            if (!Alive || load is null)
            {
                return;
            }

            _load.SetData(load);
            _loadSummary.Text = WorkshopLoadChart.Summary(load, out bool attention);
            _loadSummary.ForeColor = attention ? Theme.Amber : Theme.TextMuted;
            _loadCard.Subtitle = $"Articles attendus sur 14 jours · capacité {load.DailyCapacity} par jour";
            _loadCard.RequestPageLayout();
        }
        catch (ApiException)
        {
            if (Alive)
            {
                _loadSummary.Text = "Charge de l'atelier indisponible pour le moment.";
            }
        }
    }
}
