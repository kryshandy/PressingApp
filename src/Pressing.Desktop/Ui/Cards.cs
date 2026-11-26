namespace Pressing.Desktop.Ui;

/// <summary>
/// Carte avec titre (12 pt semi-gras), sous-titre et actions facultatifs, padding de 24 px, 16 px entre le titre et
/// le contenu. Mise en page manuelle : la hauteur préférée = en-tête + contenu (pour <see cref="GridRow"/> /
/// <see cref="VStack"/>) ; si la carte est plus haute, le contenu s'étire.
/// <paramref name="flushContent"/> : le contenu (grille) touche les bords gauche/droit de la carte.
/// </summary>
public sealed class TitledCard : RoundedPanel
{
    private readonly Label _title;
    private readonly Label _subtitle;
    private readonly ActionBar _actions;
    private readonly bool _flush;

    public TitledCard(string title, Control content, string? subtitle = null, bool flushContent = false, bool autoHeight = false)
    {
        _flush = flushContent;
        AutoHeight = autoHeight;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        _title = new TextLabel
        {
            Text = title,
            AutoSize = false,
            AutoEllipsis = true,
            Font = Theme.CardTitle,
            ForeColor = Theme.Text,
            BackColor = Color.Transparent,
            UseMnemonic = false
        };
        _subtitle = new TextLabel
        {
            Text = subtitle ?? string.Empty,
            AutoSize = false,
            AutoEllipsis = true,
            Font = Theme.F(9f),
            ForeColor = Theme.TextMuted,
            BackColor = Color.Transparent,
            UseMnemonic = false
        };
        _actions = new ActionBar { ButtonHeight = Theme.S(32), MaxVisible = 3 };
        if (flushContent && content is RoundedPanel inner)
        {
            inner.DrawBorder = false;
            inner.Radius = 0;
        }

        LayoutUtil.Prepare(content);
        Controls.Add(_title);
        Controls.Add(_subtitle);
        Controls.Add(_actions);
        Controls.Add(content);
        Content = content;
    }

    public Control Content { get; }

    /// <summary>Hauteur de la carte = hauteur du contenu (sinon le contenu remplit la carte).</summary>
    public bool AutoHeight { get; set; }

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
        set => _title.Text = value;
    }

    public void AddAction(Control control)
    {
        if (control is FlatButton { AutoSize: true } b)
        {
            b.Height = Theme.S(32);
        }

        _actions.Add(control);
        PerformLayout();
    }

    private bool HasHeader => _title.Text.Length > 0 || _actions.Controls.Count > 1;

    private int HeaderHeight(int width)
    {
        if (!HasHeader)
        {
            return 0;
        }

        int text = _title.Font.Height + (_subtitle.Text.Length > 0 ? Theme.S(2) + _subtitle.Font.Height : 0);
        int actions = _actions.GetPreferredSize(Size.Empty).Height;
        return Math.Max(text, actions);
    }

    private Padding Inner => new(
        _flush ? 1 : Theme.CardPadding,
        Theme.CardPadding,
        _flush ? 1 : Theme.CardPadding,
        _flush ? Theme.S(8) : Theme.CardPadding);

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        Padding inner = Inner;
        int header = HeaderHeight(width - 2 * Theme.CardPadding);
        int contentWidth = Math.Max(1, width - inner.Horizontal);
        int content = LayoutUtil.IsShown(Content) ? LayoutUtil.PreferredHeight(Content, contentWidth) : 0;
        return new Size(width, inner.Top + header + (header > 0 ? Theme.S(16) : 0) + content + inner.Bottom);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (Width <= 0 || Height <= 0 || _title is null || Content is null)
        {
            return;
        }

        Padding inner = Inner;
        int headerWidth = Width - 2 * Theme.CardPadding;
        int header = HeaderHeight(headerWidth);
        int y = inner.Top;
        if (header > 0)
        {
            Size actions = _actions.GetPreferredSize(Size.Empty);
            int actionsWidth = Math.Min(actions.Width, headerWidth / 2 + Theme.S(60));
            _actions.SetBounds(Width - Theme.CardPadding - actionsWidth, y + (header - actions.Height) / 2, actionsWidth, actions.Height);
            int textWidth = Math.Max(10, headerWidth - (actionsWidth > 0 ? actionsWidth + Theme.S(16) : 0));
            int textHeight = _title.Font.Height + (_subtitle.Text.Length > 0 ? Theme.S(2) + _subtitle.Font.Height : 0);
            int ty = y + (header - textHeight) / 2;
            _title.SetBounds(Theme.CardPadding, ty, textWidth, _title.Font.Height);
            _subtitle.Visible = _subtitle.Text.Length > 0;
            _subtitle.SetBounds(Theme.CardPadding, ty + _title.Font.Height + Theme.S(2), textWidth, _subtitle.Font.Height);
            y += header + Theme.S(16);
        }

        _title.Visible = header > 0;
        int contentWidth = Math.Max(1, Width - inner.Horizontal);
        int available = Math.Max(0, Height - y - inner.Bottom);
        int height = AutoHeight ? Math.Min(available, LayoutUtil.PreferredHeight(Content, contentWidth)) : available;
        Content.SetBounds(inner.Left, y, contentWidth, Math.Max(available, height));
    }
}

