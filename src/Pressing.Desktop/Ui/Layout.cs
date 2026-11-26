using System.Drawing.Drawing2D;
using System.Reflection;

namespace Pressing.Desktop.Ui;

// =====================================================================================================
//  Primitives de mise en page (DESIGN.md) : elles rendent la charte automatique.
//
//  PageLayout   : gouttière de 32 px, en-tête de page, barre de filtres, corps (rempli ou défilant).
//  ScrollViewport : UN défilement par écran, sans barre native (fine barre thémée dans la gouttière).
//  VStack       : pile verticale avec espacement de 24 px (lignes Auto / Fixed / Fill).
//  GridRow      : ligne de la grille de 12 colonnes (spans, gouttière de 24 px, cartes de même hauteur).
//  ActionBar    : boutons sur une seule ligne, débordement dans « Plus d'actions ».
// =====================================================================================================

/// <summary>Utilitaires communs aux conteneurs de mise en page.</summary>
public static class LayoutUtil
{
    private static readonly Func<Control, bool>? VisibleState = CreateVisibleState();

    private static Func<Control, bool>? CreateVisibleState()
    {
        try
        {
            MethodInfo? method = typeof(Control).GetMethod("GetState", BindingFlags.NonPublic | BindingFlags.Instance);
            if (method is null || method.GetParameters().Length != 1)
            {
                return null;
            }

            Type flagType = method.GetParameters()[0].ParameterType;
            object visible = Enum.ToObject(flagType, 0x00000002);
            return c => (bool)method.Invoke(c, new[] { visible })!;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Visibilité « voulue » du contrôle (sa propriété Visible propre), indépendamment de celle de ses parents :
    /// permet de calculer une mise en page avant l'affichage de la fenêtre.
    /// </summary>
    public static bool IsShown(Control control)
    {
        if (VisibleState is not null)
        {
            try
            {
                return VisibleState(control);
            }
            catch
            {
                // repli ci-dessous
            }
        }

        return control.Visible || control.Parent is { Visible: false };
    }

    /// <summary>Hauteur préférée d'un contrôle pour une largeur donnée.</summary>
    public static int PreferredHeight(Control control, int width)
    {
        if (control is TextBoxBase or InputBox or SelectBox or DateBox or StepperBox or FlatButton { AutoSize: false })
        {
            return control.Height;
        }

        Size size = control.GetPreferredSize(new Size(Math.Max(1, width), 0));
        int height = size.Height;
        if (control is Label { AutoSize: false } and not TextLabel)
        {
            height = control.Height;
        }

        return Math.Max(height, control.MinimumSize.Height);
    }

    /// <summary>
    /// Demande le recalcul de la mise en page de la page qui contient ce contrôle (après un changement de contenu :
    /// lignes de grille chargées, bloc affiché ou masqué…). Les appels rapprochés sont regroupés.
    /// </summary>
    public static void RequestPageLayout(this Control control)
    {
        Control? root = null;
        for (Control? c = control.Parent; c is not null; c = c.Parent)
        {
            root = c;
            if (c is ScrollViewport or PageLayout)
            {
                break;
            }
        }

        if (root is null || !root.IsHandleCreated || root.IsDisposed)
        {
            control.Parent?.PerformLayout();
            return;
        }

        if (root.Tag is PendingLayout)
        {
            return;
        }

        object? previous = root.Tag;
        root.Tag = new PendingLayout(previous);
        root.BeginInvoke(() =>
        {
            if (root.Tag is PendingLayout pending)
            {
                root.Tag = pending.Previous;
            }

            if (root.IsDisposed || control.IsDisposed)
            {
                return;
            }

            List<Control> chain = new();
            for (Control? c = control.Parent; c is not null && !ReferenceEquals(c, root); c = c.Parent)
            {
                chain.Add(c);
            }

            root.PerformLayout();
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                chain[i].PerformLayout();
            }
        });
    }

    private sealed record PendingLayout(object? Previous);

    internal static void Prepare(Control child)
    {
        child.Dock = DockStyle.None;
        child.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        child.Margin = Padding.Empty;
    }
}

/// <summary>Conteneur à mise en page manuelle (double tampon, fond hérité).</summary>
public abstract class LayoutControl : Control
{
    protected LayoutControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ContainerControl | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Margin = Padding.Empty;
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Theme.BackOf(this));

    protected override void OnPaint(PaintEventArgs e)
    {
    }

    /// <summary>Un enfant affiché/masqué (ou un libellé qui change) peut changer la hauteur de la page : on la recalcule.</summary>
    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (e.Control is { } child)
        {
            child.VisibleChanged += OnChildChanged;
            if (child is TextLabel)
            {
                child.TextChanged += OnChildChanged;
            }
        }
    }

    protected override void OnControlRemoved(ControlEventArgs e)
    {
        base.OnControlRemoved(e);
        if (e.Control is { } child)
        {
            child.VisibleChanged -= OnChildChanged;
            child.TextChanged -= OnChildChanged;
        }
    }

    private void OnChildChanged(object? sender, EventArgs e)
    {
        if (IsHandleCreated && !IsDisposed && this is not ActionBar)
        {
            PerformLayout();
            this.RequestPageLayout();
        }
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (Width > 0 && Height > 0)
        {
            Arrange();
        }
    }

    /// <summary>Place les enfants dans <see cref="Control.ClientRectangle"/>.</summary>
    protected abstract void Arrange();
}

