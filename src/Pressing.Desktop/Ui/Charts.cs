using System.Drawing.Drawing2D;
using Pressing.Client;
using Pressing.Core.Dtos;

namespace Pressing.Desktop.Ui;

/// <summary>Histogramme des 7 derniers jours dessiné en GDI+ (barres arrondies, valeurs, info-bulle au survol).</summary>
public sealed class RevenueChart : Control
{
    private IReadOnlyList<RevenuePointDto> _points = Array.Empty<RevenuePointDto>();
    private int _hover = -1;

    public RevenueChart()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        MinimumSize = new Size(Theme.S(240), Theme.S(180));
    }

    public void SetData(IReadOnlyList<RevenuePointDto> points)
    {
        _points = points;
        _hover = -1;
        Invalidate();
    }

    private Rectangle PlotArea => new(Theme.S(56), Theme.S(18), Math.Max(10, Width - Theme.S(56) - Theme.S(12)),
        Math.Max(10, Height - Theme.S(18) - Theme.S(52)));

    private static decimal NiceMax(decimal max)
    {
        if (max <= 0)
        {
            return 1000;
        }

        double magnitude = Math.Pow(10, Math.Floor(Math.Log10((double)max)));
        double normalized = (double)max / magnitude;
        double nice = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
        return (decimal)(nice * magnitude);
    }

    private static string Short(decimal value) => value switch
    {
        >= 1_000_000m => (value / 1_000_000m).ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) + " M",
        >= 1_000m => (value / 1_000m).ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) + " k",
        _ => value.ToString("0")
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle plot = PlotArea;
        decimal max = NiceMax(_points.Count == 0 ? 0 : _points.Max(p => p.Revenue));
        TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;

        // Grille horizontale et graduations.
        const int lines = 4;
        for (int i = 0; i <= lines; i++)
        {
            int y = plot.Bottom - (int)(plot.Height * (i / (double)lines));
            using Pen grid = new(Theme.Mix(Theme.Surface, Theme.Border, i == 0 ? 1f : 0.55f), 1f)
            {
                DashStyle = i == 0 ? DashStyle.Solid : DashStyle.Dash
            };
            g.DrawLine(grid, plot.Left, y, plot.Right, y);
            TextRenderer.DrawText(g, Short(max * i / lines), Theme.F(8.5f), new Rectangle(0, y - Theme.S(9), plot.Left - Theme.S(8), Theme.S(18)),
                Theme.TextFaint, flags | TextFormatFlags.Right);
        }

        if (_points.Count == 0)
        {
            TextRenderer.DrawText(g, "Aucune donnée", Theme.F(10f), plot, Theme.TextMuted,
                flags | TextFormatFlags.HorizontalCenter);
            return;
        }

        float slot = plot.Width / (float)_points.Count;
        float barWidth = Math.Min(slot * 0.52f, Theme.S(54));
        for (int i = 0; i < _points.Count; i++)
        {
            RevenuePointDto p = _points[i];
            float height = (float)(p.Revenue / max) * plot.Height;
            float x = plot.Left + slot * i + (slot - barWidth) / 2;
            RectangleF bar = new(x, plot.Bottom - height, barWidth, Math.Max(height, p.Revenue > 0 ? 3f : 0f));
            bool hot = i == _hover;
            if (bar.Height > 0)
            {
                using GraphicsPath path = Theme.TopRoundedRect(bar, Math.Min(barWidth / 2.2f, Theme.S(9)));
                using LinearGradientBrush brush = new(new RectangleF(bar.X, bar.Y - 1, bar.Width, bar.Height + 2),
                    hot ? Theme.AccentHover : Theme.Accent,
                    Theme.Mix(Theme.Accent, Theme.Surface, hot ? 0.35f : 0.55f), 90f);
                g.FillPath(brush, path);
            }

            string[] parts = p.Label.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            Rectangle slotRect = new((int)(plot.Left + slot * i), plot.Bottom + Theme.S(6), (int)slot, Theme.S(18));
            TextRenderer.DrawText(g, parts[0], Theme.F(9f, hot ? FontStyle.Bold : FontStyle.Regular), slotRect,
                hot ? Theme.Text : Theme.TextMuted, flags | TextFormatFlags.HorizontalCenter);
            if (parts.Length > 1)
            {
                slotRect.Y += Theme.S(17);
                TextRenderer.DrawText(g, parts[1], Theme.F(8.5f), slotRect, Theme.TextFaint, flags | TextFormatFlags.HorizontalCenter);
            }
        }

        if (_hover >= 0 && _hover < _points.Count)
        {
            RevenuePointDto p = _points[_hover];
            string text = $"{p.Label} {Fmt.Date(p.Date)}\n{Fmt.Money(p.Revenue)} — {p.Orders} commande(s)";
            Size size = TextRenderer.MeasureText(text, Theme.F(9f, FontStyle.Bold), new Size(Theme.S(260), Theme.S(100)), TextFormatFlags.WordBreak);
            int w = size.Width + Theme.S(20);
            int h = size.Height + Theme.S(14);
            float cx = plot.Left + slot * _hover + slot / 2;
            float top = plot.Bottom - (float)(p.Revenue / max) * plot.Height - h - Theme.S(10);
            Rectangle tip = new((int)Math.Clamp(cx - w / 2f, 2, Width - w - 2), (int)Math.Max(2, top), w, h);
            using GraphicsPath path = Theme.RoundedRect(tip, Theme.S(8));
            using SolidBrush back = new(Theme.Sidebar);
            using Pen pen = new(Theme.Border);
            g.FillPath(back, path);
            g.DrawPath(pen, path);
            TextRenderer.DrawText(g, text, Theme.F(9f, FontStyle.Bold), tip, Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hover = -1;
        Rectangle plot = PlotArea;
        if (_points.Count > 0 && e.X >= plot.Left && e.X <= plot.Right)
        {
            hover = (int)((e.X - plot.Left) / (plot.Width / (float)_points.Count));
            hover = Math.Clamp(hover, 0, _points.Count - 1);
        }

        if (hover != _hover)
        {
            _hover = hover;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = -1;
        Invalidate();
        base.OnMouseLeave(e);
    }
}

/// <summary>Répartition par statut : barres horizontales arrondies colorées avec compteurs.</summary>
public sealed class StatusBars : Control
{
    private IReadOnlyList<StatusCountDto> _items = Array.Empty<StatusCountDto>();

    public StatusBars()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        MinimumSize = new Size(Theme.S(200), Theme.S(120));
    }

    public void SetData(IReadOnlyList<StatusCountDto> items)
    {
        _items = items;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        if (_items.Count == 0)
        {
            TextRenderer.DrawText(g, "Aucune commande", Theme.F(10f), ClientRectangle, Theme.TextMuted,
                flags | TextFormatFlags.HorizontalCenter);
            return;
        }

        int max = Math.Max(1, _items.Max(i => i.Count));
        int rowHeight = Math.Min(Theme.S(44), Math.Max(Theme.S(28), Height / Math.Max(1, _items.Count)));
        int labelWidth = Theme.S(130);
        int valueWidth = Theme.S(40);
        int barLeft = labelWidth + Theme.S(8);
        int barWidth = Math.Max(10, Width - barLeft - valueWidth - Theme.S(8));
        for (int i = 0; i < _items.Count; i++)
        {
            StatusCountDto item = _items[i];
            int y = i * rowHeight;
            Color color = Theme.FromHex(item.Color);
            TextRenderer.DrawText(g, item.Label, Theme.F(10f), new Rectangle(0, y, labelWidth, rowHeight), Theme.Text, flags);
            Rectangle track = new(barLeft, y + rowHeight / 2 - Theme.S(5), barWidth, Theme.S(10));
            using (GraphicsPath path = Theme.RoundedRect(track, Theme.S(5)))
            using (SolidBrush brush = new(Theme.SurfaceAlt))
            {
                g.FillPath(brush, path);
            }

            if (item.Count > 0)
            {
                Rectangle fill = new(track.X, track.Y, Math.Max(track.Height, (int)(track.Width * (item.Count / (double)max))), track.Height);
                using GraphicsPath path = Theme.RoundedRect(fill, Theme.S(5));
                using SolidBrush brush = new(color);
                g.FillPath(brush, path);
            }

            TextRenderer.DrawText(g, item.Count.ToString(), Theme.F(10f, FontStyle.Bold),
                new Rectangle(Width - valueWidth, y, valueWidth, rowHeight), Theme.Text, flags | TextFormatFlags.Right);
        }
    }
}
