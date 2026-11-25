using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Desktop.Dialogs;
using Pressing.Desktop.Services;
using Pressing.Desktop.Ui;

namespace Pressing.Desktop.Views;

/// <summary>Grille tarifaire (catégorie × service) ; édition réservée aux Managers et Administrateurs.</summary>
public sealed class TariffsView : BaseView
{
    private sealed class PriceRow : IPillRow, ITintRow
    {
        public PriceRow(CategoryDto category, CategoryPriceDto price)
        {
            Category = category;
            Price = price;
            CategoryName = category.Name;
            Service = price.ServiceLabel;
            Amount = Fmt.Money(price.Price);
            Express = price.ExpressSupplement > 0 ? "+ " + Fmt.Money(price.ExpressSupplement) : "—";
            Days = price.EstimatedDays + (price.EstimatedDays > 1 ? " jours" : " jour");
        }

        public CategoryDto Category { get; }
        public CategoryPriceDto Price { get; }
        public string CategoryName { get; }
        public string Service { get; }
        public string Amount { get; }
        public string Express { get; }
        public string Days { get; }

        public (string Text, Color Color)? GetPill(string columnName) =>
            columnName == "state" && (!Price.IsActive || !Category.IsActive) ? ("Inactif", Theme.Slate) : null;

        public Color? GetTint(string columnName) =>
            !Price.IsActive || !Category.IsActive ? Theme.TextFaint : columnName == "amount" ? Theme.Accent : null;
    }

    private readonly GridCard _card = new("Aucun tarif défini.");
    private readonly ToggleBox _inactive = new() { Text = "Afficher les tarifs inactifs" };
    private readonly FlatButton _edit = new() { Text = "Modifier la catégorie", Kind = ButtonKind.Secondary, IconGlyph = Icons.Edit, Enabled = false };
    private readonly Label _hint = new() { AutoSize = true, ForeColor = Theme.TextMuted, Font = Theme.F(9.5f), Margin = new Padding(0, Theme.S(8), 0, 0) };

    public TariffsView(IAppHost host) : base(host)
    {
        PageHeader header = new("Tarifs", "Catalogue des prestations : prix, supplément express et délai");
        header.AddAction("Actualiser", ButtonKind.Secondary, Icons.Refresh, async (_, _) => await RunAsync(RefreshAsync));
        if (Session.IsManagerOrAdmin)
        {
            header.AddControl(_edit);
            header.AddAction("Nouvelle catégorie", ButtonKind.Primary, Icons.Add, async (_, _) => await CreateAsync());
        }

        _hint.Text = Session.IsManagerOrAdmin
            ? "Double-cliquez sur une ligne pour modifier la catégorie et ses tarifs."
            : "Consultation seule : la modification des tarifs est réservée aux managers.";
        _inactive.Margin = new Padding(0, 0, 0, Theme.S(12));

        ThemedGrid grid = _card.Grid;
        grid.AddText("category", "CATÉGORIE", nameof(PriceRow.CategoryName), 24, minWidth: 140);
        grid.AddText("service", "SERVICE", nameof(PriceRow.Service), 24, minWidth: 140);
        grid.AddText("amount", "PRIX", nameof(PriceRow.Amount), 15, true, 100);
        grid.AddText("express", "SUPPL. EXPRESS", nameof(PriceRow.Express), 16, true, 120);
        grid.AddText("days", "DÉLAI", nameof(PriceRow.Days), 12, true, 90);
        grid.AddPill("state", "", 10, 90);
        grid.SelectionChanged += (_, _) => _edit.Enabled = Session.IsManagerOrAdmin && grid.SelectedItem<PriceRow>() is not null;
        grid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0 && grid.Rows[e.RowIndex].DataBoundItem is PriceRow)
            {
                await EditAsync();
            }
        };

        Controls.Add(UiKit.Rows((header, false), (_inactive, false), (_card, true), (_hint, false)));
        _edit.Click += async (_, _) => await EditAsync();
        _inactive.CheckedChanged += async (_, _) => await RunAsync(LoadAsync);
    }

    public override string Title => "Tarifs";

    public override async Task LoadAsync()
    {
        IReadOnlyList<CategoryDto> catalog = await Session.GetCatalogAsync(true, _inactive.Checked);
        if (!Alive)
        {
            return;
        }

        List<PriceRow> rows = new();
        foreach (CategoryDto category in catalog.OrderBy(c => c.SortOrder).ThenBy(c => c.Name))
        {
            foreach (CategoryPriceDto price in category.Prices.OrderBy(p => p.ServiceType))
            {
                if (_inactive.Checked || (price.IsActive && category.IsActive))
                {
                    rows.Add(new PriceRow(category, price));
                }
            }
        }

        _card.Grid.SetRows(rows);
        _card.UpdateEmpty();
    }

    private async Task CreateAsync()
    {
        using CategoryDialog dialog = new(Api, null);
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Session.InvalidateCatalog();
            Host.Toast("Catégorie créée.");
            await RunAsync(LoadAsync);
        }
    }

    private async Task EditAsync()
    {
        PriceRow? row = _card.Grid.SelectedItem<PriceRow>();
        if (row is null || !Session.IsManagerOrAdmin)
        {
            return;
        }

        // Charge la catégorie complète (avec les services inactifs) avant l'édition.
        CategoryDto category = row.Category;
        using CategoryDialog dialog = new(Api, category);
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Session.InvalidateCatalog();
            Host.Toast("Tarifs mis à jour.");
            await RunAsync(LoadAsync);
        }
    }
}
