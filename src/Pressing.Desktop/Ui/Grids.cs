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

/// <summary>
/// DataGridView sombre : en-tête 40 px, lignes 44 px, colonnes réparties sur la largeur (Fill + FillWeight +
/// MinimumWidth, colonnes numériques à largeur fixe), JAMAIS de barre horizontale ni de barre native :
/// défilement à la molette et fine barre thémée dessinée par-dessus la grille quand les lignes débordent.
/// </summary>
public sealed class ThemedGrid : DataGridView
{
    public ThemedGrid()
    {
        DoubleBuffered = true;
        AutoGenerateColumns = false;
        BackgroundColor = Theme.Surface;
        BorderStyle = BorderStyle.None;
        // Bordures dessinées à la main dans OnCellPainting : en mode sombre WinForms, le rendu natif ajoute des
        // traits verticaux entre les cellules même en « SingleHorizontal ».
        CellBorderStyle = DataGridViewCellBorderStyle.None;
        GridColor = Theme.Mix(Theme.Surface, Theme.Border, 0.55f);
        EnableHeadersVisualStyles = false;
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        ColumnHeadersHeight = Theme.S(40);
        ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Theme.SurfaceAlt,
            ForeColor = Theme.TextMuted,
            SelectionBackColor = Theme.SurfaceAlt,
            SelectionForeColor = Theme.TextMuted,
            Font = Theme.FieldLabel,
            Padding = new Padding(Theme.S(12), 0, Theme.S(12), 0),
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
            Padding = new Padding(Theme.S(12), 0, Theme.S(12), 0),
            WrapMode = DataGridViewTriState.False
        };
        AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Theme.Mix(Theme.Surface, Theme.SurfaceAlt, 0.55f),
            SelectionBackColor = Theme.SelectionBack,
            SelectionForeColor = Color.White
        };
        RowHeadersVisible = false;
        RowTemplate.Height = Theme.S(44);
        AllowUserToAddRows = false;
        AllowUserToDeleteRows = false;
        AllowUserToResizeRows = false;
        AllowUserToResizeColumns = false;
        AllowUserToOrderColumns = false;
        MultiSelect = false;
        SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        ReadOnly = true;
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        ScrollBars = ScrollBars.None;
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

    // ---- Colonnes facultatives (écrans étroits) -----------------------------------------------
    private readonly List<string> _optional = new();

    /// <summary>
    /// Colonnes masquées automatiquement, dans cet ordre, quand la largeur ne suffit pas pour afficher toutes les
    /// colonnes sans troncature (jamais de barre horizontale) ; elles réapparaissent quand la place revient.
    /// </summary>
    public void SetOptional(params string[] names)
    {
        _optional.Clear();
        _optional.AddRange(names);
        FitColumns();
    }

    private int RequiredWidth() => Columns.Cast<DataGridViewColumn>().Where(c => c.Visible)
        .Sum(c => c.AutoSizeMode == DataGridViewAutoSizeColumnMode.None ? c.Width : c.MinimumWidth);

    private void FitColumns()
    {
        if (_optional.Count == 0 || Width <= 0)
        {
            return;
        }

        int available = ClientSize.Width - 2;
        foreach (string name in _optional)
        {
            if (Columns[name] is { } c)
            {
                c.Visible = true;
            }
        }

        foreach (string name in _optional)
        {
            if (RequiredWidth() <= available)
            {
                break;
            }

            if (Columns[name] is { } c)
            {
                c.Visible = false;
            }
        }

        Invalidate();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        FitColumns();
        if (HorizontalScrollingOffset != 0)
        {
            HorizontalScrollingOffset = 0;
        }

        Invalidate();
    }

    // ---- Défilement sans barre native -----------------------------------------------------
    private bool _dragThumb;
    private int _dragY;
    private int _dragFirst;

    private int VisibleRows => Math.Max(1, (Height - ColumnHeadersHeight) / Math.Max(1, RowTemplate.Height));

    private bool CanScroll => Rows.Count > VisibleRows;

    private Rectangle ThumbRect
    {
        get
        {
            if (!CanScroll)
            {
                return Rectangle.Empty;
            }

            int top = ColumnHeadersHeight + Theme.S(4);
            int track = Math.Max(1, Height - top - Theme.S(4));
            int thumb = Math.Max(Theme.S(32), track * VisibleRows / Rows.Count);
            int max = Math.Max(1, Rows.Count - VisibleRows);
            int first = Math.Max(0, FirstDisplayedScrollingRowIndex);
            int y = top + (track - thumb) * Math.Min(first, max) / max;
            return new Rectangle(Width - Theme.S(9), y, Theme.S(5), thumb);
        }
    }

    private void ScrollRowsTo(int first)
    {
        if (!CanScroll)
        {
            return;
        }

        first = Math.Clamp(first, 0, Rows.Count - VisibleRows);
        if (first != FirstDisplayedScrollingRowIndex)
        {
            try
            {
                FirstDisplayedScrollingRowIndex = first;
            }
            catch (InvalidOperationException)
            {
                // grille pas encore affichée
            }

            Invalidate();
        }
    }

    /// <summary>
    /// La molette fait défiler les lignes ; quand la grille n'a rien à faire défiler (ou est en butée), le message
    /// n'est pas traité et Windows le transmet à la page (zone défilante parente).
    /// </summary>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (CanScroll)
        {
            int before = FirstDisplayedScrollingRowIndex;
            int step = Math.Max(1, SystemInformation.MouseWheelScrollLines);
            ScrollRowsTo(before - Math.Sign(e.Delta) * step);
            if (FirstDisplayedScrollingRowIndex != before && e is HandledMouseEventArgs handled)
            {
                handled.Handled = true;
            }
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Rectangle thumb = ThumbRect;
        if (!thumb.IsEmpty && e.Button == MouseButtons.Left && e.X >= Width - Theme.S(14) && e.Y > ColumnHeadersHeight)
        {
            if (e.Y >= thumb.Top && e.Y <= thumb.Bottom)
            {
                _dragThumb = true;
                _dragY = e.Y;
                _dragFirst = FirstDisplayedScrollingRowIndex;
            }
            else
            {
                ScrollRowsTo(FirstDisplayedScrollingRowIndex + (e.Y < thumb.Top ? -VisibleRows : VisibleRows));
            }

            return;
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragThumb)
        {
            Rectangle thumb = ThumbRect;
            int track = Height - ColumnHeadersHeight - Theme.S(8) - thumb.Height;
            if (track > 0)
            {
                ScrollRowsTo(_dragFirst + (e.Y - _dragY) * (Rows.Count - VisibleRows) / track);
            }

            return;
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_dragThumb)
        {
            _dragThumb = false;
            Invalidate();
            return;
        }

        base.OnMouseUp(e);
    }

    protected override void OnScroll(ScrollEventArgs e)
    {
        base.OnScroll(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Rectangle thumb = ThumbRect;
        if (!thumb.IsEmpty)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using GraphicsPath path = Theme.RoundedRect(thumb, thumb.Width / 2);
            using SolidBrush brush = new(_dragThumb ? Theme.ScrollThumbHot : Theme.Mix(Theme.Surface, Theme.ScrollThumbHot, 0.85f));
            e.Graphics.FillPath(brush, path);
        }
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

    /// <summary>
    /// Fond de la ligne entière peint avant les cellules : aucun interstice entre deux cellules ne laisse
    /// apparaître un trait vertical plus sombre.
    /// </summary>
    protected override void OnRowPrePaint(DataGridViewRowPrePaintEventArgs e)
    {
        base.OnRowPrePaint(e);
        if (e.RowIndex < 0 || e.RowIndex >= Rows.Count)
        {
            return;
        }

        DataGridViewRow row = Rows[e.RowIndex];
        bool selected = e.State.HasFlag(DataGridViewElementStates.Selected);
        DataGridViewCellStyle style = row.InheritedStyle;
        using SolidBrush fill = new(selected ? style.SelectionBackColor : style.BackColor);
        Rectangle bounds = new(e.RowBounds.X, e.RowBounds.Y, Math.Max(e.RowBounds.Width, ClientSize.Width), e.RowBounds.Height);
        e.Graphics.FillRectangle(fill, bounds);
        e.PaintParts &= ~(DataGridViewPaintParts.Background | DataGridViewPaintParts.SelectionBackground);
    }

    protected override void OnCellPainting(DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Graphics is null)
        {
            base.OnCellPainting(e);
            return;
        }

        bool selected = e.State.HasFlag(DataGridViewElementStates.Selected);
        Color back = selected
            ? e.CellStyle?.SelectionBackColor ?? Theme.SelectionBack
            : e.CellStyle?.BackColor ?? Theme.Surface;
        using (SolidBrush fill = new(back))
        {
            // +1 px : recouvre la jointure avec la cellule suivante (qui repeint son propre fond par-dessus).
            e.Graphics.FillRectangle(fill, e.CellBounds.X, e.CellBounds.Y, e.CellBounds.Width + 1, e.CellBounds.Height);
        }

        if (Rows[e.RowIndex].DataBoundItem is IPillRow pills &&
            pills.GetPill(Columns[e.ColumnIndex].Name) is { } pill)
        {
            {
                Size text = TextRenderer.MeasureText(pill.Text, Theme.F(8.5f, FontStyle.Bold), new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
                int w = Math.Min(e.CellBounds.Width - Theme.S(24), Math.Max(Theme.S(92), text.Width + Theme.S(24)));
                int h = Theme.S(26);
                Rectangle rect = new(e.CellBounds.X + Theme.S(12), e.CellBounds.Y + (e.CellBounds.Height - h) / 2, Math.Max(w, Theme.S(20)), h);
                PillPainter.Draw(e.Graphics, rect, pill.Text, pill.Color, Theme.F(8.5f, FontStyle.Bold), back);
            }
        }
        else
        {
            e.Paint(e.CellBounds, DataGridViewPaintParts.ContentForeground);
        }

        using (Pen line = new(GridColor))
        {
            int y = e.CellBounds.Bottom - 1;
            e.Graphics.DrawLine(line, e.CellBounds.Left, y, e.CellBounds.Right, y);
        }

        e.Handled = true;
    }

    // ---- Colonnes ------------------------------------------------------------------------
    /// <summary>
    /// Colonne de texte : largeur proportionnelle (<paramref name="weight"/>) bornée par <paramref name="minWidth"/>,
    /// ou largeur fixe si <paramref name="fixedWidth"/> &gt; 0 (montants, quantités, dates).
    /// </summary>
    public DataGridViewTextBoxColumn AddText(string name, string header, string property, float weight = 100,
        bool right = false, int minWidth = 60, string? format = null, int fixedWidth = 0)
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

        if (fixedWidth > 0)
        {
            col.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            col.Width = Theme.S(fixedWidth);
            col.MinimumWidth = Math.Min(col.MinimumWidth, col.Width);
            col.Resizable = DataGridViewTriState.False;
        }

        Columns.Add(col);
        return col;
    }

    /// <summary>Colonne de pastilles : la ligne liée doit implémenter <see cref="IPillRow"/>.</summary>
    public DataGridViewTextBoxColumn AddPill(string name, string header, float weight = 100, int minWidth = 100, int fixedWidth = 0)
    {
        DataGridViewTextBoxColumn col = AddText(name, header, string.Empty, weight, false, minWidth, null, fixedWidth);
        col.DataPropertyName = string.Empty;
        return col;
    }
}

