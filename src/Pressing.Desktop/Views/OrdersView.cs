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
    private readonly DateBox _from = new();
    private readonly DateBox _to = new();
    private readonly ToggleBox _onlyReady = new() { Text = "Prêtes uniquement" };
    private readonly ToggleBox _onlyUnpaid = new() { Text = "Impayées uniquement" };
    private readonly GridRow _customDates = new() { Visible = false };
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

        // Barre de filtres sur une ligne : recherche (6 colonnes) puis statut et période (3 + 3).
        GridRow filters = new GridRow().Add(_search, 6).Add(_status, 3).Add(_period, 3);
        _customDates.Add(new Control(), 6).Add(new InlineField("Du", _from, labelPercent: 18), 3).Add(new InlineField("au", _to, labelPercent: 18), 3);
        VStack toolbar = new VStack(12).Add(filters).Add(_customDates);

        ThemedGrid grid = _card.Grid;
        grid.AddText("ticket", "TICKET", nameof(OrderRow.Ticket), fixedWidth: 164);
        grid.AddText("client", "CLIENT", nameof(OrderRow.Client), 58, minWidth: 130);
        grid.AddText("due", "PRÉVUE LE", nameof(OrderRow.Due), fixedWidth: 108);
        grid.AddText("items", "ARTICLES", nameof(OrderRow.Items), 42, minWidth: 110);
        grid.AddText("total", "TOTAL", nameof(OrderRow.Total), right: true, fixedWidth: 112);
        grid.AddText("balance", "RESTE", nameof(OrderRow.Balance), right: true, fixedWidth: 112);
        grid.AddPill("status", "STATUT", fixedWidth: 140);
        grid.AddPill("express", "", fixedWidth: 100);
        grid.SetOptional("express", "items", "due");
        grid.CellDoubleClick += (_, e) => OpenSelected(e.RowIndex);
        grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                OpenSelected(grid.CurrentRow?.Index ?? -1);
                e.Handled = true;
            }
        };

        TitledCard list = new("Résultats", _card, "Double-clic ou Entrée pour ouvrir une commande", flushContent: true);
        list.AddAction(_onlyReady);
        list.AddAction(_onlyUnpaid);
        SetPage(header, new VStack(16).Add(list, StackSize.Fill).Add(_pager), toolbar: toolbar);

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
