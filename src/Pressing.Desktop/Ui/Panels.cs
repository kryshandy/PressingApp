using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace Pressing.Desktop.Ui;

/// <summary>Carte à coins arrondis. Son <c>BackColor</c> est la couleur de la carte : les enfants l'héritent.</summary>
public class RoundedPanel : Panel
{
    public RoundedPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.ContainerControl, true);
        BackColor = Theme.Surface;
        Padding = Theme.Pad(20);
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = 14;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = Theme.Border;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool DrawBorder { get; set; } = true;

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Theme.BackOf(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle rect = new(0, 0, Width - 1, Height - 1);
        using GraphicsPath path = Theme.RoundedRect(rect, Theme.S(Radius));
        using SolidBrush brush = new(BackColor);
        g.FillPath(brush, path);
        if (DrawBorder)
        {
            using Pen pen = new(BorderColor, 1f);
            g.DrawPath(pen, path);
        }
    }

    protected override void OnParentBackColorChanged(EventArgs e)
    {
        Invalidate();
        base.OnParentBackColorChanged(e);
    }
}

/// <summary>Carte de chiffre clé : titre discret, grande valeur, note, filet d'accent (dessinée à la main).</summary>
public sealed class StatCard : RoundedPanel
{
    private readonly string _title;
    private string _value;
    private string _note;
    private Color _accent;

    public StatCard(string title, string value, Color accent, string note = "")
    {
        _title = title.ToUpperInvariant();
        _value = value;
        _note = note;
        _accent = accent;
        Dock = DockStyle.Fill;
        Margin = new Padding(Theme.S(6));
        MinimumSize = new Size(Theme.S(120), Theme.S(100));
    }

    public void Set(string value, string? note = null)
    {
        _value = value;
        if (note is not null)
        {
            _note = note;
        }

        Invalidate();
    }

    public void SetAccent(Color accent)
    {
        _accent = accent;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle bar = new(Theme.S(10), Theme.S(16), Theme.S(4), Math.Max(Theme.S(10), Height - Theme.S(32)));
        using (GraphicsPath path = Theme.RoundedRect(bar, Theme.S(2)))
        using (SolidBrush brush = new(_accent))
        {
            g.FillPath(brush, path);
        }

        int left = Theme.S(26);
        int width = Math.Max(10, Width - left - Theme.S(12));
        TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
        Font titleFont = Theme.F(8.5f, FontStyle.Bold);
        Font valueFont = Theme.F(19f, FontStyle.Bold);
        Font noteFont = Theme.F(9f);
        int titleHeight = titleFont.Height;
        int valueHeight = valueFont.Height;
        int noteHeight = noteFont.Height;
        int gap = Math.Max(Theme.S(2), (Height - Theme.S(28) - titleHeight - valueHeight - noteHeight) / 2);
        int y = Theme.S(14);
        TextRenderer.DrawText(g, _title, titleFont, new Rectangle(left, y, width, titleHeight), Theme.TextMuted, flags);
        y += titleHeight + gap;
        TextRenderer.DrawText(g, _value, valueFont, new Rectangle(left, y, width, valueHeight), Theme.Text, flags);
        y += valueHeight + gap;
        TextRenderer.DrawText(g, _note, noteFont, new Rectangle(left, y, width, noteHeight), Theme.TextMuted, flags);
    }
}

/// <summary>Pastille colorée (statut) : texte + fond translucide.</summary>
public sealed class PillLabel : Control
{
    private Color _color = Theme.Slate;

    public PillLabel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        Font = Theme.F(9f, FontStyle.Bold);
        Height = Theme.S(28);
        Margin = new Padding(0, 0, Theme.S(8), 0);
    }

    public Color PillColor
    {
        get => _color;
        set { _color = value; Invalidate(); }
    }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => base.Text;
        set { base.Text = value ?? string.Empty; Invalidate(); }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        Size text = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
        return new Size(text.Width + Theme.S(26), Theme.S(28));
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Size = GetPreferredSize(Size.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.BackOf(this));
        PillPainter.Draw(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), Text, _color, Font);
    }
}

/// <summary>Dessin partagé des pastilles (contrôle PillLabel et cellules de grille).</summary>
public static class PillPainter
{
    public static void Draw(Graphics g, Rectangle rect, string text, Color color, Font? font = null, Color? back = null)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using GraphicsPath path = Theme.RoundedRect(rect, rect.Height / 2);
        Color under = back ?? Theme.Surface;
        using SolidBrush brush = new(Theme.Mix(under, color, 0.20f));
        g.FillPath(brush, path);
        using Pen pen = new(Theme.Mix(under, color, 0.45f), 1f);
        g.DrawPath(pen, path);
        TextRenderer.DrawText(g, text, font ?? Theme.F(9f, FontStyle.Bold), rect, Theme.Mix(color, Color.White, 0.25f),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }
}