public static class ControlExtensions
{
    /// <summary>Rend un contrôle (et ses enfants) cliquable avec le curseur « main ».</summary>
    public static void MakeClickable(this Control control, Action action)
    {
        control.Cursor = Cursors.Hand;
        control.Click += (_, _) => action();
        foreach (Control child in control.Controls)
        {
            child.MakeClickable(action);
        }
    }
}

/// <summary>Liste « libellé … valeur » (fiches de détail, totaux) : libellé à gauche, valeur alignée à droite.</summary>
public sealed class InfoList : LayoutControl
{
    private readonly bool _wrap;
    private readonly List<(Label? Key, Control Value)> _rows = new();

    public InfoList(bool wrapValues = true)
    {
        _wrap = wrapValues;
        Dock = DockStyle.Top;
    }

    public void Clear()
    {
        foreach ((Label? key, Control value) in _rows)
        {
            key?.Dispose();
            value.Dispose();
        }

        _rows.Clear();
        Controls.Clear();
        PerformLayout();
        this.RequestPageLayout();
    }

    public Label Add(string key, string value, Color? color = null, bool bold = false, float size = 10f)
    {
        Label k = new TextLabel
        {
            Text = key,
            AutoSize = false,
            Font = Theme.F(size),
            ForeColor = Theme.TextMuted,
            BackColor = Color.Transparent,
            UseMnemonic = false
        };
        Label v = new TextLabel
        {
            Wrap = _wrap,
            Text = value,
            AutoSize = false,
            Font = bold ? Theme.Semi(size) : Theme.F(size),
            ForeColor = color ?? Theme.Text,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.TopRight,
            UseMnemonic = false
        };
        v.TextChanged += (_, _) => PerformLayout();
        _rows.Add((k, v));
        Controls.Add(k);
        Controls.Add(v);
        PerformLayout();
        this.RequestPageLayout();
        return v;
    }

    public void AddSeparator()
    {
        Panel line = new() { Height = 1, BackColor = Theme.Border };
        _rows.Add((null, line));
        Controls.Add(line);
        PerformLayout();
    }

    private const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;

    private (int KeyWidth, int RowHeight, int ValueHeight) Measure(Label key, Label value, int width)
    {
        int keyWidth = Math.Min(width / 2, TextRenderer.MeasureText(key.Text, key.Font, new Size(int.MaxValue, int.MaxValue), Flags).Width + Theme.S(2));
        int valueWidth = Math.Max(Theme.S(40), width - keyWidth - Theme.S(16));
        Size v = TextRenderer.MeasureText(value.Text, value.Font, new Size(valueWidth, int.MaxValue), _wrap ? Flags : Flags & ~TextFormatFlags.WordBreak);
        int valueHeight = _wrap ? v.Height : value.Font.Height;
        return (keyWidth, Math.Max(key.Font.Height, valueHeight) + Theme.S(10), valueHeight);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Math.Max(Width, Theme.S(200));
        int height = 0;
        foreach ((Label? key, Control value) in _rows)
        {
            height += key is null ? Theme.S(13) : Measure(key, (Label)value, width).RowHeight;
        }

        return new Size(width, height);
    }

