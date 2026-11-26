using System.Diagnostics;
using Pressing.Client;
using Pressing.Core.Dtos;
using Pressing.Desktop.Services;
using Pressing.Desktop.Ui;

namespace Pressing.Desktop.Views;

/// <summary>Rapports d'activité : chiffre d'affaires, tableaux de synthèse et export CSV (Managers et Administrateurs).</summary>
public sealed class ReportsView : BaseView
{
    private enum Period
    {
        Today,
        Week,
        Month30,
        ThisMonth,
        Custom
    }

    private sealed class CategoryRow
    {
        public CategoryRow(CategoryStatDto s)
        {
            Category = s.Category;
            Quantity = s.Quantity.ToString();
            Revenue = Fmt.Money(s.Revenue);
        }

        public string Category { get; }
        public string Quantity { get; }
        public string Revenue { get; }
    }

    private sealed class MethodRow
    {
        public MethodRow(PaymentMethodStatDto s)
        {
            Method = s.Label;
            Count = s.Count.ToString();
            Amount = Fmt.Money(s.Amount);
        }

        public string Method { get; }
        public string Count { get; }
        public string Amount { get; }
    }

    private sealed class StaffRow
    {
        public StaffRow(StaffStatDto s)
        {
            Name = s.StaffName;
            Orders = s.Orders.ToString();
            Payments = Fmt.Money(s.Payments);
        }

        public string Name { get; }
        public string Orders { get; }
        public string Payments { get; }
    }

    private readonly SelectBox _period = new();
    private readonly DateBox _from = new() { Width = Theme.S(150) };
    private readonly DateBox _to = new() { Width = Theme.S(150) };
    private readonly FlowLayoutPanel _customDates = new() { AutoSize = true, WrapContents = false, Visible = false, Margin = Padding.Empty };
    private readonly Label _range = new() { AutoSize = true, ForeColor = Theme.TextMuted, Font = Theme.F(10f), Anchor = AnchorStyles.Left };
    private readonly FlatButton _export = new() { Text = "Exporter CSV", Kind = ButtonKind.Secondary, IconGlyph = Icons.Save };
    private readonly StatCard _revenue = new("Chiffre d'affaires", "—", Theme.Accent);
    private readonly StatCard _refunds = new("Remboursements", "—", Theme.Red);
    private readonly StatCard _net = new("CA net", "—", Theme.Green);
    private readonly StatCard _outstanding = new("Encours impayé", "—", Theme.Amber);
    private readonly StatCard _orders = new("Commandes", "—", Theme.Blue);
    private readonly StatCard _completed = new("Terminées", "—", Theme.Green);
    private readonly StatCard _newClients = new("Nouveaux clients", "—", Theme.Violet);
    private readonly StatCard _items = new("Articles traités", "—", Theme.Blue);
    private readonly RevenueChart _chart = new() { Dock = DockStyle.Fill };
    private readonly TitledCard _chartCard;
    private readonly GridCard _categories = new("Aucune vente sur la période.");
    private readonly GridCard _methods = new("Aucun encaissement sur la période.");
    private readonly GridCard _staff = new("Aucune activité sur la période.");
    private readonly GridCard _recent = new("Aucune commande sur la période.");
    private int _version;

