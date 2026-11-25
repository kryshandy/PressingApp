using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Desktop.Services;
using Pressing.Desktop.Ui;

namespace Pressing.Desktop.Views;

/// <summary>Filtre transmis à l'écran des commandes lors d'une navigation.</summary>
public sealed class OrdersFilter
{
    public string? Search { get; init; }
    public OrderStatus? Status { get; init; }
    public bool OnlyReady { get; init; }
    public bool OnlyOutstanding { get; init; }
    public int? ClientId { get; init; }
}

/// <summary>Liste filtrable et paginée des commandes.</summary>
public sealed class OrdersView : BaseView
{
    private enum Period
    {
        All,
        Today,
        Week,
        Month,
        ThisMonth,
        Custom
    }

    private const int PageSize = 25;

    private readonly InputBox _search = new() { IconGlyph = Icons.Search, PlaceholderText = "Ticket, nom ou téléphone du client…" };
    private readonly SelectBox _status = new();
    private readonly SelectBox _period = new();
    private readonly DateBox _from = new() { Width = Theme.S(150) };
    private readonly DateBox _to = new() { Width = Theme.S(150) };
    private readonly ToggleBox _onlyReady = new() { Text = "Prêtes uniquement" };
    private readonly ToggleBox _onlyUnpaid = new() { Text = "Impayées uniquement" };
    private readonly FlowLayoutPanel _customDates = new() { AutoSize = true, WrapContents = false, Visible = false, Margin = new Padding(0, 0, Theme.S(16), 0) };
    private readonly GridCard _card = new("Aucune commande ne correspond à ces critères.");
    private readonly PagerBar _pager = new();
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 450 };
    private int? _clientId;
    private int _page = 1;
    private int _version;
    private bool _suspend;

    public OrdersView(IAppHost host) : base(host)
    {
        PageHeader header = new("Commandes", "Suivi de tous les dépôts : statuts, paiements et retraits");
        header.AddAction("Actualiser", ButtonKind.Secondary, Icons.Refresh, async (_, _) => await RunAsync(RefreshAsync));
        header.AddAction("Nouveau dépôt", ButtonKind.Primary, Icons.Add, (_, _) => Host.StartNewOrder());

        _status.SetChoices(
            new[] { new Choice<OrderStatus?>(null, "Tous les statuts") }
                .Concat(Enum.GetValues<OrderStatus>().Select(s => new Choice<OrderStatus?>(s, s.Label()))),
            null);
        _period.SetChoices(new[]
        {
            new Choice<Period>(Period.All, "Toute période"),
            new Choice<Period>(Period.Today, "Aujourd'hui"),
            new Choice<Period>(Period.Week, "7 derniers jours"),
            new Choice<Period>(Period.Month, "30 derniers jours"),
            new Choice<Period>(Period.ThisMonth, "Ce mois-ci"),
            new Choice<Period>(Period.Custom, "Personnalisée…")
        }, Period.All);
        _from.Value = DateTime.Today.AddDays(-30);
        _to.Value = DateTime.Today;

        TableLayoutPanel filters = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(0, 0, 0, Theme.S(12))
        };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(210)));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(210)));
        _search.Margin = new Padding(0, 0, Theme.S(12), Theme.S(10));
        _status.Margin = new Padding(0, 0, Theme.S(12), Theme.S(10));
        _period.Margin = new Padding(0, 0, 0, Theme.S(10));
        _search.Dock = _status.Dock = _period.Dock = DockStyle.Fill;
        filters.Controls.Add(_search, 0, 0);
        filters.Controls.Add(_status, 1, 0);
        filters.Controls.Add(_period, 2, 0);

        _customDates.Controls.Add(UiKit.Muted("Du  "));
        _customDates.Controls.Add(_from);
        _customDates.Controls.Add(UiKit.Muted("   au  "));
        _customDates.Controls.Add(_to);
        foreach (Control c in _customDates.Controls)
        {
            c.Anchor = AnchorStyles.Left;
        }

        FlowLayoutPanel toggles = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty, Dock = DockStyle.Fill };
        _onlyReady.Margin = new Padding(0, 0, Theme.S(24), 0);
        _onlyUnpaid.Margin = new Padding(0, 0, Theme.S(24), 0);
        toggles.Controls.Add(_customDates);
        toggles.Controls.Add(_onlyReady);
        toggles.Controls.Add(_onlyUnpaid);
        filters.Controls.Add(toggles, 0, 1);
        filters.SetColumnSpan(toggles, 3);

        ThemedGrid grid = _card.Grid;
        grid.AddText("ticket", "TICKET", nameof(OrderRow.Ticket), 18, minWidth: 150);
        grid.AddText("client", "CLIENT", nameof(OrderRow.Client), 22, minWidth: 110);
        grid.AddText("due", "PRÉVUE LE", nameof(OrderRow.Due), 12, minWidth: 100);
        grid.AddText("items", "ARTICLES", nameof(OrderRow.Items), 16, minWidth: 90);
        grid.AddText("total", "TOTAL", nameof(OrderRow.Total), 12, true, 96);
        grid.AddText("balance", "RESTE", nameof(OrderRow.Balance), 12, true, 96);
        grid.AddPill("status", "STATUT", 15, 120);
        grid.AddPill("express", "", 9, 86);
        grid.CellDoubleClick += (_, e) => OpenSelected(e.RowIndex);
        grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                OpenSelected(grid.CurrentRow?.Index ?? -1);
                e.Handled = true;
            }
        };

        Controls.Add(UiKit.Rows((header, false), (filters, false), (_card, true), (_pager, false)));

        _pager.PageRequested += async (_, page) =>
        {
            _page = page;
            await RunAsync(QueryAsync);
        };
        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            _page = 1;
            await RunAsync(QueryAsync);
        };
        _search.TextChanged += (_, _) =>
        {
            if (!_suspend)
            {
                _debounce.Stop();
                _debounce.Start();
            }
        };
        _search.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                _debounce.Stop();
                _page = 1;
                await RunAsync(QueryAsync);
            }
        };
        _status.SelectedIndexChanged += async (_, _) => await FilterChangedAsync();
        _period.SelectedIndexChanged += async (_, _) =>
        {
            _customDates.Visible = _period.SelectedValue<Period>() == Period.Custom;
            await FilterChangedAsync();
        };
        _from.ValueChanged += async (_, _) => await FilterChangedAsync();
        _to.ValueChanged += async (_, _) => await FilterChangedAsync();
        _onlyReady.CheckedChanged += async (_, _) => await FilterChangedAsync();
        _onlyUnpaid.CheckedChanged += async (_, _) => await FilterChangedAsync();
    }

    public override string Title => "Commandes";

    public override void ApplyParameter(object? parameter)
    {
        if (parameter is not OrdersFilter filter)
        {
            return;
        }

        _suspend = true;
        _search.Text = filter.Search ?? string.Empty;
        _status.SetChoices(
            new[] { new Choice<OrderStatus?>(null, "Tous les statuts") }
                .Concat(Enum.GetValues<OrderStatus>().Select(s => new Choice<OrderStatus?>(s, s.Label()))),
            filter.Status);
        _onlyReady.Checked = filter.OnlyReady;
        _onlyUnpaid.Checked = filter.OnlyOutstanding;
        _clientId = filter.ClientId;
        _suspend = false;
    }

    private async Task FilterChangedAsync()
    {
        if (_suspend || !Alive)
        {
            return;
        }

        _page = 1;
        await RunAsync(QueryAsync);
    }

    public override Task LoadAsync() => QueryAsync();

    private void OpenSelected(int rowIndex)
    {
        ThemedGrid grid = _card.Grid;
        if (rowIndex >= 0 && rowIndex < grid.Rows.Count && grid.Rows[rowIndex].DataBoundItem is OrderRow row)
        {
            Host.OpenOrder(row.Id);
        }
    }

    private (DateTime? From, DateTime? To) Range() => _period.SelectedValue<Period>() switch
    {
        Period.Today => (DateTime.Today, DateTime.Today),
        Period.Week => (DateTime.Today.AddDays(-6), DateTime.Today),
        Period.Month => (DateTime.Today.AddDays(-29), DateTime.Today),
        Period.ThisMonth => (new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), DateTime.Today),
        Period.Custom => (_from.Value, _to.Value),
        _ => (null, null)
    };

    private async Task QueryAsync()
    {
        int version = ++_version;
        (DateTime? from, DateTime? to) = Range();
        PagedResult<OrderSummaryDto>? result = await Api.SearchOrdersAsync(
            _status.SelectedValue<OrderStatus?>(),
            string.IsNullOrWhiteSpace(_search.Text) ? null : _search.Text.Trim(),
            from, to, _clientId, _onlyReady.Checked, _onlyUnpaid.Checked, true, _page, PageSize);
        if (!Alive || version != _version || result is null)
        {
            return;
        }

        _card.Grid.SetRows(result.Items.Select(o => new OrderRow(o)).ToList());
        _card.UpdateEmpty();
        _pager.Update(result.Page, result.TotalPages, result.Total, "commande");
    }
}