/// <summary>
/// Carte arrondie contenant une grille et un état vide soigné (icône + phrase centrées).
/// <see cref="AutoHeightRows"/> : la hauteur préférée suit le nombre de lignes (grilles dans une page défilante).
/// </summary>
public sealed class GridCard : RoundedPanel
{
    private readonly EmptyState _empty;

    public GridCard(string emptyText = "Aucun élément à afficher.")
    {
        Padding = new Padding(1);
        Dock = DockStyle.Fill;
        Margin = Padding.Empty;
        Grid = new ThemedGrid { Dock = DockStyle.Fill };
        _empty = new EmptyState(emptyText) { Visible = true };
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

    /// <summary>Si &gt; 0 : hauteur préférée = en-tête + nombre de lignes (au moins cette valeur, au plus <see cref="MaxRows"/>).</summary>
    public int AutoHeightRows { get; set; }

    public int MaxRows { get; set; } = 12;

    public override Size GetPreferredSize(Size proposedSize)
    {
        int rows = AutoHeightRows > 0
            ? Math.Clamp(Grid.Rows.Count, AutoHeightRows, Math.Max(AutoHeightRows, MaxRows))
            : Math.Max(3, Math.Min(Grid.Rows.Count, MaxRows));
        return new Size(proposedSize.Width, 2 + Grid.ColumnHeadersHeight + rows * Grid.RowTemplate.Height);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_empty is null || Grid is null)
        {
            return;
        }

        int top = 1 + Grid.ColumnHeadersHeight;
        _empty.SetBounds(1, top, Math.Max(0, Width - 2), Math.Max(0, Height - top - 1));
    }