// -----------------------------------------------------------------------------------------------------
/// <summary>
/// Zone défilante sans barre native : le contenu (un seul contrôle) prend la largeur moins la gouttière
/// (<see cref="Control.Padding"/> gauche/droite) et sa hauteur préférée ; une barre fine de 6 px est dessinée dans
/// la gouttière de droite (elle ne réduit jamais la largeur utile). Molette, glisser la barre, focus visible.
/// </summary>
public sealed class ScrollViewport : Control
{
    private Control? _content;
    private int _offset;
    private bool _hot;
    private bool _dragging;
    private int _dragY;
    private int _dragOffset;
    private bool _inLayout;
    private bool _refitPending;

    public ScrollViewport()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.ContainerControl, true);
        BackColor = Theme.Background;
        Margin = Padding.Empty;
    }

    /// <summary>Quand vrai, le contenu occupe au moins la hauteur visible (ses lignes « Fill » s'étirent).</summary>
    public bool FillViewport { get; set; } = true;

    public int Offset => _offset;

    public int ContentHeight { get; private set; }

    public event EventHandler? Scrolled;

    public Control? Content
    {
        get => _content;
        set
        {
            if (_content is not null)
            {
                _content.Layout -= OnContentLayout;
                Controls.Remove(_content);
            }

            _content = value;
            if (value is not null)
            {
                LayoutUtil.Prepare(value);
                Controls.Add(value);
                value.Layout += OnContentLayout;
                HookFocus(value);
            }

            _offset = 0;
            PerformLayout();
        }
    }

    private int Extent => Padding.Top + ContentHeight + Padding.Bottom;

    public int MaxOffset => Math.Max(0, Extent - Height);

    private void OnContentLayout(object? sender, LayoutEventArgs e)
    {
        if (_inLayout || _refitPending || !IsHandleCreated || IsDisposed)
        {
            return;
        }

        _refitPending = true;
        BeginInvoke(() =>
        {
            _refitPending = false;
            if (!IsDisposed)
            {
                PerformLayout();
            }
        });
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_content is null || _inLayout)
        {
            return;
        }

        _inLayout = true;
        try
        {
            int width = Math.Max(1, Width - Padding.Horizontal);
            int preferred = LayoutUtil.PreferredHeight(_content, width);
            int minimum = FillViewport ? Math.Max(0, Height - Padding.Vertical) : 0;
            ContentHeight = Math.Max(preferred, minimum);
            _offset = Math.Clamp(_offset, 0, MaxOffset);
            _content.SetBounds(Padding.Left, Padding.Top - _offset, width, ContentHeight);
        }
        finally
        {
            _inLayout = false;
        }

        Invalidate();
    }

    public void ScrollTo(int offset)
    {
        int value = Math.Clamp(offset, 0, MaxOffset);
        if (value == _offset || _content is null)
        {
            return;
        }

        _offset = value;
        _content.Location = new Point(Padding.Left, Padding.Top - _offset);
        Invalidate();
        Update();
        Scrolled?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Fait défiler pour rendre visible un contrôle descendant (focus clavier).</summary>
    public void EnsureVisible(Control control)
    {
        if (_content is null || MaxOffset == 0 || control.Parent is null)
        {
            return;
        }

        Rectangle r = RectangleToClient(control.Parent.RectangleToScreen(control.Bounds));
        int margin = Theme.S(16);
        if (r.Height >= Height - 2 * margin || r.Top < 0)
        {
            ScrollTo(_offset + r.Top - margin);
        }
        else if (r.Bottom > Height)
        {
            ScrollTo(_offset + r.Bottom - Height + margin);
        }
    }

    private void HookFocus(Control control)
    {
        control.GotFocus += OnDescendantFocus;
        control.ControlAdded += OnDescendantAdded;
        foreach (Control child in control.Controls)
        {
            HookFocus(child);
        }
    }

    private void OnDescendantAdded(object? sender, ControlEventArgs e)
    {
        if (e.Control is not null)
        {
            HookFocus(e.Control);
        }
    }

    private void OnDescendantFocus(object? sender, EventArgs e)
    {
        if (sender is Control c && c is not DataGridView && c.Height < Height)
        {
            EnsureVisible(c);
        }
    }

    private Rectangle ThumbRect
    {
        get
        {
            if (MaxOffset <= 0 || Height <= 0)
            {
                return Rectangle.Empty;
            }

            int inset = Theme.S(6);
            int track = Height - 2 * inset;
            int thumb = Math.Max(Theme.S(40), (int)((long)track * Height / Math.Max(1, Extent)));
            int y = inset + (int)((long)(track - thumb) * _offset / MaxOffset);
            return new Rectangle(Width - Theme.S(12), y, Theme.S(6), thumb);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);
        Rectangle thumb = ThumbRect;
        if (thumb.IsEmpty)
        {
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        using GraphicsPath path = Theme.RoundedRect(thumb, thumb.Width / 2);
        using SolidBrush brush = new(_hot || _dragging ? Theme.ScrollThumbHot : Theme.ScrollThumb);
        g.FillPath(brush, path);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (MaxOffset > 0)
        {
            ScrollTo(_offset - e.Delta * Theme.S(96) / 120);
            if (e is HandledMouseEventArgs handled)
            {
                handled.Handled = true;
            }

            return;
        }

        base.OnMouseWheel(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Rectangle thumb = ThumbRect;
        if (thumb.IsEmpty || e.Button != MouseButtons.Left || e.X < Width - Theme.S(24))
        {
            return;
        }

        if (thumb.Contains(new Point(thumb.X + 1, e.Y)))
        {
            _dragging = true;
            _dragY = e.Y;
            _dragOffset = _offset;
        }
        else
        {
            ScrollTo(_offset + (e.Y < thumb.Top ? -Height : Height) * 9 / 10);
        }

        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            Rectangle thumb = ThumbRect;
            int track = Height - 2 * Theme.S(6) - thumb.Height;
            if (track > 0)
            {
                ScrollTo(_dragOffset + (int)((long)(e.Y - _dragY) * MaxOffset / track));
            }

            return;
        }

        bool hot = e.X >= Width - Theme.S(24) && !ThumbRect.IsEmpty;
        if (hot != _hot)
        {
            _hot = hot;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hot = false;
        Invalidate();
    }
}

// -----------------------------------------------------------------------------------------------------
/// <summary>
/// Gabarit de page : gouttière de 32 px à gauche et à droite (mêmes bords que l'en-tête global), 24 px en haut,
/// en-tête de page, barre de filtres facultative (16 px sous l'en-tête), puis le corps :
/// <list type="bullet">
/// <item><c>scroll: false</c> — le corps remplit la hauteur restante (listes : la grille défile seule) ;</item>
/// <item><c>scroll: true</c> — le corps défile dans un <see cref="ScrollViewport"/> (barre fine dans la gouttière).</item>
/// </list>
/// </summary>
public sealed class PageLayout : LayoutControl
{
    private readonly Control? _header;
    private readonly Control? _toolbar;
    private readonly Control _body;

    public PageLayout(Control? header, Control body, bool scroll = false, Control? toolbar = null)
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        _header = header;
        _toolbar = toolbar;
        foreach (Control? c in new[] { header, toolbar })
        {
            if (c is not null)
            {
                LayoutUtil.Prepare(c);
                Controls.Add(c);
            }
        }

        if (scroll)
        {
            Viewport = new ScrollViewport { Content = body };
            _body = Viewport;
        }
        else
        {
            _body = body;
        }

        LayoutUtil.Prepare(_body);
        Controls.Add(_body);
    }

    /// <summary>La zone défilante (null si le corps remplit la page).</summary>
    public ScrollViewport? Viewport { get; }

    public Control Body => Viewport?.Content ?? _body;

    protected override void Arrange()
    {
        int gutter = Theme.PageGutter;
        int width = Math.Max(1, Width - 2 * gutter);
        int y = Theme.S(24);
        if (_header is not null && LayoutUtil.IsShown(_header))
        {
            int h = LayoutUtil.PreferredHeight(_header, width);
            _header.SetBounds(gutter, y, width, h);
            y += h + Theme.Gap;
        }

        if (_toolbar is not null && LayoutUtil.IsShown(_toolbar))
        {
            int h = LayoutUtil.PreferredHeight(_toolbar, width);
            _toolbar.SetBounds(gutter, y, width, h);
            y += h + Theme.S(16);
        }

        if (Viewport is not null)
        {
            Viewport.Padding = new Padding(gutter, 0, gutter, Theme.S(24));
            Viewport.SetBounds(0, y, Width, Math.Max(0, Height - y));
        }
        else
        {
            _body.SetBounds(gutter, y, width, Math.Max(0, Height - y - Theme.S(24)));
        }
    }
}

public enum StackSize
{
    /// <summary>Hauteur préférée du contrôle.</summary>
    Auto,
    /// <summary>Hauteur fixe.</summary>
    Fixed,
    /// <summary>Partage la hauteur restante (au moins la hauteur préférée dans une zone défilante).</summary>
    Fill
}

// -----------------------------------------------------------------------------------------------------
/// <summary>Pile verticale : chaque ligne prend toute la largeur ; espacement uniforme (24 px par défaut).</summary>
public sealed class VStack : LayoutControl
{
    private readonly List<(Control Control, StackSize Mode, int Height)> _items = new();

    public VStack(int gap = 24)
    {
        Gap = Theme.S(gap);
    }

    public int Gap { get; set; }

    public VStack Add(Control control, StackSize mode = StackSize.Auto, int height = 0)
    {
        LayoutUtil.Prepare(control);
        _items.Add((control, mode, Theme.S(height)));
        Controls.Add(control);
        return this;
    }

    private IEnumerable<(Control Control, StackSize Mode, int Height)> Shown =>
        _items.Where(i => LayoutUtil.IsShown(i.Control)
                          && !(i.Control is Padded p && !LayoutUtil.IsShown(p.Child))
                          && !(i.Control is TextLabel { Text.Length: 0 }));

    private int ItemHeight((Control Control, StackSize Mode, int Height) item, int width) =>
        item.Mode == StackSize.Fixed ? item.Height : LayoutUtil.PreferredHeight(item.Control, width);

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        var shown = Shown.ToList();
        int height = shown.Sum(i => ItemHeight(i, width)) + Math.Max(0, shown.Count - 1) * Gap;
        return new Size(width, height);
    }

    protected override void Arrange()
    {
        var shown = Shown.ToList();
        foreach (var hidden in _items.Where(i => !shown.Contains(i)))
        {
            hidden.Control.SetBounds(0, 0, 0, 0);
        }

        int width = Width;
        int fixedHeight = shown.Where(i => i.Mode != StackSize.Fill).Sum(i => ItemHeight(i, width));
        int fills = shown.Count(i => i.Mode == StackSize.Fill);
        int remaining = Height - fixedHeight - Math.Max(0, shown.Count - 1) * Gap;
        int y = 0;
        int fillIndex = 0;
        foreach (var item in shown)
        {
            int h;
            if (item.Mode == StackSize.Fill)
            {
                int share = fills == 0 ? 0 : remaining / fills + (fillIndex == fills - 1 ? remaining % Math.Max(1, fills) : 0);
                h = Math.Max(item.Control.MinimumSize.Height, share);
                fillIndex++;
            }
            else
            {
                h = ItemHeight(item, width);
            }

            item.Control.SetBounds(0, y, width, Math.Max(0, h));
            y += h + Gap;
        }
    }
}

