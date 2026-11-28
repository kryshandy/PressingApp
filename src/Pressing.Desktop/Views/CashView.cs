using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Desktop.Dialogs;
using Pressing.Desktop.Services;
using Pressing.Desktop.Ui;

namespace Pressing.Desktop.Views;

/// <summary>
/// Caisse : paiements de la période, totaux par mode de paiement et total du jour ; clôture de caisse journalière
/// (« Z de caisse ») et, pour un Manager ou un Administrateur, historique des clôtures avec validation et PDF.
/// </summary>
public sealed class CashView : BaseView
{
    /// <summary>Ligne de l'historique des clôtures de caisse.</summary>
    private sealed class ClosingRow : IPillRow, ITintRow
    {
        public ClosingRow(CashClosingDto c)
        {
            Source = c;
            Day = Fmt.Date(c.Date);
            Float = Fmt.Money(c.OpeningFloat);
            Expected = Fmt.Money(c.ExpectedCashInDrawer);
            Counted = Fmt.Money(c.CountedCash);
            Variance = c.Variance == 0 ? "Aucun" : (c.Variance > 0 ? "+ " : "− ") + Fmt.Money(Math.Abs(c.Variance));
            CreatedBy = c.CreatedBy;
            ValidatedBy = c.ValidatedBy is null ? "—" : $"{c.ValidatedBy} · {Fmt.Date(c.ValidatedAt ?? c.Date)}";
            Comment = string.IsNullOrWhiteSpace(c.Comment) ? "—" : c.Comment!;
        }

        public CashClosingDto Source { get; }
        public string Day { get; }
        public string Float { get; }
        public string Expected { get; }
        public string Counted { get; }
        public string Variance { get; }
        public string CreatedBy { get; }
        public string ValidatedBy { get; }
        public string Comment { get; }

        public (string Text, Color Color)? GetPill(string columnName) => columnName == "state"
            ? (Source.Status.Label(), Source.Status == CashClosingStatus.Validated ? Theme.Green : Theme.Amber)
            : null;