    protected override void Arrange()
    {
        int y = 0;
        foreach ((Label? key, Control value) in _rows)
        {
            if (key is null)
            {
                value.SetBounds(0, y + Theme.S(6), Width, 1);
                y += Theme.S(13);
                continue;
            }

            (int keyWidth, int rowHeight, int valueHeight) = Measure(key, (Label)value, Width);
            key.SetBounds(0, y + Theme.S(5), keyWidth, key.Font.Height);
            int valueLeft = keyWidth + Theme.S(16);
            value.SetBounds(valueLeft, y + Theme.S(5) + (key.Font.Height > valueHeight ? (key.Font.Height - valueHeight) / 2 : 0),
                Width - valueLeft, valueHeight + Theme.S(2));
            y += rowHeight;
        }
    }
}

/// <summary>Panneau défilant qui ne saute pas vers le contrôle focalisé (évite les défilements intempestifs).</summary>
public sealed class ScrollPanel : Panel
{
    public ScrollPanel()
    {
        AutoScroll = true;
        BackColor = Theme.Background;
        Dock = DockStyle.Fill;
    }

    protected override Point ScrollToControl(Control activeControl) => DisplayRectangle.Location;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeScroll.Darken(this);
    }
}

/// <summary>
/// Deux colonnes dont la droite a une largeur fixe ; la hauteur totale s'adapte au contenu le plus haut.
/// (Plus fiable qu'un TableLayoutPanel AutoSize dont les colonnes grandissent selon le contenu.)
/// </summary>
public sealed class TwoColumnPanel : Panel
{
    private readonly Control _left;
    private readonly Control _right;
    private readonly int _rightWidth;
    private bool _inLayout;

    public TwoColumnPanel(Control left, Control right, int rightWidth)
    {
        _left = left;
        _right = right;
        _rightWidth = rightWidth;
        Dock = DockStyle.Top;
        BackColor = Theme.Background;
        foreach (Control c in new[] { left, right })
        {
            c.Dock = DockStyle.None;
            if (c is TableLayoutPanel t)
            {
                t.AutoSize = false;
            }
        }

        Controls.Add(left);
        Controls.Add(right);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_inLayout || ClientSize.Width < 100)
        {
            return;
        }

        _inLayout = true;
        try
        {
            int width = ClientSize.Width;
            int gap = Theme.S(12);
            int height;
            if (width < Theme.S(1000))
            {
                // Écran étroit : la colonne de droite passe sous celle de gauche.
                int leftHeight = _left.GetPreferredSize(new Size(width, 0)).Height;
                int rightHeight = _right.GetPreferredSize(new Size(width, 0)).Height;
                _left.SetBounds(0, 0, width, leftHeight);
                _right.SetBounds(0, leftHeight + gap, width, rightHeight);
                height = leftHeight + gap + rightHeight;
            }
            else
            {
                int rightWidth = Theme.S(_rightWidth);
                int leftWidth = Math.Max(Theme.S(320), width - rightWidth);
                int leftHeight = _left.GetPreferredSize(new Size(leftWidth, 0)).Height;
                int rightHeight = _right.GetPreferredSize(new Size(rightWidth, 0)).Height;
                _left.SetBounds(0, 0, leftWidth, leftHeight);
                _right.SetBounds(leftWidth, 0, rightWidth, rightHeight);
                height = Math.Max(leftHeight, rightHeight);
            }

            if (Height != height)
            {
                Height = height;
            }
        }
        finally
        {
            _inLayout = false;
        }
    }
}
