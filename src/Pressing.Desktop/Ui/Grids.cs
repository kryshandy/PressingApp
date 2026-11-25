using System.Drawing.Drawing2D;

namespace Pressing.Desktop.Ui;

/// <summary>Ligne de grille pouvant afficher des pastilles colorées (statut, express…).</summary>
public interface IPillRow
{
    /// <summary>Texte et couleur de la pastille pour la colonne demandée, ou null pour un affichage texte normal.</summary>
    (string Text, Color Color)? GetPill(string columnName);
}

/// <summary>Ligne de grille dont certaines cellules ont une couleur de texte particulière (retard, solde dû…).</summary>
public interface ITintRow
{
    Color? GetTint(string columnName);
}

/// <summary>DataGridView sombre, sans bordure lourde, lignes de 36 px, double tampon.</summary>
public sealed class ThemedGrid : DataGridView
{
    public ThemedGrid()
    {
        DoubleBuffered = true;
        AutoGenerateColumns = false;
        BackgroundColor = Theme.Surface;
        BorderStyle = BorderStyle.None;
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        GridColor = Theme.Mix(Theme.Surface, Theme.Border, 0.55f);
        EnableHeadersVisualStyles = false;
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        ColumnHeadersHeight = Theme.S(38);
        ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Theme.SurfaceAlt,
            ForeColor = Theme.TextMuted,
            SelectionBackColor = Theme.SurfaceAlt,
            SelectionForeColor = Theme.TextMuted,
            Font = Theme.F(8.5f, FontStyle.Bold),
            Padding = new Padding(Theme.S(10), 0, Theme.S(10), 0),
            Alignment = DataGridViewContentAlignment.MiddleLeft,
            WrapMode = DataGridViewTriState.False
        };
        DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            SelectionBackColor = Theme.SelectionBack,
            SelectionForeColor = Color.White,
            Font = Theme.F(10f),
            Padding = new Padding(Theme.S(10), 0, Theme.S(10), 0),
            WrapMode = DataGridViewTriState.False
        };
        AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Theme.Mix(Theme.Surface, Theme.SurfaceAlt, 0.55f),
            SelectionBackColor = Theme.SelectionBack,
            SelectionForeColor = Color.White
        };
        RowHeadersVisible = false;
        RowTemplate.Height = Theme.S(36);
        AllowUserToAddRows = false;
        AllowUserToDeleteRows = false;
        AllowUserToResizeRows = false;
        AllowUserToResizeColumns = true;
        AllowUserToOrderColumns = false;
        MultiSelect = false;
        SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        ReadOnly = true;
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        ScrollBars = ScrollBars.Both;
        ShowCellToolTips = true;
        StandardTab = true;
        TabStop = true;
    }

    /// <summary>Remplace la source de données en conservant la ligne sélectionnée quand c'est possible.</summary>
    public void SetRows<T>(IList<T> rows)
    {
        int first = FirstDisplayedScrollingRowIndex;
        DataSource = null;
        DataSource = rows;
        if (Rows.Count > 0)
        {
            ClearSelection();
            if (first > 0 && first < Rows.Count)
            {
                try
                {
                    FirstDisplayedScrollingRowIndex = first;
                }
                catch (InvalidOperationException)
                {
                    // Grille pas encore affichée : le défilement reste en haut.
                }
            }
        }
    }

    public T? SelectedItem<T>() where T : class
    {
        if (CurrentRow?.DataBoundItem is T item)
        {
            return item;
        }

        return null;
    }

    /// <summary>Quand la grille n'a pas de défilement propre, la molette fait défiler le conteneur parent.</summary>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (!VerticalScrollBar.Visible)
        {
            for (Control? c = Parent; c is not null; c = c.Parent)
            {
                if (c is ScrollableControl { AutoScroll: true, VerticalScroll.Visible: true } sc)
                {
                    int target = Math.Clamp(sc.VerticalScroll.Value - e.Delta / 120 * Theme.S(70),
                        sc.VerticalScroll.Minimum, sc.VerticalScroll.Maximum);
                    sc.AutoScrollPosition = new Point(sc.HorizontalScroll.Value, target);
                    if (e is HandledMouseEventArgs handled)
                    {
                        handled.Handled = true;
                    }

                    return;
                }
            }
        }

        base.OnMouseWheel(e);
    }

    protected override void OnCellFormatting(DataGridViewCellFormattingEventArgs e)
    {
        base.OnCellFormatting(e);
        if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && e.CellStyle is not null &&
            Rows[e.RowIndex].DataBoundItem is ITintRow tint && tint.GetTint(Columns[e.ColumnIndex].Name) is { } color)
        {
            e.CellStyle.ForeColor = color;
            e.CellStyle.SelectionForeColor = color;
        }
    }

    protected override void OnCellPainting(DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && Rows[e.RowIndex].DataBoundItem is IPillRow pills &&
            pills.GetPill(Columns[e.ColumnIndex].Name) is { } pill)
        {
            e.Paint(e.ClipBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border | DataGridViewPaintParts.SelectionBackground);
            if (e.Graphics is not null)
            {
                Size text = TextRenderer.MeasureText(pill.Text, Theme.F(8.5f, FontStyle.Bold), new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
                int w = Math.Min(e.CellBounds.Width - Theme.S(12), text.Width + Theme.S(24));
                int h = Theme.S(24);
                Rectangle rect = new(e.CellBounds.X + Theme.S(10), e.CellBounds.Y + (e.CellBounds.Height - h) / 2, Math.Max(w, Theme.S(20)), h);
                Color under = e.State.HasFlag(DataGridViewElementStates.Selected)
                    ? Theme.SelectionBack
                    : e.CellStyle?.BackColor ?? Theme.Surface;
                PillPainter.Draw(e.Graphics, rect, pill.Text, pill.Color, Theme.F(8.5f, FontStyle.Bold), under);
            }

            e.Handled = true;
            return;
        }

        base.OnCellPainting(e);
    }

    // ---- Colonnes ------------------------------------------------------------------------
    public DataGridViewTextBoxColumn AddText(string name, string header, string property, float weight = 100,
        bool right = false, int minWidth = 60, string? format = null)
    {
        DataGridViewTextBoxColumn col = new()
        {
            Name = name,
            HeaderText = header,
            DataPropertyName = property,
            FillWeight = weight,
            MinimumWidth = Theme.S(minWidth),
            SortMode = DataGridViewColumnSortMode.NotSortable,
            ReadOnly = true
        };
        if (right)
        {
            col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            col.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        }

        if (format is not null)
        {
            col.DefaultCellStyle.Format = format;
        }

        Columns.Add(col);
        return col;
    }

    /// <summary>Colonne de pastilles : la ligne liée doit implémenter <see cref="IPillRow"/>.</summary>
    public DataGridViewTextBoxColumn AddPill(string name, string header, float weight = 100, int minWidth = 100)
    {
        DataGridViewTextBoxColumn col = AddText(name, header, string.Empty, weight, false, minWidth);
        col.DataPropertyName = string.Empty;
        return col;
    }
}

/// <summary>Carte arrondie contenant une grille et un message « aucun résultat » centré.</summary>
public sealed class GridCard : RoundedPanel
{
    private readonly Label _empty;

    public GridCard(string emptyText = "Aucun élément à afficher.")
    {
        Padding = new Padding(Theme.S(1));
        Dock = DockStyle.Fill;
        Grid = new ThemedGrid { Dock = DockStyle.Fill };
        _empty = new Label
        {
            Text = emptyText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Theme.TextMuted,
            Font = Theme.F(10.5f),
            BackColor = Theme.Surface,
            Visible = false
        };
        Controls.Add(Grid);
        Controls.Add(_empty);
        _empty.BringToFront();
        Grid.DataBindingComplete += (_, _) => UpdateEmpty();
        Grid.RowsAdded += (_, _) => UpdateEmpty();
        Grid.RowsRemoved += (_, _) => UpdateEmpty();
    }

    public ThemedGrid Grid { get; }

    public string EmptyText
    {
        get => _empty.Text;
        set => _empty.Text = value;
    }

    public void UpdateEmpty()
    {
        bool empty = Grid.Rows.Count == 0;
        _empty.Visible = empty;
        if (empty)
        {
            _empty.BringToFront();
        }
    }
}