// -----------------------------------------------------------------------------------------------------
/// <summary>
/// Ligne de la grille de 12 colonnes : chaque cellule a un span (12 · 6+6 · 4+4+4 · 3+3+3+3 · 8+4 · 4+8),
/// gouttière de 24 px, toutes les cellules ont la même hauteur (la plus grande hauteur préférée, ou la hauteur
/// imposée par le parent). Une cellule « collante » garde sa hauteur et suit le défilement de la page.
/// </summary>
public sealed class GridRow : LayoutControl
{
    private readonly List<(Control Control, int Span)> _cells = new();
    private Control? _sticky;
    private ScrollViewport? _viewport;

    public GridRow(int height = 0)
    {
        FixedHeight = Theme.S(height);
    }

    /// <summary>Hauteur imposée (0 = hauteur de la cellule la plus haute).</summary>
    public int FixedHeight { get; set; }

    /// <summary>Hauteur minimale de la ligne (px logiques déjà convertis).</summary>
    public int MinHeight { get; set; }

    /// <summary>En dessous de cette largeur, les cellules s'empilent (0 = jamais).</summary>
    public int StackBelow { get; set; }

    public GridRow Add(Control control, int span)
    {
        LayoutUtil.Prepare(control);
        _cells.Add((control, Math.Clamp(span, 1, 12)));
        Controls.Add(control);
        return this;
    }

