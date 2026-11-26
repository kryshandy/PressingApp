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
    public int Radius { get; set; } = 12;

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

/// <summary>Carte de chiffre clé : libellé discret, grande valeur (22 pt), note, filet d'accent (dessinée à la main).</summary>
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
        Margin = Padding.Empty;
        MinimumSize = new Size(Theme.S(120), Theme.S(96));
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

    private static Font TitleFont => Theme.FieldLabel;
    private static Font ValueFont => Theme.Semi(20f);
    private static Font NoteFont => Theme.F(9f);

    public override Size GetPreferredSize(Size proposedSize) =>
        new(proposedSize.Width, Theme.S(20) * 2 + TitleFont.Height + Theme.S(6) + ValueFont.Height + Theme.S(4) + NoteFont.Height);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int pad = Theme.S(20);
        int contentHeight = TitleFont.Height + Theme.S(6) + ValueFont.Height + Theme.S(4) + NoteFont.Height;
        int y = Math.Max(pad, (Height - contentHeight) / 2);
        Rectangle bar = new(pad, y + Theme.S(2), Theme.S(4), contentHeight - Theme.S(4));
        using (GraphicsPath path = Theme.RoundedRect(bar, Theme.S(2)))
        using (SolidBrush brush = new(_accent))
        {
            g.FillPath(brush, path);
        }

        int left = pad + Theme.S(16);
        int width = Math.Max(10, Width - left - pad);
        TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        TextRenderer.DrawText(g, _title, TitleFont, new Rectangle(left, y, width, TitleFont.Height), Theme.TextMuted, flags);
        y += TitleFont.Height + Theme.S(6);
        TextRenderer.DrawText(g, _value, ValueFont, new Rectangle(left, y, width, ValueFont.Height), Theme.Figure, flags);
        y += ValueFont.Height + Theme.S(4);
        TextRenderer.DrawText(g, _note, NoteFont, new Rectangle(left, y, width, NoteFont.Height), Theme.TextMuted, flags);
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

/// <summary>Libellé (8.5 pt, majuscules) au-dessus d'un contrôle de saisie, 6 px d'écart ; aide facultative dessous.</summary>
public sealed class FieldBox : LayoutControl
{
    private readonly Label? _hint;

    public FieldBox(string label, Control control, string? hint = null)
    {
        Caption = new TextLabel
        {
            Text = label.ToUpper(System.Globalization.CultureInfo.GetCultureInfo("fr-FR")),
            Font = Theme.FieldLabel,
            ForeColor = Theme.TextMuted
        };
        Control = control;
        LayoutUtil.Prepare(control);
        Controls.Add(Caption);
        Controls.Add(control);
        if (hint is not null)
        {
            _hint = new TextLabel { Text = hint, Font = Theme.F(8.5f), ForeColor = Theme.TextFaint };
            Controls.Add(_hint);
        }
    }

    public Label Caption { get; }

    public Control Control { get; }

    private int ControlHeight(int width) => Control is ToggleBox or Label ? Control.GetPreferredSize(new Size(width, 0)).Height : Control.Height;

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        int height = Caption.Font.Height + Theme.S(6) + ControlHeight(width);
        if (_hint is not null)
        {
            height += Theme.S(4) + _hint.Font.Height;
        }

        return new Size(width, height);
    }

    protected override void Arrange()
    {
        Caption.SetBounds(0, 0, Width, Caption.Font.Height);
        int y = Caption.Font.Height + Theme.S(6);
        int h = ControlHeight(Width);
        Control.SetBounds(0, y, Width, h);
        _hint?.SetBounds(0, y + h + Theme.S(4), Width, _hint.Font.Height);
    }
}

/// <summary>Pied de liste : « ‹ Page 2 / 5 › » à gauche, « 123 résultats » à droite (mêmes bords que la carte).</summary>
public sealed class PagerBar : LayoutControl
{
    private readonly FlatButton _prev;
    private readonly FlatButton _next;
    private readonly TextLabel _info;
    private readonly TextLabel _total;

    public PagerBar()
    {
        Height = Theme.S(40);
        _prev = new FlatButton { Kind = ButtonKind.Secondary, IconGlyph = Icons.ChevronLeft, AutoSize = false, Height = Theme.S(36) };
        _next = new FlatButton { Kind = ButtonKind.Secondary, IconGlyph = Icons.ChevronRight, AutoSize = false, Height = Theme.S(36) };
        _info = new TextLabel { Font = Theme.F(9.5f), ForeColor = Theme.TextMuted, TextAlign = ContentAlignment.MiddleCenter };
        _total = new TextLabel { Font = Theme.F(9.5f), ForeColor = Theme.TextMuted, TextAlign = ContentAlignment.MiddleRight };
        _prev.Click += (_, _) => PageRequested?.Invoke(this, Page - 1);
        _next.Click += (_, _) => PageRequested?.Invoke(this, Page + 1);
        Controls.Add(_prev);
        Controls.Add(_info);
        Controls.Add(_next);
        Controls.Add(_total);
        Visible = false;
    }

    public int Page { get; private set; } = 1;

    public event EventHandler<int>? PageRequested;

    public override Size GetPreferredSize(Size proposedSize) => new(proposedSize.Width, Theme.S(40));

    protected override void Arrange()
    {
        int b = Theme.S(36);
        int y = (Height - b) / 2;
        _prev.SetBounds(0, y, b, b);
        int infoWidth = TextRenderer.MeasureText("Page 999 / 999", _info.Font).Width + Theme.S(8);
        _info.SetBounds(b, 0, infoWidth, Height);
        _next.SetBounds(b + infoWidth, y, b, b);
        _total.SetBounds(Width / 2, 0, Width / 2, Height);
    }

    public void Update(int page, int totalPages, int totalItems, string noun = "résultat")
    {
        Page = Math.Max(1, page);
        totalPages = Math.Max(1, totalPages);
        _prev.Enabled = Page > 1;
        _next.Enabled = Page < totalPages;
        _info.Text = $"Page {Page} / {totalPages}";
        _total.Text = totalItems + " " + noun + (totalItems > 1 ? "s" : string.Empty);
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