/// <summary>Libellé au-dessus d'un contrôle de saisie.</summary>
public sealed class FieldBox : TableLayoutPanel
{
    public FieldBox(string label, Control control, string? hint = null)
    {
        ColumnCount = 1;
        RowCount = hint is null ? 2 : 3;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Fill;
        Margin = new Padding(0, 0, Theme.S(12), Theme.S(12));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Caption = new Label
        {
            Text = label,
            AutoSize = true,
            Font = Theme.F(9f, FontStyle.Bold),
            ForeColor = Theme.TextMuted,
            Margin = new Padding(0, 0, 0, Theme.S(6))
        };
        Control = control;
        control.Dock = DockStyle.Fill;
        control.Margin = Padding.Empty;
        Controls.Add(Caption, 0, 0);
        Controls.Add(control, 0, 1);
        if (hint is not null)
        {
            RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(new Label
            {
                Text = hint,
                AutoSize = true,
                Font = Theme.F(8.5f),
                ForeColor = Theme.TextFaint,
                Margin = new Padding(0, Theme.S(4), 0, 0)
            }, 0, 2);
        }
    }

    public Label Caption { get; }

    public Control Control { get; }
}

/// <summary>Barre de pagination « ‹ Page 2 / 5 › — 123 résultats ».</summary>
public sealed class PagerBar : TableLayoutPanel
{
    private readonly FlatButton _prev;
    private readonly FlatButton _next;
    private readonly Label _info;

    public PagerBar()
    {
        ColumnCount = 4;
        RowCount = 1;
        Height = Theme.S(48);
        Dock = DockStyle.Bottom;
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _prev = new FlatButton { Kind = ButtonKind.Secondary, IconGlyph = Icons.ChevronLeft, Height = Theme.S(34), Anchor = AnchorStyles.Left };
        _next = new FlatButton { Kind = ButtonKind.Secondary, IconGlyph = Icons.ChevronRight, Height = Theme.S(34), Anchor = AnchorStyles.Left };
        _info = new Label
        {
            AutoSize = true,
            Font = Theme.F(9.5f),
            ForeColor = Theme.TextMuted,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(Theme.S(12), 0, Theme.S(12), 0)
        };
        Label total = new() { AutoSize = true, Font = Theme.F(9.5f), ForeColor = Theme.TextMuted, Anchor = AnchorStyles.Right };
        TotalLabel = total;
        _prev.Click += (_, _) => PageRequested?.Invoke(this, Page - 1);
        _next.Click += (_, _) => PageRequested?.Invoke(this, Page + 1);
        Controls.Add(_prev, 0, 0);
        Controls.Add(_info, 1, 0);
        Controls.Add(_next, 2, 0);
        Controls.Add(total, 3, 0);
        Visible = false;
    }

    private Label TotalLabel { get; }

    public int Page { get; private set; } = 1;

    public event EventHandler<int>? PageRequested;

    public void Update(int page, int totalPages, int totalItems, string noun = "résultat")
    {
        Page = Math.Max(1, page);
        totalPages = Math.Max(1, totalPages);
        _prev.Enabled = Page > 1;
        _next.Enabled = Page < totalPages;
        _info.Text = $"Page {Page} / {totalPages}";
        TotalLabel.Text = totalItems + " " + noun + (totalItems > 1 ? "s" : string.Empty);
        Visible = totalItems > 0;
    }
}

/// <summary>Fabriques de libellés pour éviter la répétition.</summary>
public static class UiKit
{
    public static Label Label(string text, Font? font = null, Color? color = null, bool autoSize = true) => new()
    {
        Text = text,
        AutoSize = autoSize,
        Font = font ?? Theme.Body,
        ForeColor = color ?? Theme.Text,
        BackColor = Color.Transparent,
        Margin = Padding.Empty
    };

    public static Label Muted(string text, float size = 9.5f) => Label(text, Theme.F(size), Theme.TextMuted);

    public static Label Heading(string text) => Label(text, Theme.F(13f, FontStyle.Bold), Theme.Text);

    /// <summary>Séparateur horizontal fin.</summary>
    public static Panel Divider() => new()
    {
        Height = 1,
        Dock = DockStyle.Top,
        BackColor = Theme.Border,
        Margin = new Padding(0, Theme.S(8), 0, Theme.S(8))
    };

    /// <summary>Ligne « libellé ……… valeur » pour les récapitulatifs.</summary>
    public static TableLayoutPanel KeyValueRow(string key, out Label value, Color? valueColor = null, bool bold = false)
    {
        TableLayoutPanel row = new()
        {
            ColumnCount = 2,
            RowCount = 1,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, Theme.S(6))
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Label k = Label(key, Theme.F(10f), Theme.TextMuted);
        k.Anchor = AnchorStyles.Left;
        value = Label("—", Theme.F(10f, bold ? FontStyle.Bold : FontStyle.Regular), valueColor ?? Theme.Text);
        value.Anchor = AnchorStyles.Right;
        row.Controls.Add(k, 0, 0);
        row.Controls.Add(value, 1, 0);
        return row;
    }

    /// <summary>Empile verticalement des contrôles : hauteur automatique, sauf la ligne « Fill » qui prend le reste.</summary>
    public static TableLayoutPanel Rows(params (Control Control, bool Fill)[] rows)
    {
        TableLayoutPanel table = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = rows.Length,
            Margin = Padding.Empty,
            BackColor = Color.Transparent
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < rows.Length; i++)
        {
            table.RowStyles.Add(rows[i].Fill ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
            rows[i].Control.Dock = DockStyle.Fill;
            table.Controls.Add(rows[i].Control, 0, i);
        }

        return table;
    }

    /// <summary>Bandeau d'état vide / message inline (ex. erreur de connexion).</summary>
    public static Label Notice(string text, Color color) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = color,
        Font = Theme.F(9.5f),
        BackColor = Color.Transparent,
        Margin = Padding.Empty
    };
}
