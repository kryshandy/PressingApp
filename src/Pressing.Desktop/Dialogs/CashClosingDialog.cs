using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Desktop.Ui;

namespace Pressing.Desktop.Dialogs;

/// <summary>
/// Clôture de caisse journalière (« Z de caisse ») : montants attendus par mode de paiement, saisie du fond de caisse
/// et des espèces comptées, écart calculé en direct (vert = juste, rouge = manque, ambre = excédent), commentaire.
/// Un brouillon existant est repris et mis à jour.
/// </summary>
public sealed class CashClosingDialog : DialogForm
{
    private readonly PressingApiClient _api;
    private readonly CashClosingPreviewDto _preview;
    private readonly CashClosingDto? _draft;
    private readonly InputBox _float;
    private readonly InputBox _counted;
    private readonly InputBox _comment;
    private readonly LiveStrip _variance;

    public CashClosingDialog(PressingApiClient api, CashClosingPreviewDto preview, CashClosingDto? draft = null)
        : base("Clôture de caisse",
            $"Journée du {Fmt.Date(preview.Date)} · {preview.PaymentsCount} encaissement(s), {preview.RefundsCount} remboursement(s)",
            760, 640)
    {
        _api = api;
        _preview = preview;
        _draft = draft;
        Primary.Text = draft is null ? "Enregistrer la clôture" : "Mettre à jour la clôture";
        Primary.IconGlyph = Icons.Check;

        _float = new InputBox
        {
            Suffix = "FCFA", TextAlign = HorizontalAlignment.Right, SelectAllOnFocus = true,
            AmountValue = draft?.OpeningFloat ?? 0
        };
        _counted = new InputBox
        {
            Suffix = "FCFA", TextAlign = HorizontalAlignment.Right, SelectAllOnFocus = true,
            AmountValue = draft?.CountedCash ?? preview.ExpectedCash
        };
        _comment = new InputBox
        {
            Multiline = true,
            Height = Theme.S(88),
            Text = draft?.Comment ?? string.Empty,
            PlaceholderText = "Obligatoire en cas d'écart : expliquez la différence constatée."
        };

        _variance = new LiveStrip();

        // Les blocs sont empilés du dernier ajouté (en haut) au premier (en bas).
        Body.Controls.Add(new FieldBox("Commentaire", _comment));
        Body.Controls.Add(_variance);
        FormGrid grid = new();
        grid.AddField("Fond de caisse d'ouverture", _float, 1, "Espèces présentes dans le tiroir en début de journée");
        grid.AddField("Espèces comptées", _counted, 1, "Total du tiroir-caisse, fond de caisse inclus");
        Body.Controls.Add(grid);
        Body.Controls.Add(Summary.Strip(
            ("Espèces", Fmt.Money(preview.ExpectedCash), null),
            ("Mobile Money", Fmt.Money(preview.ExpectedMobileMoney), null),
            ("Carte bancaire", Fmt.Money(preview.ExpectedCard), null),
            ("Virement", Fmt.Money(preview.ExpectedTransfer), null)));
        Body.Controls.Add(new TextLabel
        {
            Text = preview.ExpectedOther != 0
                ? $"ENCAISSEMENTS ATTENDUS, NETS DES REMBOURSEMENTS (HORS AUTRES MODES : {Fmt.Money(preview.ExpectedOther)})"
                : "ENCAISSEMENTS ATTENDUS, NETS DES REMBOURSEMENTS",
            Font = Theme.FieldLabel,
            ForeColor = Theme.TextMuted
        });

        _float.TextChanged += (_, _) => Recompute();
        _counted.TextChanged += (_, _) => Recompute();
        Recompute();

        Primary.Click += async (_, _) => await SubmitAsync();
        Shown += (_, _) => _counted.Focus();
    }

    public CashClosingDto? Result { get; private set; }

    private decimal ExpectedInDrawer => _float.AmountValue + _preview.ExpectedCash;

