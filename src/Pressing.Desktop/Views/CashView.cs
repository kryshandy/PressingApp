using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Desktop.Services;
using Pressing.Desktop.Ui;

namespace Pressing.Desktop.Views;

/// <summary>Caisse : paiements de la période, totaux par mode de paiement et total du jour.</summary>
public sealed class CashView : BaseView
{
    private enum Period
    {
        Today,
        Yesterday,
        Week,
        ThisMonth,
        Custom
    }

    private const int MaxRows = 1000;

    private readonly SelectBox _period = new();
    private readonly SelectBox _method = new();
    private readonly InputBox _search = new() { IconGlyph = Icons.Search, PlaceholderText = "Ticket, référence ou client…" };
    private readonly DateBox _from = new() { Width = Theme.S(150) };
    private readonly DateBox _to = new() { Width = Theme.S(150) };
    private readonly FlowLayoutPanel _customDates = new() { AutoSize = true, WrapContents = false, Visible = false, Margin = Padding.Empty };
    private readonly StatCard _received = new("Encaissé", "—", Theme.Green);
    private readonly StatCard _refunded = new("Remboursé", "—", Theme.Red);
    private readonly StatCard _net = new("Total net", "—", Theme.Accent);
    private readonly StatCard _count = new("Opérations", "—", Theme.Blue);
    private readonly FlowLayoutPanel _methods = new() { AutoSize = true, WrapContents = true, Margin = Padding.Empty, Dock = DockStyle.Fill };
    private readonly GridCard _card = new("Aucun paiement sur cette période.");
    private readonly Label _limit = new() { AutoSize = true, ForeColor = Theme.Amber, Font = Theme.F(9.5f), Visible = false, Margin = new Padding(0, Theme.S(6), 0, 0) };
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 450 };
    private int _version;

    public CashView(IAppHost host) : base(host)
    {
        PageHeader header = new("Caisse", "Encaissements, remboursements et totaux par mode de paiement");
        header.AddAction("Actualiser", ButtonKind.Secondary, Icons.Refresh, async (_, _) => await RunAsync(RefreshAsync));

        _period.SetChoices(new[]
        {
            new Choice<Period>(Period.Today, "Aujourd'hui"),
            new Choice<Period>(Period.Yesterday, "Hier"),
            new Choice<Period>(Period.Week, "7 derniers jours"),
            new Choice<Period>(Period.ThisMonth, "Ce mois-ci"),
            new Choice<Period>(Period.Custom, "Personnalisée…")
        }, Period.Today);
        _method.SetChoices(
            new[] { new Choice<PaymentMethod?>(null, "Tous les modes") }
                .Concat(Enum.GetValues<PaymentMethod>().Select(m => new Choice<PaymentMethod?>(m, m.Label()))),
            null);
        _from.Value = DateTime.Today.AddDays(-7);
        _to.Value = DateTime.Today;

        TableLayoutPanel filters = new()
        {
            AutoSize = true,
            ColumnCount = 4,
            Margin = Padding.Empty,
            Padding = new Padding(0, 0, 0, Theme.S(12))
        };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(210)));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(200)));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _period.Dock = DockStyle.Fill;
        _period.Margin = new Padding(0, 0, Theme.S(12), 0);
        _method.Dock = DockStyle.Fill;
        _method.Margin = new Padding(0, 0, Theme.S(12), 0);
        _search.Dock = DockStyle.Fill;
        _search.Margin = Padding.Empty;
        _customDates.Controls.Add(_from);
        _customDates.Controls.Add(UiKit.Muted("  au  "));
        _customDates.Controls.Add(_to);
        _customDates.Margin = new Padding(0, 0, Theme.S(12), 0);
        foreach (Control c in _customDates.Controls)
        {
            c.Anchor = AnchorStyles.Left;
        }

        filters.Controls.Add(_period, 0, 0);
        filters.Controls.Add(_customDates, 1, 0);
        filters.Controls.Add(_method, 2, 0);
        filters.Controls.Add(_search, 3, 0);

        TableLayoutPanel stats = new() { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Height = Theme.S(112), AutoSize = false, Margin = Padding.Empty };
        for (int i = 0; i < 4; i++)
        {
            stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        }

        stats.Controls.Add(_received, 0, 0);
        stats.Controls.Add(_refunded, 1, 0);
        stats.Controls.Add(_net, 2, 0);
        stats.Controls.Add(_count, 3, 0);
        stats.Margin = new Padding(Theme.S(-6), 0, Theme.S(-6), 0);
        Panel statsWrap = new() { Height = Theme.S(116), Padding = new Padding(Theme.S(0)), Margin = Padding.Empty };
        statsWrap.Controls.Add(stats);
        stats.Dock = DockStyle.Fill;

        _methods.Padding = new Padding(Theme.S(6), Theme.S(6), 0, Theme.S(6));

        ThemedGrid grid = _card.Grid;
        grid.AddText("date", "DATE", nameof(PaymentRow.Date), 16, minWidth: 130);
        grid.AddText("ticket", "TICKET", nameof(PaymentRow.Ticket), 17, minWidth: 130);
        grid.AddText("type", "TYPE", nameof(PaymentRow.Type), 14, minWidth: 110);
        grid.AddText("method", "MODE", nameof(PaymentRow.Method), 14, minWidth: 100);
        grid.AddText("reference", "RÉFÉRENCE", nameof(PaymentRow.Reference), 17, minWidth: 100);
        grid.AddText("staff", "ENCAISSÉ PAR", nameof(PaymentRow.Staff), 14, minWidth: 100);
        grid.AddText("amount", "MONTANT", nameof(PaymentRow.Amount), 14, true, 110);
        grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && grid.Rows[e.RowIndex].DataBoundItem is PaymentRow row)
            {
                Host.OpenOrder(row.Source.OrderId);
            }
        };

        Controls.Add(UiKit.Rows((header, false), (filters, false), (statsWrap, false), (_methods, false), (_card, true), (_limit, false)));

        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await RunAsync(QueryAsync);
        };
        _search.TextChanged += (_, _) =>
        {
            _debounce.Stop();
            _debounce.Start();
        };
        _period.SelectedIndexChanged += async (_, _) =>
        {
            _customDates.Visible = _period.SelectedValue<Period>() == Period.Custom;
            await FilterChangedAsync();
        };
        _method.SelectedIndexChanged += async (_, _) => await FilterChangedAsync();
        _from.ValueChanged += async (_, _) => await FilterChangedAsync();
        _to.ValueChanged += async (_, _) => await FilterChangedAsync();
    }

    public override string Title => "Caisse";

    public override Task LoadAsync() => QueryAsync();

    public override Task ReactivateAsync() => QueryAsync();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _debounce.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task FilterChangedAsync()
    {
        if (Alive)
        {
            await RunAsync(QueryAsync);
        }
    }

    private (DateTime From, DateTime To) Range() => _period.SelectedValue<Period>() switch
    {
        Period.Yesterday => (DateTime.Today.AddDays(-1), DateTime.Today.AddDays(-1)),
        Period.Week => (DateTime.Today.AddDays(-6), DateTime.Today),
        Period.ThisMonth => (new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), DateTime.Today),
        Period.Custom => (_from.Value, _to.Value),
        _ => (DateTime.Today, DateTime.Today)
    };

    private async Task QueryAsync()
    {
        int version = ++_version;
        (DateTime from, DateTime to) = Range();
        PagedResult<PaymentDto>? result = await Api.GetPaymentsAsync(
            from, to, _method.SelectedValue<PaymentMethod?>(),
            string.IsNullOrWhiteSpace(_search.Text) ? null : _search.Text.Trim(), 1, MaxRows);
        if (!Alive || version != _version || result is null)
        {
            return;
        }

        List<PaymentDto> items = result.Items.ToList();
        decimal received = items.Where(p => p.Type == PaymentType.Payment).Sum(p => p.Amount);
        decimal refunded = items.Where(p => p.Type == PaymentType.Refund).Sum(p => p.Amount);
        _received.Set(Fmt.Money(received), $"{items.Count(p => p.Type == PaymentType.Payment)} encaissement(s)");
        _refunded.Set(Fmt.Money(refunded), $"{items.Count(p => p.Type == PaymentType.Refund)} remboursement(s)");
        _net.Set(Fmt.Money(received - refunded), from == to ? Fmt.Date(from) : $"{Fmt.Date(from)} → {Fmt.Date(to)}");
        _count.Set(result.Total.ToString(), "opération(s) sur la période");

        _methods.SuspendLayout();
        _methods.Controls.Clear();
        foreach (PaymentMethod method in Enum.GetValues<PaymentMethod>())
        {
            decimal amount = items.Where(p => p.Method == method).Sum(p => p.Type == PaymentType.Refund ? -p.Amount : p.Amount);
            int count = items.Count(p => p.Method == method);
            _methods.Controls.Add(MethodChip(method.Label(), amount, count));
        }

        _methods.ResumeLayout(true);
        _card.Grid.SetRows(items.Select(p => new PaymentRow(p)).ToList());
        _card.UpdateEmpty();
        _limit.Visible = result.Total > MaxRows;
        _limit.Text = $"Seuls les {MaxRows} paiements les plus récents sont affichés ({result.Total} au total) : affinez la période.";
    }

    private static Control MethodChip(string label, decimal amount, int count)
    {
        RoundedPanel chip = new()
        {
            Width = Theme.S(200),
            Height = Theme.S(60),
            Padding = new Padding(Theme.S(14), Theme.S(8), Theme.S(10), Theme.S(6)),
            Margin = new Padding(0, 0, Theme.S(10), Theme.S(6)),
            BackColor = Theme.SurfaceAlt
        };
        Label name = new() { Text = label.ToUpperInvariant() + (count > 0 ? $"  ·  {count}" : string.Empty), Dock = DockStyle.Top, Height = Theme.S(18), Font = Theme.F(8.5f, FontStyle.Bold), ForeColor = Theme.TextMuted };
        Label value = new() { Text = Fmt.Money(amount), Dock = DockStyle.Fill, Font = Theme.F(12f, FontStyle.Bold), ForeColor = amount == 0 ? Theme.TextFaint : Theme.Text, AutoEllipsis = true };
        chip.Controls.Add(value);
        chip.Controls.Add(name);
        return chip;
    }
}
