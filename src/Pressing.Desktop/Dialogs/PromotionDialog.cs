using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Desktop.Ui;

namespace Pressing.Desktop.Dialogs;

/// <summary>Création / modification d'une promotion (code promo).</summary>
public sealed class PromotionDialog : DialogForm
{
    private readonly PressingApiClient _api;
    private readonly PromotionDto? _existing;
    private readonly InputBox _code = new() { CharacterCasingUpper = true, PlaceholderText = "ex. BIENVENUE10", MaxLength = 30 };
    private readonly InputBox _description = new() { PlaceholderText = "ex. 10 % de remise pour une première commande" };
    private readonly SelectBox _type = new();
    private readonly InputBox _value = new() { TextAlign = HorizontalAlignment.Right, SelectAllOnFocus = true };
    private readonly InputBox _minimum = new() { Suffix = "F", TextAlign = HorizontalAlignment.Right, SelectAllOnFocus = true };
    private readonly DateBox _from = new();
    private readonly DateBox _to = new();
    private readonly InputBox _maxUses = new() { TextAlign = HorizontalAlignment.Right, SelectAllOnFocus = true };
    private readonly ToggleBox _active = new() { Text = "Promotion active", Checked = true };

    public PromotionDialog(PressingApiClient api, PromotionDto? existing)
        : base(existing is null ? "Nouvelle promotion" : "Modifier la promotion",
            existing?.Code ?? "Définissez le code, la remise et la période de validité.", 700, 620)
    {
        _api = api;
        _existing = existing;
        Primary.Text = existing is null ? "Créer" : "Enregistrer";
        Primary.IconGlyph = Icons.Save;

        _type.SetChoices(new[]
        {
            new Choice<PromotionType>(PromotionType.Percentage, "Pourcentage (%)"),
            new Choice<PromotionType>(PromotionType.FixedAmount, "Montant fixe (F)")
        }, existing?.Type ?? PromotionType.Percentage);

        _code.Text = existing?.Code ?? string.Empty;
        _description.Text = existing?.Description ?? string.Empty;
        _value.Text = existing is null ? string.Empty : Theme.AmountText(existing.Value);
        _minimum.AmountValue = existing?.MinOrderAmount ?? 0;
        _from.Value = existing?.ValidFrom.Date ?? DateTime.Today;
        _to.Value = existing?.ValidTo.Date ?? DateTime.Today.AddMonths(1);
        _maxUses.Text = (existing?.MaxUses ?? 0).ToString();
        _active.Checked = existing?.IsActive ?? true;
        UpdateValueSuffix();
        _type.SelectedIndexChanged += (_, _) => UpdateValueSuffix();

        FormGrid grid = new(2);
        grid.AddField("Code promo *", _code);
        grid.AddField("Type de remise", _type);
        grid.AddField("Description", _description, 2);
        grid.AddField("Valeur de la remise *", _value, 1, "Pourcentage entre 0 et 100, ou montant en francs.");
        grid.AddField("Montant minimum de commande", _minimum, 1, "0 = aucun minimum.");
        grid.AddField("Valable du *", _from);
        grid.AddField("Jusqu'au (inclus) *", _to);
        grid.AddField("Utilisations maximum", _maxUses, 1, "0 = utilisations illimitées.");
        if (existing is not null)
        {
            grid.AddField("Utilisations actuelles", new Label
            {
                Text = existing.UsedCount.ToString(),
                AutoSize = false,
                Height = Theme.S(40),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = Theme.F(11f, FontStyle.Bold),
                ForeColor = Theme.Accent
            });
        }

        FlowLayoutPanel flags = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0, Theme.S(4), 0, 0) };
        flags.Controls.Add(_active);
        Body.Controls.Add(flags);
        Body.Controls.Add(grid);
        Primary.Click += async (_, _) => await SubmitAsync();
        Shown += (_, _) => _code.Focus();
    }

    public PromotionDto? Result { get; private set; }

    private PromotionType SelectedType => _type.SelectedValue<PromotionType>();

    private void UpdateValueSuffix() => _value.Suffix = SelectedType == PromotionType.Percentage ? "%" : "F";

    private async Task SubmitAsync()
    {
        string code = _code.Text.Trim().ToUpperInvariant();
        if (code.Length == 0)
        {
            Fail("Le code promo est obligatoire.", _code);
            return;
        }

        if (code.Any(char.IsWhiteSpace))
        {
            Fail("Le code promo ne doit pas contenir d'espace.", _code);
            return;
        }

        if (!Theme.TryParseAmount(_value.Text, out decimal value) || value <= 0)
        {
            Fail("La valeur de la remise doit être supérieure à 0.", _value);
            return;
        }

        if (SelectedType == PromotionType.Percentage && value > 100)
        {
            Fail("Le pourcentage ne peut pas dépasser 100 %.", _value);
            return;
        }

        decimal minimum = 0;
        if (!string.IsNullOrWhiteSpace(_minimum.Text) && (!Theme.TryParseAmount(_minimum.Text, out minimum) || minimum < 0))
        {
            Fail("Le montant minimum doit être positif ou nul.", _minimum);
            return;
        }

        if (_to.Value < _from.Value)
        {
            Fail("La date de fin précède la date de début.", _to);
            return;
        }

        int maxUses = 0;
        if (!string.IsNullOrWhiteSpace(_maxUses.Text) && (!int.TryParse(_maxUses.Text.Trim(), out maxUses) || maxUses < 0))
        {
            Fail("Utilisations maximum : entier positif (0 = illimité).", _maxUses);
            return;
        }

        if (_existing is not null && maxUses > 0 && maxUses < _existing.UsedCount)
        {
            Fail($"Déjà {_existing.UsedCount} utilisation(s) : maximum trop bas.", _maxUses);
            return;
        }

        ShowError(null);
        DateTime validFrom = _from.Value.Date;
        DateTime validTo = _to.Value.Date.AddDays(1).AddSeconds(-1);
        string description = _description.Text.Trim();
        bool ok = await UiRun.RunAsync(this, async () =>
        {
            Result = _existing is null
                ? await _api.CreatePromotionAsync(new CreatePromotionRequest(code, description, SelectedType, value, minimum, validFrom, validTo, maxUses, _active.Checked))
                : await _api.UpdatePromotionAsync(_existing.Id, new UpdatePromotionRequest(code, description, SelectedType, value, minimum, validFrom, validTo, maxUses, _active.Checked));
        }, "Enregistrement de la promotion impossible");
        if (ok)
        {
            DialogResult = DialogResult.OK;
        }
    }

    private void Fail(string message, Control focus)
    {
        ShowError(message);
        focus.Focus();
    }
}