    /// <summary>La cellule garde sa hauteur préférée et reste visible pendant le défilement (récapitulatif).</summary>
    public GridRow Sticky(Control control)
    {
        _sticky = control;
        return this;
    }

    private bool Stacked => StackBelow > 0 && Width > 0 && Width < Theme.S(StackBelow);

    public static int SpanWidth(int width, int span)
    {
        float column = (width - 11f * Theme.Gap) / 12f;
        return (int)Math.Round(span * column + (span - 1) * Theme.Gap);
    }

    public static int SpanLeft(int width, int startColumn)
    {
        float column = (width - 11f * Theme.Gap) / 12f;
        return (int)Math.Round(startColumn * (column + Theme.Gap));
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        var shown = _cells.Where(c => LayoutUtil.IsShown(c.Control)).ToList();
        if (StackBelow > 0 && width < Theme.S(StackBelow))
        {
            return new Size(width, shown.Sum(c => LayoutUtil.PreferredHeight(c.Control, width)) + Math.Max(0, shown.Count - 1) * Theme.Gap);
        }

        if (FixedHeight > 0)
        {
            return new Size(width, FixedHeight);
        }

        int height = shown.Count == 0 ? 0 : shown.Max(c => LayoutUtil.PreferredHeight(c.Control, SpanWidth(width, c.Span)));
        return new Size(width, Math.Max(height, MinHeight));
    }

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        AttachViewport();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        AttachViewport();
    }

    private void AttachViewport()
    {
        if (_sticky is null)
        {
            return;
        }

        ScrollViewport? found = null;
        for (Control? c = Parent; c is not null; c = c.Parent)
        {
            if (c is ScrollViewport v)
            {
                found = v;
                break;
            }
        }

        if (!ReferenceEquals(found, _viewport))
        {
            if (_viewport is not null)
            {
                _viewport.Scrolled -= OnViewportScrolled;
            }

            _viewport = found;
            if (found is not null)
            {
                found.Scrolled += OnViewportScrolled;
            }
        }
    }

    private void OnViewportScrolled(object? sender, EventArgs e) => PlaceSticky();

    private void PlaceSticky()
    {
        if (_sticky is null || _viewport is null || Stacked || !LayoutUtil.IsShown(_sticky))
        {
            return;
        }

        int height = Math.Min(Height, LayoutUtil.PreferredHeight(_sticky, _sticky.Width));
        int top = _viewport.RectangleToClient(RectangleToScreen(ClientRectangle)).Top;
        int y = Math.Clamp(-top + Theme.S(0), 0, Math.Max(0, Height - height));
        if (_sticky.Top != y || _sticky.Height != height)
        {
            _sticky.SetBounds(_sticky.Left, y, _sticky.Width, height);
        }
    }

    protected override void Arrange()
    {
        var shown = _cells.Where(c => LayoutUtil.IsShown(c.Control)).ToList();
        if (Stacked)
        {
            int y = 0;
            foreach ((Control control, _) in shown)
            {
                int h = LayoutUtil.PreferredHeight(control, Width);
                control.SetBounds(0, y, Width, h);
                y += h + Theme.Gap;
            }

            return;
        }

        int column = 0;
        foreach ((Control control, int span) in shown)
        {
            int left = SpanLeft(Width, column);
            int right = column + span >= 12 ? Width : SpanLeft(Width, column + span) - Theme.Gap;
            if (ReferenceEquals(control, _sticky))
            {
                int h = Math.Min(Height, LayoutUtil.PreferredHeight(control, right - left));
                control.SetBounds(left, control.Top, right - left, h);
            }
            else
            {
                control.SetBounds(left, 0, right - left, Height);
            }

            column += span;
        }

        PlaceSticky();
    }
}