    private decimal Variance => _counted.AmountValue - ExpectedInDrawer;

    private void Recompute()
    {
        decimal variance = Variance;
        Color color = variance == 0 ? Theme.Green : variance < 0 ? Theme.Red : Theme.Amber;
        string text = variance == 0
            ? "Aucun écart"
            : (variance > 0 ? "+ " : "− ") + Fmt.Money(Math.Abs(variance));
        string explanation = variance == 0 ? "La caisse est juste" : variance < 0 ? "Il manque de l'argent en caisse" : "Il y a un excédent en caisse";
        _variance.Set(
            ("Écart constaté", text, color),
            ("Espèces attendues en caisse", Fmt.Money(ExpectedInDrawer), null),
            ("Total encaissé du jour", Fmt.Money(_preview.TotalExpected), null));
        _variance.Caption = explanation;
        _variance.CaptionColor = color;
        if (variance == 0)
        {
            ShowError(null);
        }
    }

    private async Task SubmitAsync()
    {
        if (_counted.AmountValue < 0 || _float.AmountValue < 0)
        {
            ShowError("Les montants ne peuvent pas être négatifs.");
            return;
        }

        decimal variance = Variance;
        if (variance != 0 && string.IsNullOrWhiteSpace(_comment.Text))
        {
            ShowError($"Un écart de {Fmt.Money(Math.Abs(variance))} est constaté : le commentaire est obligatoire.");
            _comment.Focus();
            return;
        }

        ShowError(null);
        CashClosingRequest request = new(
            _preview.Date, _float.AmountValue, _counted.AmountValue,
            string.IsNullOrWhiteSpace(_comment.Text) ? null : _comment.Text.Trim());
        bool ok = await UiRun.RunAsync(this, async () =>
        {
            Result = _draft is null
                ? await _api.CreateCashClosingAsync(request)
                : await _api.UpdateCashClosingAsync(_draft.Id, request);
        }, "Clôture impossible");
        if (ok)
        {
            DialogResult = DialogResult.OK;
        }
    }

    /// <summary>Bandeau de chiffres clés mis à jour en direct ; le premier chiffre est mis en avant avec sa couleur.</summary>
    private sealed class LiveStrip : RoundedPanel
    {
        private (string Label, string Value, Color? Color)[] _items = Array.Empty<(string, string, Color?)>();

        public string Caption { get; set; } = string.Empty;

        public Color CaptionColor { get; set; } = Theme.TextMuted;

        public LiveStrip()
        {
            Height = Theme.S(108);
        }

        public void Set(params (string Label, string Value, Color? Color)[] items)
        {
            _items = items;
            Invalidate();
        }

        public override Size GetPreferredSize(Size proposedSize) => new(proposedSize.Width, Theme.S(108));

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_items.Length == 0)
            {
                return;
            }

            Font label = Theme.FieldLabel;
            Font value = Theme.Semi(14f);
            Font small = Theme.F(9f);
            int pad = Theme.S(20);
            int width = (Width - 2 * pad) / _items.Length;
            int y = Theme.S(18);
            TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            for (int i = 0; i < _items.Length; i++)
            {
                int x = pad + i * width;
                TextRenderer.DrawText(e.Graphics, _items[i].Label.ToUpperInvariant(), label,
                    new Rectangle(x, y, width - Theme.S(12), label.Height), Theme.TextMuted, flags);
                TextRenderer.DrawText(e.Graphics, _items[i].Value, value,
                    new Rectangle(x, y + label.Height + Theme.S(6), width - Theme.S(12), value.Height),
                    _items[i].Color ?? Theme.Figure, flags);
            }

            if (Caption.Length > 0)
            {
                TextRenderer.DrawText(e.Graphics, Caption, small,
                    new Rectangle(pad, Height - Theme.S(18) - small.Height, Width - 2 * pad, small.Height), CaptionColor, flags);
            }
        }
    }
}
