using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Desktop.Dialogs;
using Pressing.Desktop.Services;
using Pressing.Desktop.Ui;

namespace Pressing.Desktop.Views;

/// <summary>Codes promotionnels : liste, création et modification (Managers et Administrateurs).</summary>
public sealed class PromotionsView : BaseView
{
    private sealed class PromoRow : IPillRow, ITintRow
    {
        public PromoRow(PromotionDto p)
        {
            Source = p;
            Code = p.Code;
            Description = string.IsNullOrWhiteSpace(p.Description) ? "—" : p.Description;
            Type = p.Type == PromotionType.Percentage ? "Pourcentage" : "Montant fixe";
            Value = p.Type == PromotionType.Percentage ? Theme.AmountText(p.Value) + " %" : Fmt.Money(p.Value);
            Minimum = p.MinOrderAmount > 0 ? Fmt.Money(p.MinOrderAmount) : "—";
            Period = $"{Fmt.Date(p.ValidFrom)} → {Fmt.Date(p.ValidTo)}";
            Uses = p.MaxUses > 0 ? $"{p.UsedCount} / {p.MaxUses}" : $"{p.UsedCount} / illimité";
        }

        public PromotionDto Source { get; }
        public string Code { get; }
        public string Description { get; }
        public string Type { get; }
        public string Value { get; }
        public string Minimum { get; }
        public string Period { get; }
        public string Uses { get; }

        public (string Text, Color Color)? GetPill(string columnName)
        {
            if (columnName != "state")
            {
                return null;
            }

            if (!Source.IsActive)
            {
                return ("Inactive", Theme.Slate);
            }

            if (Source.ValidTo < DateTime.Now)
            {
                return ("Expirée", Theme.Amber);
            }

            if (Source.MaxUses > 0 && Source.UsedCount >= Source.MaxUses)
            {
                return ("Épuisée", Theme.Amber);
            }

            return Source.ValidFrom > DateTime.Now ? ("À venir", Theme.Blue) : ("Active", Theme.Green);
        }

        public Color? GetTint(string columnName) => columnName switch
        {
            "code" => Source.IsActive ? Theme.Accent : Theme.TextFaint,
            _ => !Source.IsActive ? Theme.TextFaint : null
        };
    }

    private readonly GridCard _card = new("Aucune promotion pour le moment. Créez votre premier code promo.");
    private readonly FlatButton _edit = new() { Text = "Modifier", Kind = ButtonKind.Secondary, IconGlyph = Icons.Edit, Enabled = false };
    private readonly ToggleBox _inactive = new() { Text = "Afficher les promotions inactives", Checked = true };
    private readonly Label _hint = new() { AutoSize = true, ForeColor = Theme.TextMuted, Font = Theme.F(9.5f), Margin = new Padding(0, Theme.S(8), 0, 0) };
    private List<PromotionDto> _all = new();

    public PromotionsView(IAppHost host) : base(host)
    {
        PageHeader header = new("Promotions", "Codes promo et remises applicables lors du dépôt");
        header.AddAction("Actualiser", ButtonKind.Secondary, Icons.Refresh, async (_, _) => await RunAsync(RefreshAsync));
        header.AddControl(_edit);
        header.AddAction("Nouvelle promotion", ButtonKind.Primary, Icons.Add, async (_, _) => await CreateAsync());

        _hint.Text = "Double-cliquez sur une ligne pour modifier la promotion. Une promotion inactive n'est plus acceptée à la caisse.";
        _inactive.Margin = new Padding(0, 0, 0, Theme.S(12));

        ThemedGrid grid = _card.Grid;
        grid.AddText("code", "CODE", nameof(PromoRow.Code), 14, minWidth: 110);
        grid.AddText("description", "DESCRIPTION", nameof(PromoRow.Description), 26, minWidth: 140);
        grid.AddText("type", "TYPE", nameof(PromoRow.Type), 11, minWidth: 100);
        grid.AddText("value", "VALEUR", nameof(PromoRow.Value), 9, true, 80);
        grid.AddText("minimum", "MINIMUM", nameof(PromoRow.Minimum), 11, true, 90);
        grid.AddText("period", "PÉRIODE", nameof(PromoRow.Period), 20, minWidth: 180);
        grid.AddText("uses", "UTILISATIONS", nameof(PromoRow.Uses), 11, true, 110);
        grid.AddPill("state", "ÉTAT", 10, 100);
        grid.SelectionChanged += (_, _) => _edit.Enabled = grid.SelectedItem<PromoRow>() is not null;
        grid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0 && grid.Rows[e.RowIndex].DataBoundItem is PromoRow)
            {
                await EditAsync();
            }
        };

        Controls.Add(UiKit.Rows((header, false), (_inactive, false), (_card, true), (_hint, false)));
        _edit.Click += async (_, _) => await EditAsync();
        _inactive.CheckedChanged += (_, _) => Fill();
    }

    public override string Title => "Promotions";

    public override async Task LoadAsync()
    {
        List<PromotionDto>? list = await Api.GetPromotionsAsync(true);
        if (!Alive)
        {
            return;
        }

        _all = list ?? new List<PromotionDto>();
        Fill();
    }

    private void Fill()
    {
        IEnumerable<PromotionDto> items = _inactive.Checked ? _all : _all.Where(p => p.IsActive);
        _card.Grid.SetRows(items.Select(p => new PromoRow(p)).ToList());
        _card.UpdateEmpty();
    }

    private async Task CreateAsync()
    {
        using PromotionDialog dialog = new(Api, null);
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Host.Toast("Promotion créée.");
            await RunAsync(LoadAsync);
        }
    }

    private async Task EditAsync()
    {
        PromoRow? row = _card.Grid.SelectedItem<PromoRow>();
        if (row is null)
        {
            return;
        }

        using PromotionDialog dialog = new(Api, row.Source);
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Host.Toast("Promotion mise à jour.");
            await RunAsync(LoadAsync);
        }
    }
}