// -----------------------------------------------------------------------------------------------------
/// <summary>
/// Barre d'actions sur UNE ligne (jamais de retour à la ligne). Au-delà de <see cref="MaxVisible"/> actions, ou
/// quand la largeur manque, les actions secondaires passent dans un bouton « Plus d'actions ».
/// </summary>
public sealed class ActionBar : LayoutControl
{
    private readonly List<Control> _items = new();
    private readonly HashSet<Control> _overflow = new();
    private readonly FlatButton _more;
    private bool _arranging;

    public ActionBar(bool alignRight = true)
    {
        AlignRight = alignRight;
        _more = new FlatButton { Text = "Plus d'actions", Kind = ButtonKind.Secondary, IconGlyph = Icons.More, Visible = false };
        _more.Click += (_, _) => ShowOverflow();
        Controls.Add(_more);
    }

    public bool AlignRight { get; set; }

    /// <summary>Nombre maximal d'actions visibles (au-delà : menu « Plus d'actions »).</summary>
    public int MaxVisible { get; set; } = 4;

    public int Spacing { get; set; } = Theme.S(8);

    /// <summary>Hauteur des boutons de la barre (40 px ; 32 px dans une barre d'outils de carte).</summary>
    public int ButtonHeight { get; set; } = Theme.ControlHeight;

    public FlatButton AddAction(string text, ButtonKind kind, string icon = "", EventHandler? onClick = null)
    {
        FlatButton button = new() { Text = text, Kind = kind, IconGlyph = icon };
        if (onClick is not null)
        {
            button.Click += onClick;
        }

        Add(button);
        return button;
    }

    public T Add<T>(T control) where T : Control
    {
        LayoutUtil.Prepare(control);
        if (control is FlatButton { AutoSize: true } b)
        {
            b.Height = ButtonHeight;
        }

        _items.Add(control);
        Controls.Add(control);
        control.VisibleChanged += (_, _) => Relayout();
        control.TextChanged += (_, _) => Relayout();
        PerformLayout();
        return control;
    }

    private void Relayout()
    {
        if (!_arranging)
        {
            PerformLayout();
            Parent?.PerformLayout();
        }
    }

    public void Remove(Control control)
    {
        _items.Remove(control);
        _overflow.Remove(control);
        Controls.Remove(control);
        PerformLayout();
    }

    private static int ItemWidth(Control c) => c.AutoSize ? c.GetPreferredSize(Size.Empty).Width : c.Width;

    public override Size GetPreferredSize(Size proposedSize)
    {
        var shown = _items.Where(LayoutUtil.IsShown).ToList();
        int width = shown.Sum(ItemWidth) + Math.Max(0, shown.Count - 1) * Spacing;
        int height = shown.Count == 0 ? 0 : Math.Max(ButtonHeight, shown.Max(c => c.Height));
        return new Size(width, height);
    }

