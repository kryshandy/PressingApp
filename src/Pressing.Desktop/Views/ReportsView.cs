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
    private readonly DateBox _from = new();
    private readonly DateBox _to = new();
    private readonly GridRow _customDates = new() { Visible = false };
    private readonly TextLabel _range = new() { ForeColor = Theme.TextMuted, Font = Theme.F(10f), TextAlign = ContentAlignment.MiddleRight };
    private readonly FlatButton _export = new() { Text = "Exporter CSV", Kind = ButtonKind.Secondary, IconGlyph = Icons.Save };
    private readonly StatCard _revenue = new("Chiffre d'affaires", "—", Theme.Accent);
    private readonly StatCard _refunds = new("Remboursements", "—", Theme.Red);
    private readonly StatCard _net = new("CA net", "—", Theme.Green);
    private readonly StatCard _outstanding = new("Encours impayé", "—", Theme.Amber);
    private readonly StatCard _orders = new("Commandes", "—", Theme.Blue);
    private readonly StatCard _completed = new("Terminées", "—", Theme.Green);
    private readonly StatCard _newClients = new("Nouveaux clients", "—", Theme.Violet);
    private readonly StatCard _items = new("Articles traités", "—", Theme.Blue);
    private readonly RevenueChart _chart = new();
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

        // Barre de filtres : période (3) · dates personnalisées (6) · intervalle affiché (3).
        _customDates.Add(new InlineField("Du", _from, labelPercent: 18), 6).Add(new InlineField("au", _to, labelPercent: 18), 6);
        GridRow filters = new GridRow(40).Add(_period, 3).Add(new Padded(_customDates, Padding.Empty), 6).Add(_range, 3);

        _chartCard = new TitledCard("Recettes journalières", _chart, "Encaissements par jour sur la période");

        ConfigureGrids();
        foreach (GridCard g in new[] { _categories, _methods, _staff, _recent })
        {
            g.AutoHeightRows = 5;
            g.MaxRows = 8;
        }

        // KPI 3+3+3+3 sur deux lignes, graphique pleine largeur, puis 6+6 et 6+6.
        VStack page = new VStack()
            .Add(new GridRow().Add(_revenue, 3).Add(_refunds, 3).Add(_net, 3).Add(_outstanding, 3))
            .Add(new GridRow().Add(_orders, 3).Add(_completed, 3).Add(_newClients, 3).Add(_items, 3))
            .Add(_chartCard, StackSize.Fixed, 340)
            .Add(new GridRow()
                .Add(new TitledCard("Top catégories", _categories, "Quantités vendues et chiffre d'affaires", flushContent: true), 6)
                .Add(new TitledCard("Modes de paiement", _methods, "Encaissements par mode", flushContent: true), 6))
            .Add(new GridRow()
                .Add(new TitledCard("Personnel", _staff, "Commandes créées et encaissements", flushContent: true), 6)
                .Add(new TitledCard("Commandes récentes", _recent, "Double-clic pour ouvrir", flushContent: true), 6));
        SetPage(header, page, scroll: true, toolbar: filters);

        _period.SelectedIndexChanged += async (_, _) =>
        {
            _customDates.Visible = _period.SelectedValue<Period>() == Period.Custom;
            await FilterChangedAsync();
        };
        _from.ValueChanged += async (_, _) => await FilterChangedAsync();
        _to.ValueChanged += async (_, _) => await FilterChangedAsync();
    }

    public override string Title => "Rapports";

    private void ConfigureGrids()
    {
        _categories.Grid.AddText("category", "CATÉGORIE", nameof(CategoryRow.Category), 100, minWidth: 110);
        _categories.Grid.AddText("quantity", "QUANTITÉ", nameof(CategoryRow.Quantity), right: true, fixedWidth: 100);
        _categories.Grid.AddText("revenue", "CHIFFRE D'AFFAIRES", nameof(CategoryRow.Revenue), right: true, fixedWidth: 160);

        _methods.Grid.AddText("method", "MODE", nameof(MethodRow.Method), 100, minWidth: 110);
        _methods.Grid.AddText("count", "OPÉRATIONS", nameof(MethodRow.Count), right: true, fixedWidth: 116);
        _methods.Grid.AddText("amount", "MONTANT", nameof(MethodRow.Amount), right: true, fixedWidth: 132);

        _staff.Grid.AddText("name", "COLLABORATEUR", nameof(StaffRow.Name), 100, minWidth: 120);
        _staff.Grid.AddText("orders", "COMMANDES", nameof(StaffRow.Orders), right: true, fixedWidth: 116);
        _staff.Grid.AddText("payments", "ENCAISSÉ", nameof(StaffRow.Payments), right: true, fixedWidth: 132);

        ThemedGrid recent = _recent.Grid;
        recent.AddText("ticket", "TICKET", nameof(OrderRow.Ticket), 50, minWidth: 140);
        recent.AddText("client", "CLIENT", nameof(OrderRow.Client), 50, minWidth: 90);
        recent.AddText("total", "TOTAL", nameof(OrderRow.Total), right: true, fixedWidth: 108);
        recent.AddPill("status", "STATUT", fixedWidth: 132);
        recent.SetOptional("total");
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
