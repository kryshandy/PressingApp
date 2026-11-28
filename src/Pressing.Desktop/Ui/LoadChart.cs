using System.Drawing.Drawing2D;
using System.Globalization;
using Pressing.Core;
using Pressing.Core.Dtos;

namespace Pressing.Desktop.Ui;

/// <summary>
/// Charge prévisionnelle de l'atelier : une barre par jour (articles attendus), ligne pointillée de capacité,
/// couleur selon le taux de charge (vert &lt; 70 %, ambre jusqu'à la capacité, rouge au-delà = surcharge) et info-bulle
/// au survol avec le détail par service. <see cref="Compact"/> : version du tableau de bord (sans graduations).
/// </summary>
public sealed class WorkshopLoadChart : Control
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private WorkshopLoadDto? _load;
    private int _hover = -1;

    public WorkshopLoadChart()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Surface;
        MinimumSize = new Size(Theme.S(240), Theme.S(96));
    }

    /// <summary>Version réduite (tableau de bord) : pas de graduations ni de valeurs au-dessus des barres.</summary>
    public bool Compact { get; set; }

    /// <summary>Hauteur préférée du graphique (px logiques) dans une carte.</summary>
    public int DesiredHeight { get; set; } = 188;

    public void SetData(WorkshopLoadDto? load)
    {
        _load = load;
        _hover = -1;
        Invalidate();
    }

    public override Size GetPreferredSize(Size proposedSize) =>
        new(proposedSize.Width > 0 ? proposedSize.Width : Width, Theme.S(DesiredHeight));

    /// <summary>Couleur d'une barre selon le taux de charge.</summary>
    public static Color RateColor(double rate) => rate > 1.0 ? Theme.Red : rate >= 0.7 ? Theme.Amber : Theme.Green;

    private Rectangle PlotArea
    {
        get
        {
            int left = Compact ? Theme.S(4) : Theme.S(40);
            int top = Compact ? Theme.S(6) : Theme.S(22);
            int bottom = Compact ? Theme.S(34) : Theme.S(42);
            return new Rectangle(left, top, Math.Max(10, Width - left - Theme.S(8)), Math.Max(10, Height - top - bottom));
        }
    }

    private int ScaleMax()
    {
        if (_load is null)
        {
            return 10;
        }

        int peak = Math.Max(_load.Days.Count == 0 ? 0 : _load.Days.Max(d => d.Items), _load.DailyCapacity);
        return Math.Max(10, (int)Math.Ceiling(peak * 1.12));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
        Rectangle plot = PlotArea;

        if (_load is null || _load.Days.Count == 0)
        {
            TextRenderer.DrawText(g, "Aucune donnée", Theme.F(10f), plot, Theme.TextMuted, flags | TextFormatFlags.HorizontalCenter);
            return;
        }

        int max = ScaleMax();
        using (Pen baseline = new(Theme.Border, 1f))
        {
            g.DrawLine(baseline, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
        }

        if (!Compact)
        {
            foreach (int tick in new[] { max / 2, max })
            {
                int y = plot.Bottom - (int)(plot.Height * (tick / (double)max));
                using Pen grid = new(Theme.Mix(Theme.Surface, Theme.Border, 0.4f), 1f) { DashStyle = DashStyle.Dash };
                g.DrawLine(grid, plot.Left, y, plot.Right, y);
                TextRenderer.DrawText(g, tick.ToString(Fr), Theme.F(8.5f),
                    new Rectangle(0, y - Theme.S(9), plot.Left - Theme.S(6), Theme.S(18)), Theme.TextFaint, flags | TextFormatFlags.Right);
            }
        }

        // Ligne de capacité, sous les barres (elles la recouvrent) et son étiquette à droite.
        int capacityY = plot.Bottom - (int)(plot.Height * (_load.DailyCapacity / (double)max));
        using (Pen capacity = new(Theme.Accent, 1.4f) { DashStyle = DashStyle.Dash })
        {
            g.DrawLine(capacity, plot.Left, capacityY, plot.Right, capacityY);
        }

        if (!Compact)
        {
            string label = $"Capacité {_load.DailyCapacity}";
            Size size = TextRenderer.MeasureText(label, Theme.F(8.5f, FontStyle.Bold));
            Rectangle tag = new(plot.Right - size.Width - Theme.S(8), capacityY - size.Height - Theme.S(2), size.Width + Theme.S(4), size.Height);
            using (SolidBrush back = new(Theme.Surface))
            {
                g.FillRectangle(back, tag);
            }

            TextRenderer.DrawText(g, label, Theme.F(8.5f, FontStyle.Bold), tag, Theme.Accent,
                TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        int count = _load.Days.Count;
        float slot = plot.Width / (float)count;
        float barWidth = Math.Min(slot * (Compact ? 0.62f : 0.56f), Theme.S(44));
        for (int i = 0; i < count; i++)
        {
            WorkshopLoadDayDto day = _load.Days[i];
            Color color = RateColor(day.LoadRate);
            float height = day.Items / (float)max * plot.Height;
            float x = plot.Left + slot * i + (slot - barWidth) / 2;
            bool hot = i == _hover;

            if (day.Items > 0)
            {
                RectangleF bar = new(x, plot.Bottom - Math.Max(height, 3f), barWidth, Math.Max(height, 3f));
                using GraphicsPath path = Theme.TopRoundedRect(bar, Math.Min(barWidth / 2.4f, Theme.S(8)));
                using SolidBrush brush = new(hot ? Theme.Mix(color, Color.White, 0.18f) : color);
                g.FillPath(brush, path);
            }
            else
            {
                using SolidBrush stub = new(Theme.Mix(Theme.Surface, Theme.Border, 0.9f));
                g.FillRectangle(stub, x, plot.Bottom - 3, barWidth, 3);
            }

            if (!Compact)
            {
                string value = day.Overloaded ? "! " + day.Items : day.Items.ToString(Fr);
                if (day.Items > 0 || day.IsToday)
                {
                    float top = plot.Bottom - Math.Max(height, 3f) - Theme.S(18);
                    Rectangle valueRect = new((int)(plot.Left + slot * i), (int)Math.Max(0, top), (int)slot, Theme.S(16));
                    Size valueSize = TextRenderer.MeasureText(value, Theme.F(9f, FontStyle.Bold));
                    using (SolidBrush back = new(Theme.Surface))
                    {
                        // Fond uni : la ligne de capacité ne barre pas le chiffre.
                        g.FillRectangle(back, valueRect.X + (valueRect.Width - valueSize.Width) / 2 - 2, valueRect.Y + 1, valueSize.Width + 4, valueRect.Height - 2);
                    }

                    TextRenderer.DrawText(g, value, Theme.F(9f, day.Overloaded ? FontStyle.Bold : FontStyle.Regular), valueRect,
                        day.Overloaded ? Theme.Red : Theme.TextMuted, flags | TextFormatFlags.HorizontalCenter);
                }
            }
            else if (day.Overloaded)
            {
                TextRenderer.DrawText(g, "!", Theme.F(9f, FontStyle.Bold),
                    new Rectangle((int)(plot.Left + slot * i), plot.Top, (int)slot, Theme.S(14)), Theme.Red,
                    flags | TextFormatFlags.HorizontalCenter);
            }

            string[] parts = day.Label.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            Color labelColor = day.IsToday ? Theme.Accent : hot ? Theme.Text : Theme.TextMuted;
            FontStyle style = day.IsToday || hot ? FontStyle.Bold : FontStyle.Regular;
            Rectangle slotRect = new((int)(plot.Left + slot * i), plot.Bottom + Theme.S(5), (int)slot, Theme.S(16));
            TextRenderer.DrawText(g, parts[0], Theme.F(Compact ? 8f : 9f, style), slotRect, labelColor, flags | TextFormatFlags.HorizontalCenter);
            if (parts.Length > 1)
            {
                slotRect.Y += Theme.S(Compact ? 13 : 15);
                TextRenderer.DrawText(g, parts[1], Theme.F(Compact ? 8f : 8.5f, style), slotRect, labelColor,
                    flags | TextFormatFlags.HorizontalCenter);
            }
        }

        if (_hover >= 0 && _hover < count)
        {
            DrawTooltip(g, _load.Days[_hover], plot, slot, max);
        }
    }

    private void DrawTooltip(Graphics g, WorkshopLoadDayDto day, Rectangle plot, float slot, int max)
    {
        string services = string.Join(" · ", day.ByService.Where(s => s.Items > 0).Select(s => $"{s.Label} {s.Items}"));
        string text = $"{Fr.TextInfo.ToTitleCase(day.Date.ToString("dddd d MMMM", Fr))}\n"
                      + $"{day.Items} / {day.Capacity} article(s) — {Math.Round(day.LoadRate * 100)} %"
                      + (day.Overloaded ? " — surcharge" : string.Empty)
                      + (services.Length > 0 ? "\n" + services : string.Empty);
        Size size = TextRenderer.MeasureText(text, Theme.F(9f, FontStyle.Bold), new Size(Theme.S(340), Theme.S(120)), TextFormatFlags.WordBreak);
        int w = size.Width + Theme.S(20);
        int h = size.Height + Theme.S(14);
        float cx = plot.Left + slot * _hover + slot / 2;
        float top = plot.Bottom - day.Items / (float)max * plot.Height - h - Theme.S(26);
        Rectangle tip = new((int)Math.Clamp(cx - w / 2f, 2, Math.Max(2, Width - w - 2)), (int)Math.Max(2, top), w, h);
        using GraphicsPath path = Theme.RoundedRect(tip, Theme.S(8));
        using SolidBrush back = new(Theme.Sidebar);
        using Pen pen = new(day.Overloaded ? Theme.Red : Theme.Border);
        g.FillPath(back, path);
        g.DrawPath(pen, path);
        TextRenderer.DrawText(g, text, Theme.F(9f, FontStyle.Bold), tip, Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hover = -1;
        Rectangle plot = PlotArea;
        if (_load is { Days.Count: > 0 } && e.X >= plot.Left && e.X <= plot.Right)
        {
            hover = Math.Clamp((int)((e.X - plot.Left) / (plot.Width / (float)_load.Days.Count)), 0, _load.Days.Count - 1);
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

    /// <summary>Phrase de synthèse commune à l'Atelier et au tableau de bord (surcharges, retards).</summary>
    public static string Summary(WorkshopLoadDto load, out bool attention)
    {
        List<string> parts = new() { $"{load.TotalItems} article(s) attendus sur {load.DaysCount} jours" };
        attention = false;
        if (load.OverloadedDays > 0)
        {
            attention = true;
            IEnumerable<string> days = load.Days.Where(d => d.Overloaded).Take(3).Select(d => d.Label);
            string more = load.OverloadedDays > 3 ? "…" : string.Empty;
            parts.Add($"{load.OverloadedDays} jour(s) en surcharge ({string.Join(", ", days)}{more})");
        }

        if (load.OverdueItems > 0)
        {
            attention = true;
            parts.Add($"{load.OverdueItems} article(s) en retard ({load.OverdueOrders} commande(s))");
        }

        if (!attention)
        {
            parts.Add("aucune surcharge ni retard");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>Totaux par service sur la période : « Lavage 12 · Repassage 4 »… (services à zéro omis).</summary>
    public static string ServiceTotals(WorkshopLoadDto load)
    {
        string text = string.Join(" · ", load.TotalsByService.Where(s => s.Items > 0).Select(s => $"{s.Label} {s.Items}"));
        return text.Length == 0 ? "Aucun article attendu sur la période." : "Par service : " + text;
    }
}