    public void UpdateEmpty()
    {
        bool empty = Grid.Rows.Count == 0;
        _empty.Visible = empty;
        if (empty)
        {
            _empty.BringToFront();
        }

        if (AutoHeightRows > 0)
        {
            this.RequestPageLayout();
        }
    }
}

/// <summary>État vide : icône discrète et phrase centrées (grilles, listes).</summary>
public sealed class EmptyState : Control
{
    public EmptyState(string text)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Text = text;
        BackColor = Theme.Surface;
    }

    public string Glyph { get; set; } = Icons.Inbox;

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);
        TextFormatFlags center = TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        int textWidth = Math.Max(10, Width - Theme.S(48));
        Size text = TextRenderer.MeasureText(Text, Theme.F(10f), new Size(textWidth, int.MaxValue), center | TextFormatFlags.WordBreak);
        int icon = Height >= Theme.S(110) ? Theme.S(40) : 0;
        int total = icon + (icon > 0 ? Theme.S(12) : 0) + text.Height;
        int y = Math.Max(Theme.S(4), (Height - total) / 2);
        if (icon > 0)
        {
            Rectangle circle = new((Width - icon) / 2, y, icon, icon);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using SolidBrush brush = new(Theme.SurfaceAlt);
            g.FillEllipse(brush, circle);
            TextRenderer.DrawText(g, Glyph, Theme.Icon(13f), circle, Theme.TextFaint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            y += icon + Theme.S(12);
        }

        TextRenderer.DrawText(g, Text, Theme.F(10f), new Rectangle(Theme.S(24), y, textWidth, text.Height), Theme.TextMuted,
            center | TextFormatFlags.WordBreak);
    }
}