    protected override void Arrange()
    {
        if (_arranging)
        {
            return;
        }

        _arranging = true;
        try
        {
            // Les éléments en débordement reviennent d'abord dans la barre.
            foreach (Control c in _overflow.ToList())
            {
                Controls.Add(c);
                _overflow.Remove(c);
            }

            Controls.SetChildIndex(_more, Controls.Count - 1);
            List<Control> shown = _items.Where(LayoutUtil.IsShown).ToList();
            int moreWidth = _more.GetPreferredSize(Size.Empty).Width;
            int Total(IEnumerable<Control> list, bool withMore)
            {
                var l = list.ToList();
                int w = l.Sum(ItemWidth) + Math.Max(0, l.Count - 1) * Spacing;
                return withMore ? w + (l.Count > 0 ? Spacing : 0) + moreWidth : w;
            }

            List<Control> visible = new(shown);
            List<Control> hidden = new();
            bool needMore = visible.Count(c => c is FlatButton) > MaxVisible || Total(visible, false) > Width;
            while (needMore && (Total(visible, true) > Width || visible.Count(c => c is FlatButton) > MaxVisible - 1))
            {
                // Retire d'abord la dernière action secondaire (jamais l'action principale).
                // Barre à droite : les actions les plus à gauche (les moins importantes) débordent d'abord.
                Control? victim = AlignRight
                    ? visible.FirstOrDefault(c => c is FlatButton { Kind: not ButtonKind.Primary })
                    : visible.LastOrDefault(c => c is FlatButton { Kind: not ButtonKind.Primary });
                if (victim is null)
                {
                    break;
                }

                visible.Remove(victim);
                if (AlignRight)
                {
                    hidden.Add(victim);
                }
                else
                {
                    hidden.Insert(0, victim);
                }
            }

            foreach (Control c in hidden)
            {
                Controls.Remove(c);
                _overflow.Add(c);
            }

            _more.Visible = hidden.Count > 0;
            _more.Height = ButtonHeight;
            List<Control> placed = new(visible);
            if (hidden.Count > 0)
            {
                // « Plus d'actions » se place avant l'action principale (à sa gauche) quand la barre est à droite.
                int primary = placed.FindIndex(c => c is FlatButton { Kind: ButtonKind.Primary });
                placed.Insert(primary >= 0 && AlignRight ? primary : placed.Count, _more);
            }

            int total = placed.Sum(c => ReferenceEquals(c, _more) ? moreWidth : ItemWidth(c)) + Math.Max(0, placed.Count - 1) * Spacing;
            int x = AlignRight ? Math.Max(0, Width - total) : 0;
            foreach (Control c in placed)
            {
                int w = ReferenceEquals(c, _more) ? moreWidth : ItemWidth(c);
                int h = c is FlatButton ? ButtonHeight : c.Height;
                c.SetBounds(x, Math.Max(0, (Height - h) / 2), w, h);
                x += w + Spacing;
            }
        }
        finally
        {
            _arranging = false;
        }
    }

    private void ShowOverflow()
    {
        ContextMenuStrip menu = ThemedMenu.Create();
        foreach (Control c in _items.Where(i => _overflow.Contains(i) && LayoutUtil.IsShown(i)))
        {
            if (c is not FlatButton b)
            {
                continue;
            }

            ToolStripMenuItem item = new(b.Text) { Enabled = b.Enabled && !b.Busy };
            if (b.IconGlyph.Length > 0)
            {
                item.Tag = b.IconGlyph;
            }

            if (b.Kind == ButtonKind.Danger)
            {
                item.ForeColor = Theme.Red;
            }

            item.Click += (_, _) => b.RaiseClick();
            menu.Items.Add(item);
        }

        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(_more, new Point(_more.Width - menu.PreferredSize.Width, _more.Height + Theme.S(4)));
    }
}

/// <summary>Menu contextuel au thème sombre (débordement des barres d'actions).</summary>
public static class ThemedMenu
{
    public static ContextMenuStrip Create()
    {
        ContextMenuStrip menu = new()
        {
            Renderer = new Renderer(),
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            Font = Theme.Body,
            ShowImageMargin = true,
            Padding = new Padding(Theme.S(4))
        };
        menu.ItemAdded += (_, e) =>
        {
            if (e.Item is not null)
            {
                e.Item.ForeColor = e.Item.ForeColor == Theme.Red ? Theme.Red : Theme.Text;
                e.Item.Padding = new Padding(Theme.S(4), Theme.S(8), Theme.S(12), Theme.S(8));
            }
        };
        return menu;
    }

    private sealed class Colors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Surface;
        public override Color ImageMarginGradientBegin => Theme.Surface;
        public override Color ImageMarginGradientMiddle => Theme.Surface;
        public override Color ImageMarginGradientEnd => Theme.Surface;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Theme.SurfaceHover;
        public override Color MenuItemSelected => Theme.SurfaceHover;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Border;
    }

    private sealed class Renderer : ToolStripProfessionalRenderer
    {
        public Renderer() : base(new Colors())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled ? Theme.TextFaint : e.Item.ForeColor;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            using SolidBrush brush = new(Theme.Surface);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e) => base.OnRenderItemImage(e);

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            base.OnRenderMenuItemBackground(e);
            if (e.Item.Tag is string glyph && glyph.Length > 0)
            {
                Rectangle r = new(Theme.S(6), 0, Theme.S(24), e.Item.Height);
                TextRenderer.DrawText(e.Graphics, glyph, Theme.Icon(10f), r, e.Item.Enabled ? Theme.TextMuted : Theme.TextFaint,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }
}

