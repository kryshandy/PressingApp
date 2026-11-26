using System.Drawing.Drawing2D;

namespace Pressing.Desktop.Ui;

public enum ButtonKind
{
    /// <summary>Action principale : laiton / or.</summary>
    Primary,
    /// <summary>Action secondaire : surface avec bordure.</summary>
    Secondary,
    /// <summary>Action destructive.</summary>
    Danger,
    /// <summary>Sans fond (icône ou lien).</summary>
    Ghost,
    /// <summary>Action bleue (liens / sélection).</summary>
    Info
}

/// <summary>Bouton plat arrondi, avec icône facultative, états survol / appui / désactivé / occupé.</summary>
public class FlatButton : Button
{
    private ButtonKind _kind = ButtonKind.Primary;
    private string _icon = string.Empty;
    private bool _hover;
    private bool _down;
    private bool _busy;

    public FlatButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Font = Theme.F(10f, FontStyle.Bold);
        Height = Theme.S(40);
        Cursor = Cursors.Hand;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = new Padding(0, 0, Theme.S(8), 0);
        UseVisualStyleBackColor = false;
    }

    public ButtonKind Kind
    {
        get => _kind;
        set { _kind = value; Invalidate(); }
    }

    /// <summary>Glyphe de la police d'icônes (voir <see cref="Icons"/>).</summary>
    public string IconGlyph
    {
        get => _icon;
        set { _icon = value ?? string.Empty; Invalidate(); }
    }

    public int Radius { get; set; } = 10;

    /// <summary>Quand vrai (appel réseau en cours), le bouton est grisé et ignore les clics sans changer <c>Enabled</c>.</summary>
    public bool Busy
    {
        get => _busy;
        set { _busy = value; Cursor = value ? Cursors.WaitCursor : Cursors.Hand; Invalidate(); }
    }

    /// <summary>Déclenche le clic même si le bouton est masqué (menu « Plus d'actions »).</summary>
    public void RaiseClick()
    {
        if (Enabled)
        {
            OnClick(EventArgs.Empty);
        }
    }

    protected override void OnClick(EventArgs e)
    {
        if (_busy)
        {
            return;
        }

        base.OnClick(e);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        Size text = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        int width = Theme.S(36) + text.Width;
        if (_icon.Length > 0)
        {
            width += Theme.S(26);
            if (Text.Length == 0)
            {
                width -= Theme.S(8);
            }
        }

        return new Size(Math.Max(width, Theme.S(44)), Math.Max(Height, Theme.S(40)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Theme.BackOf(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;

        bool disabled = !Enabled || _busy;
        (Color fill, Color fore, Color? border) = Palette(disabled);

        Rectangle rect = new(0, 0, Width - 1, Height - 1);
        using (GraphicsPath path = Theme.RoundedRect(rect, Theme.S(Radius)))
        {
            using SolidBrush brush = new(fill);
            g.FillPath(brush, path);
            if (border is { } b)
            {
                using Pen pen = new(b, 1f);
                g.DrawPath(pen, path);
            }

            if (Focused && ShowFocusCues && !disabled)
            {
                Rectangle inner = Rectangle.Inflate(rect, -Theme.S(2), -Theme.S(2));
                using GraphicsPath focus = Theme.RoundedRect(inner, Theme.S(Radius - 2));
                using Pen pen = new(Theme.Alpha(Theme.Blue, 200), 2f);
                g.DrawPath(pen, focus);
            }
        }

        // Contenu : icône + texte centrés ensemble.
        TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
                                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        Size textSize = Text.Length == 0
            ? Size.Empty
            : TextRenderer.MeasureText(g, Text, Font, new Size(int.MaxValue, int.MaxValue), flags);
        Size iconSize = _icon.Length == 0
            ? Size.Empty
            : TextRenderer.MeasureText(g, _icon, Theme.Icon(11f), new Size(int.MaxValue, int.MaxValue), flags);
        int gap = _icon.Length > 0 && Text.Length > 0 ? Theme.S(10) : 0;
        int total = iconSize.Width + gap + textSize.Width;
        int x = Math.Max(Theme.S(8), (Width - total) / 2);
        if (_busy)
        {
            // Petit indicateur de chargement à la place de l'icône.
            DrawSpinner(g, new Rectangle(x, (Height - Theme.S(14)) / 2, Theme.S(14), Theme.S(14)), fore);
            x += Theme.S(14) + Theme.S(8);
            total = Theme.S(22) + textSize.Width;
        }
        else if (_icon.Length > 0)
        {
            TextRenderer.DrawText(g, _icon, Theme.Icon(11f), new Rectangle(x, 0, iconSize.Width + 2, Height), fore, flags);
            x += iconSize.Width + gap;
        }

        if (Text.Length > 0)
        {
            int available = Math.Max(1, Width - x - Theme.S(8));
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, available, Height), fore, flags);
        }
    }

    private static void DrawSpinner(Graphics g, Rectangle rect, Color color)
    {
        using Pen pen = new(color, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        float start = (Environment.TickCount % 1000) / 1000f * 360f;
        g.DrawArc(pen, rect, start, 270);
    }

    private (Color Fill, Color Fore, Color? Border) Palette(bool disabled)
    {
        if (disabled)
        {
            return _kind == ButtonKind.Ghost
                ? (Theme.BackOf(this), Theme.TextFaint, null)
                : (Theme.Mix(Theme.BackOf(this), Theme.Border, 0.55f), Theme.TextFaint, null);
        }

        return _kind switch
        {
            ButtonKind.Primary => (_down ? Theme.AccentPressed : _hover ? Theme.AccentHover : Theme.Accent, Theme.OnAccent, null),
            ButtonKind.Danger => (_down ? Theme.Mix(Theme.Red, Color.Black, 0.25f) : _hover ? Theme.Mix(Theme.Red, Color.White, 0.12f) : Theme.Red, Color.White, null),
            ButtonKind.Info => (_down ? Theme.Mix(Theme.Blue, Color.Black, 0.25f) : _hover ? Theme.Mix(Theme.Blue, Color.White, 0.12f) : Theme.Blue, Color.White, null),
            ButtonKind.Ghost => (_down ? Theme.SurfaceHover : _hover ? Theme.Alpha(Theme.SurfaceHover, 255) : (Theme.BackOf(this)),
                _hover ? Theme.Text : Theme.TextMuted, null),
            _ => (_down ? Theme.SurfaceHover : _hover ? Theme.SurfaceHover : Theme.SurfaceAlt, Theme.Text, Theme.Border)
        };
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs mevent) { _down = true; Invalidate(); base.OnMouseDown(mevent); }
    protected override void OnMouseUp(MouseEventArgs mevent) { _down = false; Invalidate(); base.OnMouseUp(mevent); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnParentBackColorChanged(EventArgs e) { Invalidate(); base.OnParentBackColorChanged(e); }
}

/// <summary>Entrée de la barre latérale : icône, libellé, état actif/survol, pastille de compteur facultative.</summary>
public sealed class NavButton : Control
{
    private bool _hover;
    private bool _active;
    private int _badge;

    public NavButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        Font = Theme.F(10f);
        Height = Theme.S(38);
        Margin = new Padding(0, Theme.S(1), 0, Theme.S(1));
    }

    public string Glyph { get; set; } = string.Empty;

    /// <summary>Clé de navigation associée (voir <c>NavRegistry</c>).</summary>
    public string Key { get; set; } = string.Empty;

    public bool Active
    {
        get => _active;
        set { _active = value; Invalidate(); }
    }

    public int Badge
    {
        get => _badge;
        set { _badge = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Theme.Sidebar);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle rect = new(0, 0, Width - 1, Height - 1);

        if (_active || _hover || Focused)
        {
            using GraphicsPath path = Theme.RoundedRect(rect, Theme.S(10));
            using SolidBrush fill = new(_active ? Theme.Mix(Theme.Sidebar, Theme.Accent, 0.16f) : Theme.Surface);
            g.FillPath(fill, path);
        }

        if (_active)
        {
            using SolidBrush bar = new(Theme.Accent);
            Rectangle strip = new(0, Theme.S(10), Theme.S(3), Height - Theme.S(20));
            using GraphicsPath p = Theme.RoundedRect(strip, Theme.S(2));
            g.FillPath(bar, p);
        }

        Color fore = _active ? Theme.Accent : _hover ? Theme.Text : Theme.TextMuted;
        TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
                                TextFormatFlags.SingleLine;
        TextRenderer.DrawText(g, Glyph, Theme.Icon(12f), new Rectangle(Theme.S(14), 0, Theme.S(28), Height), fore,
            flags | TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(g, Text, _active ? Theme.F(10f, FontStyle.Bold) : Font,
            new Rectangle(Theme.S(50), 0, Math.Max(1, Width - Theme.S(50) - (_badge > 0 ? Theme.S(40) : Theme.S(10))), Height),
            _active ? Theme.Text : fore, flags | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

        if (_badge > 0)
        {
            string text = _badge > 99 ? "99+" : _badge.ToString();
            Size size = TextRenderer.MeasureText(text, Theme.F(8.5f, FontStyle.Bold), new Size(int.MaxValue, int.MaxValue), flags);
            int w = Math.Max(Theme.S(22), size.Width + Theme.S(12));
            Rectangle pill = new(Width - w - Theme.S(12), (Height - Theme.S(20)) / 2, w, Theme.S(20));
            using GraphicsPath path = Theme.RoundedRect(pill, Theme.S(10));
            using SolidBrush brush = new(Theme.Red);
            g.FillPath(brush, path);
            TextRenderer.DrawText(g, text, Theme.F(8.5f, FontStyle.Bold), pill, Color.White,
                flags | TextFormatFlags.HorizontalCenter);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }
}
