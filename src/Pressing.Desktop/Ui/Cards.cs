namespace Pressing.Desktop.Ui;

/// <summary>Carte avec titre (et sous-titre / actions facultatifs) autour d'un contenu.</summary>
public sealed class TitledCard : RoundedPanel
{
    private readonly Label _title;
    private readonly Label _subtitle;
    private readonly FlowLayoutPanel _actions;

    public TitledCard(string title, Control content, string? subtitle = null, bool flushContent = false, bool autoHeight = false)
    {
        Dock = autoHeight ? DockStyle.Top : DockStyle.Fill;
        if (autoHeight)
        {
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
        }

        Margin = new Padding(Theme.S(6));
        Padding = new Padding(Theme.S(flushContent ? 1 : 20), Theme.S(18), Theme.S(flushContent ? 1 : 20), Theme.S(flushContent ? 8 : 18));

        TableLayoutPanel layout = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(autoHeight ? new RowStyle(SizeType.AutoSize) : new RowStyle(SizeType.Percent, 100));
        if (autoHeight)
        {
            layout.Dock = DockStyle.Top;
            layout.AutoSize = true;
            layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        }


        TableLayoutPanel head = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(flushContent ? Theme.S(19) : 0, 0, flushContent ? Theme.S(19) : 0, Theme.S(12))
        };
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        TableLayoutPanel texts = new() { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill, Margin = Padding.Empty };
        _title = new Label { Text = title, AutoSize = true, Font = Theme.F(12f, FontStyle.Bold), ForeColor = Theme.Text, Margin = Padding.Empty };
        _subtitle = new Label
        {
            Text = subtitle ?? string.Empty,
            AutoSize = true,
            Font = Theme.F(9f),
            ForeColor = Theme.TextMuted,
            Visible = !string.IsNullOrEmpty(subtitle),
            Margin = new Padding(0, Theme.S(2), 0, 0)
        };
        texts.Controls.Add(_title, 0, 0);
        texts.Controls.Add(_subtitle, 0, 1);
        _actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
        head.Controls.Add(texts, 0, 0);
        head.Controls.Add(_actions, 1, 0);

        if (flushContent && content is RoundedPanel inner)
        {
            inner.DrawBorder = false;
        }

        content.Dock = autoHeight ? DockStyle.Top : DockStyle.Fill;
        content.Margin = Padding.Empty;
        layout.Controls.Add(head, 0, 0);
        layout.Controls.Add(content, 0, 1);
        Controls.Add(layout);
        Content = content;
    }

    public Control Content { get; }

    public string Subtitle
    {
        get => _subtitle.Text;
        set
        {
            _subtitle.Text = value;
            _subtitle.Visible = !string.IsNullOrEmpty(value);
        }
    }

    public void AddAction(Control control) => _actions.Controls.Add(control);
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

/// <summary>Liste « libellé … valeur » (fiches de détail, totaux).</summary>
public sealed class InfoList : TableLayoutPanel
{
    private readonly bool _wrap;
    private int _row;

    public InfoList(bool wrapValues = true)
    {
        _wrap = wrapValues;
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        ColumnCount = 2;
        Margin = Padding.Empty;
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
    }

    public void Clear()
    {
        Controls.Clear();
        RowStyles.Clear();
        _row = 0;
    }

    public Label Add(string key, string value, Color? color = null, bool bold = false, float size = 10f)
    {
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Label k = new()
        {
            Text = key,
            AutoSize = true,
            Font = Theme.F(size),
            ForeColor = Theme.TextMuted,
            Margin = new Padding(0, Theme.S(4), Theme.S(16), Theme.S(4)),
            Anchor = AnchorStyles.Left | AnchorStyles.Top
        };
        Label v = new()
        {
            Text = value,
            AutoSize = true,
            MaximumSize = _wrap ? new Size(Theme.S(230), 0) : Size.Empty,
            Font = Theme.F(size, bold ? FontStyle.Bold : FontStyle.Regular),
            ForeColor = color ?? Theme.Text,
            TextAlign = ContentAlignment.TopRight,
            Margin = new Padding(0, Theme.S(4), 0, Theme.S(4)),
            Anchor = AnchorStyles.Right | AnchorStyles.Top
        };
        Controls.Add(k, 0, _row);
        Controls.Add(v, 1, _row);
        _row++;
        return v;
    }

    public void AddSeparator()
    {
        RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(13)));
        Panel line = new() { Height = 1, Anchor = AnchorStyles.Left | AnchorStyles.Right, BackColor = Theme.Border, Margin = new Padding(0, Theme.S(6), 0, 0) };
        Controls.Add(line, 0, _row);
        SetColumnSpan(line, 2);
        _row++;
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