// -----------------------------------------------------------------------------------------------------
/// <summary>
/// Champ « libellé à gauche · contrôle à droite » (formulaires compacts : récapitulatif de caisse). Dans une
/// colonne étroite (&lt; 340 px), le libellé passe au-dessus du contrôle qui prend toute la largeur.
/// </summary>
public sealed class InlineField : LayoutControl
{
    private readonly TextLabel _label;
    private readonly TextLabel? _hint;

    public InlineField(string label, Control control, string? hint = null, int labelPercent = 40)
    {
        LabelPercent = labelPercent;
        _label = new TextLabel { Text = label, Font = Theme.F(9.5f), ForeColor = Theme.TextMuted };
        Controls.Add(_label);
        if (hint is not null)
        {
            _hint = new TextLabel { Text = hint, Font = Theme.F(8.5f), ForeColor = Theme.TextFaint };
            Controls.Add(_hint);
        }

        Control = control;
        LayoutUtil.Prepare(control);
        Controls.Add(control);
    }

    public Control Control { get; }

    public int LabelPercent { get; set; }

    public Label Caption => _label;

    public Label? Hint => _hint;

    /// <summary>Largeur en dessous de laquelle le libellé passe au-dessus (0 = jamais).</summary>
    public int StackBelow { get; set; } = 340;

    private bool IsStacked(int width) => StackBelow > 0 && width > 0 && width < Theme.S(StackBelow) && LabelPercent > 25;

    private int ControlHeight => Math.Max(Control.Height, Theme.ControlHeight);

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        if (IsStacked(width))
        {
            int caption = _label.Font.Height + (_hint is { Text.Length: > 0 } ? Theme.S(2) + _hint.Font.Height : 0);
            return new Size(width, caption + Theme.S(6) + ControlHeight);
        }

        return new Size(width, ControlHeight);
    }

    protected override void Arrange()
    {
        bool hint = _hint is { Text.Length: > 0 };
        if (IsStacked(Width))
        {
            _label.SetBounds(0, 0, Width, _label.Font.Height);
            int y = _label.Font.Height;
            if (hint)
            {
                _hint!.SetBounds(0, y + Theme.S(2), Width, _hint.Font.Height);
                y += Theme.S(2) + _hint.Font.Height;
            }

            Control.SetBounds(0, y + Theme.S(6), Width, Control.Height);
            return;
        }

        int labelWidth = Width * LabelPercent / 100;
        int h = Control.Height;
        Control.SetBounds(labelWidth, (Height - h) / 2, Width - labelWidth, h);
        if (hint)
        {
            int lh = _label.Font.Height;
            int hh = _hint!.Font.Height;
            int top = (Height - lh - hh) / 2;
            _label.SetBounds(0, top, labelWidth - Theme.S(8), lh);
            _hint.SetBounds(0, top + lh, labelWidth - Theme.S(8), hh);
        }
        else
        {
            _label.SetBounds(0, (Height - _label.Font.Height) / 2, labelWidth - Theme.S(8), _label.Font.Height);
        }
    }
}

/// <summary>Séparateur horizontal avec intitulé facultatif (« ACOMPTE ») pour structurer une carte.</summary>
public sealed class SectionRule : Control
{
    public SectionRule(string? caption = null)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Text = caption ?? string.Empty;
        Height = Text.Length > 0 ? Theme.S(24) : Theme.S(9);
    }

    public override Size GetPreferredSize(Size proposedSize) => new(proposedSize.Width, Height);

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Theme.BackOf(this));
        using Pen pen = new(Theme.Border);
        if (Text.Length == 0)
        {
            g.DrawLine(pen, 0, Height / 2, Width, Height / 2);
            return;
        }

        Size size = TextRenderer.MeasureText(Text, Theme.FieldLabel, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, Text, Theme.FieldLabel, new Rectangle(0, 0, size.Width + 2, Height), Theme.Accent,
            TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        g.DrawLine(pen, size.Width + Theme.S(12), Height / 2, Width, Height / 2);
    }
}

