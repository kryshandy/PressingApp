namespace Pressing.Desktop.Ui;

/// <summary>
/// En-tête de page (DESIGN.md §3) : titre 22 pt semi-gras, sous-titre 10 pt, actions alignées à droite sur une
/// seule ligne (<see cref="ActionBar"/>), centrées verticalement sur le bloc de texte. Un contrôle « avant »
/// (bouton Retour) et des pastilles à droite du titre sont possibles.
/// </summary>
public sealed class PageHeader : LayoutControl
{
    private readonly Label _title;
    private readonly Label _subtitle;
    private readonly ActionBar _actions = new();
    private readonly List<Control> _adornments = new();
    private Control? _leading;

    public PageHeader(string title, string? subtitle = null)
    {
        _title = new TextLabel
        {
            Text = title,
            AutoSize = false,
            AutoEllipsis = true,
            Font = Theme.PageTitle,
            ForeColor = Theme.TitleText,
            BackColor = Color.Transparent,
            UseMnemonic = false
        };
        _subtitle = new TextLabel
        {
            Text = subtitle ?? string.Empty,
            AutoSize = false,
            AutoEllipsis = true,
            Font = Theme.F(10f),
            ForeColor = Theme.TextMuted,
            BackColor = Color.Transparent,
            UseMnemonic = false
        };
        Controls.Add(_title);
        Controls.Add(_subtitle);
        Controls.Add(_actions);
    }

    public string Subtitle
    {
        get => _subtitle.Text;
        set
        {
            _subtitle.Text = value;
            PerformLayout();
        }
    }

    public string TitleText
    {
        get => _title.Text;
        set
        {
            _title.Text = value;
            PerformLayout();
        }
    }

    public ActionBar Actions => _actions;

    public FlatButton AddAction(string text, ButtonKind kind, string icon = "", EventHandler? onClick = null)
    {
        FlatButton button = _actions.AddAction(text, kind, icon, onClick);
        PerformLayout();
        return button;
    }

    public void AddControl(Control control)
    {
        _actions.Add(control);
        PerformLayout();
    }

    /// <summary>Contrôle placé à gauche du titre, sur la gouttière (ex. bouton Retour).</summary>
    public void SetLeading(Control control)
    {
        _leading = control;
        LayoutUtil.Prepare(control);
        Controls.Add(control);
        PerformLayout();
    }

    /// <summary>Pastille ou étiquette placée juste à droite du titre (statut, express…).</summary>
    public void AddTitleAdornment(Control control)
    {
        LayoutUtil.Prepare(control);
        _adornments.Add(control);
        Controls.Add(control);
        PerformLayout();
    }

    private int TextHeight => _title.Font.Height + (_subtitle.Text.Length > 0 ? Theme.S(4) + _subtitle.Font.Height : 0);

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        int actions = _actions.GetPreferredSize(Size.Empty).Height;
        return new Size(width, Math.Max(TextHeight, Math.Max(actions, _leading?.Height ?? 0)));
    }

    protected override void Arrange()
    {
        int x = 0;
        if (_leading is not null && LayoutUtil.IsShown(_leading))
        {
            int lw = _leading.GetPreferredSize(Size.Empty).Width;
            _leading.SetBounds(0, (Height - _leading.Height) / 2, lw, _leading.Height);
            x = lw + Theme.S(16);
        }

        // Le titre (et ses pastilles) est prioritaire : les actions prennent le reste et débordent dans « Plus d'actions ».
        int titleNeed = TextRenderer.MeasureText(_title.Text, _title.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width
                        + _adornments.Where(LayoutUtil.IsShown).Sum(a => a.GetPreferredSize(Size.Empty).Width + Theme.S(12));
        Size actions = _actions.GetPreferredSize(Size.Empty);
        int actionsWidth = Math.Max(0, Math.Min(actions.Width, Width - x - Math.Max(titleNeed, Width / 4) - Theme.S(24)));
        _actions.SetBounds(Width - actionsWidth, (Height - actions.Height) / 2, actionsWidth, actions.Height);

        int textTop = (Height - TextHeight) / 2;
        int available = Math.Max(10, Width - x - actionsWidth - (actionsWidth > 0 ? Theme.S(24) : 0));
        int adornWidth = _adornments.Where(LayoutUtil.IsShown).Sum(a => a.GetPreferredSize(Size.Empty).Width + Theme.S(8));
        int titleWidth = Math.Min(available - adornWidth,
            TextRenderer.MeasureText(_title.Text, _title.Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width + Theme.S(4));
        _title.SetBounds(x, textTop, Math.Max(10, titleWidth), _title.Font.Height);
        int ax = x + titleWidth + Theme.S(12);
        foreach (Control adornment in _adornments.Where(LayoutUtil.IsShown))
        {
            Size size = adornment.GetPreferredSize(Size.Empty);
            adornment.SetBounds(ax, textTop + (_title.Font.Height - size.Height) / 2, size.Width, size.Height);
            ax += size.Width + Theme.S(8);
        }

        _subtitle.Visible = _subtitle.Text.Length > 0;
        _subtitle.SetBounds(x + Theme.S(1), textTop + _title.Font.Height + Theme.S(4), available, _subtitle.Font.Height);
    }
}