    public ReportsView(IAppHost host) : base(host)
    {
        PageHeader header = new("Rapports", "Chiffre d'affaires, encaissements et activité de la boutique");
        header.AddAction("Actualiser", ButtonKind.Secondary, Icons.Refresh, async (_, _) => await RunAsync(RefreshAsync));
        header.AddControl(_export);
        _export.Click += async (_, _) => await ExportAsync();

        _period.SetChoices(new[]
        {
            new Choice<Period>(Period.Today, "Aujourd'hui"),
            new Choice<Period>(Period.Week, "7 derniers jours"),
            new Choice<Period>(Period.Month30, "30 derniers jours"),
            new Choice<Period>(Period.ThisMonth, "Ce mois-ci"),
            new Choice<Period>(Period.Custom, "Personnalisée…")
        }, Period.Month30);
        _from.Value = DateTime.Today.AddDays(-29);
        _to.Value = DateTime.Today;

        TableLayoutPanel filters = new()
        {
            AutoSize = true,
            ColumnCount = 4,
            Dock = DockStyle.Top,
            Margin = Padding.Empty,
            Padding = new Padding(0, 0, 0, Theme.S(8))
        };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(220)));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _period.Dock = DockStyle.Fill;
        _period.Margin = new Padding(0, 0, Theme.S(12), 0);
        _customDates.Controls.Add(_from);
        _customDates.Controls.Add(UiKit.Muted("  au  "));
        _customDates.Controls.Add(_to);
        _customDates.Margin = new Padding(0, 0, Theme.S(16), 0);
        foreach (Control c in _customDates.Controls)
        {
            c.Anchor = AnchorStyles.Left;
        }

        filters.Controls.Add(_period, 0, 0);
        filters.Controls.Add(_customDates, 1, 0);
        filters.Controls.Add(_range, 2, 0);

        TableLayoutPanel cards = new() { Dock = DockStyle.Top, Height = Theme.S(2 * 112 + 8), ColumnCount = 4, RowCount = 2, Margin = Padding.Empty };
        for (int i = 0; i < 4; i++)
        {
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        }

        cards.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(112)));
        cards.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(112)));
        StatCard[] all = { _revenue, _refunds, _net, _outstanding, _orders, _completed, _newClients, _items };
        for (int i = 0; i < all.Length; i++)
        {
            cards.Controls.Add(all[i], i % 4, i / 4);
        }

        Panel chartHost = new() { Height = Theme.S(230), BackColor = Color.Transparent };
        chartHost.Controls.Add(_chart);
        _chartCard = new TitledCard("Recettes journalières", chartHost, "Encaissements par jour sur la période");
        _chartCard.Dock = DockStyle.Top;
        _chartCard.Height = Theme.S(330);

        ConfigureGrids();
        TableLayoutPanel row1 = Columns(
            new TitledCard("Top catégories", _categories, "Quantités vendues et chiffre d'affaires", flushContent: true),
            new TitledCard("Modes de paiement", _methods, "Encaissements par mode", flushContent: true), 55, Theme.S(320));
        TableLayoutPanel row2 = Columns(
            new TitledCard("Personnel", _staff, "Commandes créées et encaissements", flushContent: true),
            new TitledCard("Commandes récentes", _recent, "Double-clic pour ouvrir", flushContent: true), 42, Theme.S(360));

        TableLayoutPanel page = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Margin = Padding.Empty
        };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.Controls.Add(cards, 0, 0);
        page.Controls.Add(_chartCard, 0, 1);
        page.Controls.Add(row1, 0, 2);
        page.Controls.Add(row2, 0, 3);

        ScrollPanel scroll = new();
        scroll.Controls.Add(page);
        Controls.Add(UiKit.Rows((header, false), (filters, false), (scroll, true)));

        _period.SelectedIndexChanged += async (_, _) =>
        {
            _customDates.Visible = _period.SelectedValue<Period>() == Period.Custom;
            await FilterChangedAsync();
        };
        _from.ValueChanged += async (_, _) => await FilterChangedAsync();
        _to.ValueChanged += async (_, _) => await FilterChangedAsync();
    }

    public override string Title => "Rapports";

    private static TableLayoutPanel Columns(Control left, Control right, int leftPercent, int height)
    {
        TableLayoutPanel row = new() { Dock = DockStyle.Top, Height = height, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, leftPercent));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100 - leftPercent));
        row.Controls.Add(left, 0, 0);
        row.Controls.Add(right, 1, 0);
        return row;
    }

    private void ConfigureGrids()
    {
        _categories.Grid.AddText("category", "CATÉGORIE", nameof(CategoryRow.Category), 44, minWidth: 120);
        _categories.Grid.AddText("quantity", "QUANTITÉ", nameof(CategoryRow.Quantity), 22, true, 80);
        _categories.Grid.AddText("revenue", "CHIFFRE D'AFFAIRES", nameof(CategoryRow.Revenue), 34, true, 140);

        _methods.Grid.AddText("method", "MODE", nameof(MethodRow.Method), 44, minWidth: 110);
        _methods.Grid.AddText("count", "OPÉRATIONS", nameof(MethodRow.Count), 24, true, 90);
        _methods.Grid.AddText("amount", "MONTANT", nameof(MethodRow.Amount), 32, true, 120);

        _staff.Grid.AddText("name", "COLLABORATEUR", nameof(StaffRow.Name), 40, minWidth: 110);
        _staff.Grid.AddText("orders", "COMMANDES", nameof(StaffRow.Orders), 26, true, 90);
        _staff.Grid.AddText("payments", "ENCAISSÉ", nameof(StaffRow.Payments), 34, true, 110);

        ThemedGrid recent = _recent.Grid;
        recent.AddText("ticket", "TICKET", nameof(OrderRow.Ticket), 24, minWidth: 120);
        recent.AddText("client", "CLIENT", nameof(OrderRow.Client), 30, minWidth: 100);
        recent.AddText("total", "TOTAL", nameof(OrderRow.Total), 22, true, 100);
        recent.AddPill("status", "STATUT", 28, 130);
        recent.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && recent.Rows[e.RowIndex].DataBoundItem is OrderRow row)
            {
                Host.OpenOrder(row.Id);
            }
        };
    }

    private (DateTime From, DateTime To) Range() => _period.SelectedValue<Period>() switch
    {
        Period.Today => (DateTime.Today, DateTime.Today),
        Period.Week => (DateTime.Today.AddDays(-6), DateTime.Today),
        Period.ThisMonth => (new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), DateTime.Today),
        Period.Custom => (_from.Value, _to.Value),
        _ => (DateTime.Today.AddDays(-29), DateTime.Today)
    };

    public override Task LoadAsync() => QueryAsync();

    private async Task FilterChangedAsync()
    {
        if (Alive)
        {
            await RunAsync(QueryAsync);
        }
    }

    private async Task QueryAsync()
    {
        (DateTime from, DateTime to) = Range();
        if (to < from)
        {
            _range.Text = "La date de fin précède la date de début.";
            _range.ForeColor = Theme.Red;
            return;
        }

        int version = ++_version;
        {
            ReportDto? report = await Api.GetReportAsync(from, to);
            if (!Alive || version != _version || report is null)
            {
                return;
            }

            _range.ForeColor = Theme.TextMuted;
            _range.Text = from == to ? "Le " + Fmt.Date(from) : $"Du {Fmt.Date(from)} au {Fmt.Date(to)}";
            _revenue.Set(Fmt.Money(report.Revenue), "Encaissements bruts");
            _refunds.Set(Fmt.Money(report.Refunds), "Montants remboursés");
            _net.Set(Fmt.Money(report.NetRevenue), "Après remboursements");
            _outstanding.Set(Fmt.Money(report.Outstanding), "Soldes restant à encaisser");
            _orders.Set(report.Orders.ToString(), "Créées sur la période");
            _completed.Set(report.CompletedOrders.ToString(), "Retirées en totalité");
            _newClients.Set(report.NewClients.ToString(), "Inscrits sur la période");
            _items.Set(report.ItemsProcessed.ToString(), "Articles confiés");
            _chart.SetData(report.Daily);
            _chartCard.Subtitle = $"Total de la période : {Fmt.Money(report.Daily.Sum(p => p.Revenue))}";
            _categories.Grid.SetRows(report.TopCategories.Select(c => new CategoryRow(c)).ToList());
            _methods.Grid.SetRows(report.PaymentMethods.Select(m => new MethodRow(m)).ToList());
            _staff.Grid.SetRows(report.Staff.Select(s => new StaffRow(s)).ToList());
            _recent.Grid.SetRows(report.RecentOrders.Select(o => new OrderRow(o)).ToList());
            _categories.UpdateEmpty();
            _methods.UpdateEmpty();
            _staff.UpdateEmpty();
            _recent.UpdateEmpty();
        }
    }

    private async Task ExportAsync()
    {
        (DateTime from, DateTime to) = Range();
        if (to < from)
        {
            AppDialogs.Info(this, "Période invalide", "La date de fin précède la date de début.", MessageKind.Warning);
            return;
        }

        string fileName = $"rapport-{from:yyyyMMdd}-{to:yyyyMMdd}.csv";
        string? path;
        using (SaveFileDialog dialog = new()
               {
                   Title = "Exporter le rapport",
                   FileName = fileName,
                   Filter = "Fichier CSV (*.csv)|*.csv",
                   DefaultExt = "csv",
                   AddExtension = true,
                   OverwritePrompt = true,
                   InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
               })
        {
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
            {
                return;
            }

            path = dialog.FileName;
        }

        byte[]? bytes = null;
        bool ok = await RunAsync(async () => bytes = await Api.ExportReportCsvAsync(from, to), "Export du rapport impossible");
        if (!ok || bytes is null || !Alive)
        {
            return;
        }

        try
        {
            await File.WriteAllBytesAsync(path, bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppDialogs.Error(this, "Impossible d'écrire le fichier : " + ex.Message, "Export du rapport");
            return;
        }

        Host.Toast("Rapport exporté : " + Path.GetFileName(path));
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Explorateur indisponible : le fichier est tout de même enregistré.
        }
    }
}
