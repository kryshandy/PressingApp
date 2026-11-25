namespace Pressing.Desktop.Ui;

/// <summary>Grille de formulaire à N colonnes égales : les champs s'ajoutent à la suite avec retour à la ligne automatique.</summary>
public sealed class FormGrid : TableLayoutPanel
{
    private readonly int _columns;
    private int _col;
    private int _row;

    public FormGrid(int columns = 2)
    {
        _columns = columns;
        ColumnCount = columns;
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = Padding.Empty;
        for (int i = 0; i < columns; i++)
        {
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
        }
    }

    public void Add(Control control, int span = 1)
    {
        span = Math.Min(span, _columns);
        if (_col + span > _columns)
        {
            _col = 0;
            _row++;
        }

        while (RowStyles.Count <= _row)
        {
            RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        Controls.Add(control, _col, _row);
        SetColumnSpan(control, span);
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
}

public static class Summary
{
    /// <summary>Bandeau de chiffres clés (libellé discret + valeur) pour le haut d'un dialogue.</summary>
    public static Panel Strip(params (string Label, string Value, Color? Color)[] items)
    {
        RoundedPanel panel = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            Padding = new Padding(Theme.S(16), Theme.S(10), Theme.S(16), Theme.S(10))
        };
        Panel wrap = new() { Dock = DockStyle.Top, Height = Theme.S(76 + 16), Padding = new Padding(0, 0, 0, Theme.S(16)) };
        wrap.Controls.Add(panel);
        TableLayoutPanel table = new() { Dock = DockStyle.Fill, ColumnCount = items.Length, RowCount = 1 };
        foreach ((string label, string value, Color? color) in items)
        {
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / items.Length));
            TableLayoutPanel cell = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            cell.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(20)));
            cell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            cell.Controls.Add(new Label
            {
                Text = label.ToUpperInvariant(),
                Dock = DockStyle.Fill,
                Font = Theme.F(8f, FontStyle.Bold),
                ForeColor = Theme.TextMuted,
                TextAlign = ContentAlignment.BottomLeft
            }, 0, 0);
            cell.Controls.Add(new Label
            {
                Text = value,
                Dock = DockStyle.Fill,
                Font = Theme.F(13f, FontStyle.Bold),
                ForeColor = color ?? Theme.Text,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            }, 0, 1);
            table.Controls.Add(cell, table.ColumnStyles.Count - 1, 0);
        }

        panel.Controls.Add(table);
        return wrap;
    }
}