        public Color? GetTint(string columnName) => columnName switch
        {
            "variance" when Source.Variance < 0 => Theme.Red,
            "variance" when Source.Variance > 0 => Theme.Amber,
            "variance" => Theme.Green,
            "day" => Theme.Accent,
            _ => null
        };
    }

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
    private readonly InputBox _search = new() { IconGlyph = Icons.Search, PlaceholderText = "Filtrer les paiements : ticket, référence ou client…" };
    private readonly DateBox _from = new();
    private readonly DateBox _to = new();
    private readonly GridRow _customDates = new() { Visible = false };
    private readonly StatCard _received = new("Encaissé", "—", Theme.Green);
    private readonly StatCard _refunded = new("Remboursé", "—", Theme.Red);
    private readonly StatCard _net = new("Total net", "—", Theme.Accent);
    private readonly StatCard _count = new("Opérations", "—", Theme.Blue);
    private readonly UniformRow _methods = new();
    private readonly GridCard _card = new("Aucun paiement sur cette période.");
    private readonly GridCard _closingsCard = new("Aucune clôture de caisse enregistrée.") { AutoHeightRows = 3, MaxRows = 8 };
    private readonly FlatButton _validate = new() { Text = "Valider", Kind = ButtonKind.Secondary, IconGlyph = Icons.Check, Enabled = false };
    private readonly FlatButton _pdf = new() { Text = "PDF", Kind = ButtonKind.Secondary, IconGlyph = Icons.Pdf, Enabled = false };
    private readonly TextLabel _limit = new() { ForeColor = Theme.Amber, Font = Theme.F(9.5f), Visible = false };
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 450 };
    private int _version;

    public CashView(IAppHost host) : base(host)
    {
        PageHeader header = new("Caisse", "Encaissements, remboursements et totaux par mode de paiement");
        header.AddAction("Actualiser", ButtonKind.Secondary, Icons.Refresh, async (_, _) => await RunAsync(RefreshAsync));
        header.AddAction("Clôturer la journée", ButtonKind.Primary, Icons.Check, async (_, _) => await CloseDayAsync());

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

        GridRow filters = new GridRow().Add(_search, 6).Add(_period, 3).Add(_method, 3);
        _customDates.Add(new Control(), 6).Add(new InlineField("Du", _from, labelPercent: 18), 3).Add(new InlineField("au", _to, labelPercent: 18), 3);
        VStack toolbar = new VStack(12).Add(filters).Add(_customDates);

        ThemedGrid grid = _card.Grid;
        grid.AddText("date", "DATE", nameof(PaymentRow.Date), fixedWidth: 152);
        grid.AddText("ticket", "TICKET", nameof(PaymentRow.Ticket), fixedWidth: 164);
        grid.AddText("type", "TYPE", nameof(PaymentRow.Type), 30, minWidth: 138);
        grid.AddText("method", "MODE", nameof(PaymentRow.Method), 30, minWidth: 110);
        grid.AddText("reference", "RÉFÉRENCE", nameof(PaymentRow.Reference), 40, minWidth: 100);
        grid.AddText("staff", "ENCAISSÉ PAR", nameof(PaymentRow.Staff), 30, minWidth: 110);
        grid.AddText("amount", "MONTANT", nameof(PaymentRow.Amount), right: true, fixedWidth: 128);
        grid.SetOptional("reference", "staff");
        grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && grid.Rows[e.RowIndex].DataBoundItem is PaymentRow row)
            {
                Host.OpenOrder(row.Source.OrderId);
            }
        };

        TitledCard methods = new("Par mode de paiement", _methods, autoHeight: true);
        TitledCard list = new("Opérations", _card, "Double-clic pour ouvrir la commande", flushContent: true) { MinimumSize = new Size(0, Theme.S(360)) };
        VStack page = new VStack()
            .Add(new GridRow().Add(_received, 3).Add(_refunded, 3).Add(_net, 3).Add(_count, 3))
            .Add(methods)
            .Add(list, StackSize.Fill)
            .Add(_limit);
        if (Session.IsManagerOrAdmin)
        {
            ThemedGrid closings = _closingsCard.Grid;
            closings.AddText("day", "JOUR", nameof(ClosingRow.Day), fixedWidth: 112);
            closings.AddPill("state", "STATUT", fixedWidth: 112);
            closings.AddText("float", "FOND", nameof(ClosingRow.Float), right: true, fixedWidth: 120);
            closings.AddText("expected", "ATTENDU EN CAISSE", nameof(ClosingRow.Expected), right: true, fixedWidth: 152);
            closings.AddText("counted", "COMPTÉ", nameof(ClosingRow.Counted), right: true, fixedWidth: 120);
            closings.AddText("variance", "ÉCART", nameof(ClosingRow.Variance), right: true, fixedWidth: 120);
            closings.AddText("created", "ÉTABLIE PAR", nameof(ClosingRow.CreatedBy), 30, minWidth: 110);
            closings.AddText("validated", "VALIDÉE PAR", nameof(ClosingRow.ValidatedBy), 40, minWidth: 130);
            closings.AddText("comment", "COMMENTAIRE", nameof(ClosingRow.Comment), 50, minWidth: 120);
            closings.SetOptional("comment", "created");
            closings.SelectionChanged += (_, _) => UpdateClosingButtons();
            closings.CellDoubleClick += async (_, e) =>
            {
                if (e.RowIndex >= 0 && closings.Rows[e.RowIndex].DataBoundItem is ClosingRow)
                {
                    await OpenClosingPdfAsync();
                }
            };
            _validate.Click += async (_, _) => await ValidateClosingAsync();
            _pdf.Click += async (_, _) => await OpenClosingPdfAsync();

            TitledCard history = new("Clôtures de caisse", _closingsCard, "Une clôture validée n'est plus modifiable", flushContent: true, autoHeight: true);
            history.AddAction(_pdf);
            history.AddAction(_validate);
            page.Add(history);
        }

        SetPage(header, page, scroll: true, toolbar: toolbar);

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

    public override async Task LoadAsync()
    {
        await QueryAsync();
        await LoadClosingsAsync();
    }

    public override Task ReactivateAsync() => LoadAsync();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _debounce.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>Jour à clôturer : le jour affiché s'il s'agit d'un jour passé unique, sinon aujourd'hui.</summary>
    private DateTime ClosingDay()
    {
        (DateTime from, DateTime to) = Range();
        return from.Date == to.Date && from.Date < DateTime.Today ? from.Date : DateTime.Today;
    }

    private async Task CloseDayAsync()
    {
        DateTime day = ClosingDay();
        CashClosingPreviewDto? preview = null;
        CashClosingDto? draft = null;
        bool loaded = await RunAsync(async () =>
        {
            preview = await Api.PreviewCashClosingAsync(day);
            if (preview?.ExistingStatus == CashClosingStatus.Draft && preview.ExistingClosingId is int id)
            {
                draft = await Api.GetCashClosingAsync(id);
            }
        }, "Clôture impossible");
        if (!loaded || preview is null || !Alive)
        {
            return;
        }

        if (preview.ExistingStatus == CashClosingStatus.Validated)
        {
            AppDialogs.Info(this, "Caisse déjà clôturée",
                $"La caisse du {Fmt.Date(day)} est déjà clôturée et validée : elle n'est plus modifiable.", MessageKind.Info);
            return;
        }

        using CashClosingDialog dialog = new(Api, preview, draft);
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Host.Toast(Session.IsManagerOrAdmin
                ? "Clôture enregistrée : elle peut maintenant être validée."
                : "Clôture enregistrée : un manager doit la valider.");
            await RunAsync(LoadClosingsAsync);
        }
    }

    private async Task LoadClosingsAsync()
    {
        if (!Session.IsManagerOrAdmin)
        {
            return;
        }

        List<CashClosingDto>? closings = await Api.GetCashClosingsAsync();
        if (!Alive || closings is null)
        {
            return;
        }

        _closingsCard.Grid.SetRows(closings.Select(c => new ClosingRow(c)).ToList());
        _closingsCard.UpdateEmpty();
        UpdateClosingButtons();
        _closingsCard.RequestPageLayout();
    }

    private void UpdateClosingButtons()
    {
        ClosingRow? row = _closingsCard.Grid.SelectedItem<ClosingRow>();
        _pdf.Enabled = row is not null;
        _validate.Enabled = row is { Source.Status: CashClosingStatus.Draft };
    }

    private async Task ValidateClosingAsync()
    {
        if (_closingsCard.Grid.SelectedItem<ClosingRow>() is not { Source.Status: CashClosingStatus.Draft } row)
        {
            return;
        }

        string variance = row.Source.Variance == 0 ? "sans écart" : $"écart de {row.Variance}";
        if (!AppDialogs.Confirm(this, "Valider la clôture",
                $"Valider la clôture du {row.Day} ({variance}) ? Une fois validée, elle ne sera plus modifiable.", "Valider"))
        {
            return;
        }

        bool ok = await RunAsync(async () => await Api.ValidateCashClosingAsync(row.Source.Id), "Validation impossible");
        if (ok && Alive)
        {
            Host.Toast("Clôture validée.");
            await RunAsync(LoadClosingsAsync);
        }
    }

    private async Task OpenClosingPdfAsync()
    {
        if (_closingsCard.Grid.SelectedItem<ClosingRow>() is not { } row)
        {
            return;
        }

        await RunAsync(async () => await CashClosingFiles.OpenAsync(Api, row.Source.Id, row.Source.Date), "PDF indisponible");
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
        _methods.RequestPageLayout();
        _card.Grid.SetRows(items.Select(p => new PaymentRow(p)).ToList());
        _card.UpdateEmpty();
        _limit.Visible = result.Total > MaxRows;
        _limit.Text = $"Seuls les {MaxRows} paiements les plus récents sont affichés ({result.Total} au total) : affinez la période.";
    }

    private static Control MethodChip(string label, decimal amount, int count) => new MethodTile(label, amount, count);

    /// <summary>Total d'un mode de paiement (libellé, nombre d'opérations, montant).</summary>
    private sealed class MethodTile : RoundedPanel
    {
        private readonly string _label;
        private readonly decimal _amount;
        private readonly int _count;

        public MethodTile(string label, decimal amount, int count)
        {
            _label = label.ToUpperInvariant();
            _amount = amount;
            _count = count;
            BackColor = Theme.SurfaceAlt;
            DrawBorder = false;
            Radius = 10;
        }

        public override Size GetPreferredSize(Size proposedSize) => new(proposedSize.Width, Theme.S(68));

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            int pad = Theme.S(16);
            int w = Math.Max(10, Width - 2 * pad);
            string caption = _label + (_count > 0 ? $"  ·  {_count}" : string.Empty);
            Font value = Theme.Semi(12f);
            int y = (Height - Theme.FieldLabel.Height - Theme.S(4) - value.Height) / 2;
            TextRenderer.DrawText(e.Graphics, caption, Theme.FieldLabel, new Rectangle(pad, y, w, Theme.FieldLabel.Height), Theme.TextMuted, flags);
            y += Theme.FieldLabel.Height + Theme.S(4);
            TextRenderer.DrawText(e.Graphics, Fmt.Money(_amount), value, new Rectangle(pad, y, w, value.Height),
                _amount == 0 ? Theme.TextFaint : Theme.Text, flags);
        }
    }
}