/// <summary>
/// Libellé dessiné SANS la marge interne de GDI (1/6 em) : son texte commence exactement au bord du contrôle,
/// ce qui aligne titres, libellés et champs sur la même verticale (le Label standard décale de 3 à 5 px).
/// </summary>
public class TextLabel : Label
{
    public TextLabel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        AutoSize = false;
        UseMnemonic = false;
        BackColor = Color.Transparent;
        Margin = Padding.Empty;
    }

    /// <summary>Retour à la ligne automatique (sinon une ligne avec « … »).</summary>
    public bool Wrap { get; set; }

    private TextFormatFlags Flags
    {
        get
        {
            TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            flags |= Wrap ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            flags |= TextAlign switch
            {
                ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => TextFormatFlags.Right,
                ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter => TextFormatFlags.HorizontalCenter,
                _ => TextFormatFlags.Left
            };
            flags |= TextAlign switch
            {
                ContentAlignment.MiddleLeft or ContentAlignment.MiddleCenter or ContentAlignment.MiddleRight => TextFormatFlags.VerticalCenter,
                ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight => TextFormatFlags.Bottom,
                _ => TextFormatFlags.Top
            };
            return flags;
        }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        if (Wrap && proposedSize.Width > 0)
        {
            Size wrapped = TextRenderer.MeasureText(Text.Length == 0 ? " " : Text, Font, new Size(proposedSize.Width, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
            return new Size(proposedSize.Width, wrapped.Height);
        }

        Size size = TextRenderer.MeasureText(Text.Length == 0 ? " " : Text, Font, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        return new Size(Text.Length == 0 ? 0 : size.Width + 1, Font.Height);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : Theme.TextFaint, Flags);
    }
}

/// <summary>Rangée de N éléments de largeur égale (puces, mini-indicateurs) ; hauteur = la plus grande.</summary>
public sealed class UniformRow : LayoutControl
{
    public UniformRow(int gap = 16)
    {
        Gap = Theme.S(gap);
    }

    public int Gap { get; set; }

    private List<Control> Items => Controls.Cast<Control>().Where(LayoutUtil.IsShown).ToList();

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (e.Control is not null)
        {
            LayoutUtil.Prepare(e.Control);
        }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        List<Control> items = Items;
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        int w = items.Count == 0 ? width : (width - (items.Count - 1) * Gap) / items.Count;
        return new Size(width, items.Count == 0 ? 0 : items.Max(c => LayoutUtil.PreferredHeight(c, w)));
    }

    protected override void Arrange()
    {
        List<Control> items = Items;
        if (items.Count == 0)
        {
            return;
        }

        for (int i = 0; i < items.Count; i++)
        {
            int left = (int)Math.Round(i * (Width + Gap) / (double)items.Count);
            int right = (int)Math.Round((i + 1) * (Width + Gap) / (double)items.Count) - Gap;
            items[i].SetBounds(left, 0, right - left, Height);
        }
    }
}

/// <summary>Avatar rond aux initiales (fiche client).</summary>
public sealed class InitialsAvatar : Control
{
    private string _initials = string.Empty;

    public InitialsAvatar(int size = 64)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Size = new Size(Theme.S(size), Theme.S(size));
    }

    public string Initials
    {
        get => _initials;
        set
        {
            _initials = value ?? string.Empty;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Theme.BackOf(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle r = new(0, 0, Width - 1, Height - 1);
        using SolidBrush brush = new(Theme.Mix(Theme.Surface, Theme.Accent, 0.22f));
        g.FillEllipse(brush, r);
        using Pen pen = new(Theme.Mix(Theme.Surface, Theme.Accent, 0.45f), 1.2f);
        g.DrawEllipse(pen, r);
        TextRenderer.DrawText(g, _initials, Theme.Semi(Height / Theme.Scale / 3.4f), r, Theme.Accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

/// <summary>Enveloppe un contrôle avec des marges intérieures (ex. barre d'outils dans une carte « flush »).</summary>
public sealed class Padded : LayoutControl
{
    private readonly Control _child;

    public Padded(Control child, Padding padding)
    {
        _child = child;
        Inner = padding;
        LayoutUtil.Prepare(child);
        Controls.Add(child);
    }

    public Padding Inner { get; set; }

    public Control Child => _child;

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        return new Size(width, LayoutUtil.PreferredHeight(_child, Math.Max(1, width - Inner.Horizontal)) + Inner.Vertical);
    }

    protected override void Arrange() =>
        _child.SetBounds(Inner.Left, Inner.Top, Math.Max(1, Width - Inner.Horizontal), Math.Max(1, Height - Inner.Vertical));
}

/// <summary>Chiffre clé dans une carte : libellé en majuscules puis grande valeur (solde, total à payer).</summary>
public sealed class FigureBlock : LayoutControl
{
    private readonly TextLabel _caption;

    public FigureBlock(string caption, float size = 24f)
    {
        _caption = new TextLabel { Text = caption.ToUpperInvariant(), Font = Theme.FieldLabel, ForeColor = Theme.TextMuted };
        Value = new TextLabel { Font = Theme.Semi(size), ForeColor = Theme.Figure, Text = "—" };
        Controls.Add(_caption);
        Controls.Add(Value);
    }

    public TextLabel Value { get; }

    public string Caption
    {
        get => _caption.Text;
        set => _caption.Text = value.ToUpperInvariant();
    }

    public override Size GetPreferredSize(Size proposedSize) =>
        new(proposedSize.Width, _caption.Font.Height + Theme.S(4) + Value.Font.Height);

    protected override void Arrange()
    {
        _caption.SetBounds(0, 0, Width, _caption.Font.Height);
        Value.SetBounds(0, _caption.Font.Height + Theme.S(4), Width, Value.Font.Height);
    }
}
