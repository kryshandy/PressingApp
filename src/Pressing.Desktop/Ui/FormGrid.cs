namespace Pressing.Desktop.Ui;

/// <summary>
/// Grille de formulaire à N colonnes égales (6+6 par défaut) : gouttière de 24 px, 16 px entre les rangées ;
/// les champs s'ajoutent à la suite avec retour à la ligne automatique. Les contrôles qui ne sont pas des
/// <see cref="FieldBox"/> (interrupteurs, boutons) s'alignent sur le bas de la rangée, c'est-à-dire sur les champs.
/// </summary>
public sealed class FormGrid : LayoutControl
{
    private readonly int _columns;
    private readonly List<(Control Control, int Span, int Row, int Column)> _cells = new();
    private int _col;
    private int _row;

    public FormGrid(int columns = 2)
    {
        _columns = Math.Max(1, columns);
        Dock = DockStyle.Top;
    }

    public int ColumnGap { get; set; } = Theme.Gap;

    public int RowGap { get; set; } = Theme.S(16);

    public void Add(Control control, int span = 1)
    {
        span = Math.Clamp(span, 1, _columns);
        if (_col + span > _columns)
        {
            _col = 0;
            _row++;
        }

        LayoutUtil.Prepare(control);
        _cells.Add((control, span, _row, _col));
        Controls.Add(control);
        _col += span;
        if (_col >= _columns)
        {
            _col = 0;
            _row++;
        }
    }

    public FieldBox AddField(string label, Control input, int span = 1, string? hint = null)
    {
        FieldBox field = new(label, input, hint);
        Add(field, span);
        return field;
    }

    private static bool Shown(Control c) => LayoutUtil.IsShown(c) && c is not TextLabel { Text.Length: 0 };

    private int CellWidth(int width, int span)
    {
        float column = (width - (_columns - 1f) * ColumnGap) / _columns;
        return (int)Math.Round(span * column + (span - 1) * ColumnGap);
    }

    private int CellLeft(int width, int column)
    {
        float c = (width - (_columns - 1f) * ColumnGap) / _columns;
        return (int)Math.Round(column * (c + ColumnGap));
    }

    private List<(int Row, int Height)> Rows(int width)
    {
        return _cells.Where(c => Shown(c.Control))
            .GroupBy(c => c.Row)
            .OrderBy(g => g.Key)
            .Select(g => (g.Key, g.Max(c => LayoutUtil.PreferredHeight(c.Control, CellWidth(width, c.Span)))))
            .ToList();
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        List<(int Row, int Height)> rows = Rows(width);
        return new Size(width, rows.Sum(r => r.Height) + Math.Max(0, rows.Count - 1) * RowGap);
    }

    protected override void Arrange()
    {
        foreach (var cell in _cells.Where(c => !Shown(c.Control)))
        {
            cell.Control.SetBounds(0, 0, 0, 0);
        }

        int y = 0;
        foreach ((int row, int height) in Rows(Width))
        {
            foreach (var cell in _cells.Where(c => c.Row == row && Shown(c.Control)))
            {
                int w = CellWidth(Width, cell.Span);
                int h = LayoutUtil.PreferredHeight(cell.Control, w);
                int top = cell.Control is FieldBox ? y
                    : h < Theme.ControlHeight ? y + height - (Theme.ControlHeight + h) / 2 : y + height - h;
                cell.Control.SetBounds(CellLeft(Width, cell.Column), top, w, h);
            }

            y += height + RowGap;
        }
    }
}

public static class Summary
{
    /// <summary>Bandeau de chiffres clés (libellé discret + valeur) pour le haut d'un dialogue : colonnes égales.</summary>
    public static Control Strip(params (string Label, string Value, Color? Color)[] items) => new StatStrip(items);

    private sealed class StatStrip : RoundedPanel
    {
        private readonly (string Label, string Value, Color? Color)[] _items;

        public StatStrip((string Label, string Value, Color? Color)[] items)
        {
            _items = items;
            Height = Theme.S(84);
        }

        public override Size GetPreferredSize(Size proposedSize) => new(proposedSize.Width, Theme.S(84));

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_items.Length == 0)
            {
                return;
            }

            Font label = Theme.FieldLabel;
            Font value = Theme.Semi(14f);
            int pad = Theme.S(20);
            int width = (Width - 2 * pad) / _items.Length;
            int y = (Height - label.Height - Theme.S(6) - value.Height) / 2;
            TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            for (int i = 0; i < _items.Length; i++)
            {
                int x = pad + i * width;
                TextRenderer.DrawText(e.Graphics, _items[i].Label.ToUpperInvariant(), label, new Rectangle(x, y, width - Theme.S(12), label.Height), Theme.TextMuted, flags);
                TextRenderer.DrawText(e.Graphics, _items[i].Value, value, new Rectangle(x, y + label.Height + Theme.S(6), width - Theme.S(12), value.Height),
                    _items[i].Color ?? Theme.Figure, flags);
            }
        }
    }
}
