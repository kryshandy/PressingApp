using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace Pressing.Desktop.Ui;

/// <summary>Palette, contrôles et helpers d'interface modernes.</summary>
public static class Theme
{
    public static readonly Color Navy = Color.FromArgb(15, 23, 42);
    public static readonly Color NavyLight = Color.FromArgb(30, 41, 59);
    public static readonly Color NavyHover = Color.FromArgb(51, 65, 85);
    public static readonly Color Blue = Color.FromArgb(37, 99, 235);
    public static readonly Color BlueDark = Color.FromArgb(29, 78, 216);
    public static readonly Color Light = Color.FromArgb(241, 245, 249);
    public static readonly Color White = Color.White;
    public static readonly Color Green = Color.FromArgb(22, 163, 74);
    public static readonly Color Orange = Color.FromArgb(245, 158, 11);
    public static readonly Color Red = Color.FromArgb(220, 38, 38);
    public static readonly Color Slate = Color.FromArgb(51, 65, 85);
    public static readonly Color Muted = Color.FromArgb(100, 116, 139);
    public static readonly Color Border = Color.FromArgb(226, 232, 240);
    public static readonly Color RowAlternate = Color.FromArgb(248, 250, 252);

    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public static Font F(float size, FontStyle style = FontStyle.Regular) => new("Segoe UI", size, style);

    public static string Money(decimal valeur) => valeur.ToString("N0", Fr) + " F";

    public static string Date(DateTime valeur) => valeur.ToString("dd/MM/yyyy", Fr);

    public static string DateHeure(DateTime valeur) => valeur.ToString("dd/MM/yyyy HH:mm", Fr);

    public static bool TryDecimal(string? texte, out decimal valeur)
    {
        if (string.IsNullOrWhiteSpace(texte))
        {
            valeur = 0;
            return false;
        }

        return decimal.TryParse(
            texte.Trim().Replace(" ", "").Replace("\u00a0", "").Replace(',', '.'),
            NumberStyles.Number, CultureInfo.InvariantCulture, out valeur);
    }

    public static (Color Fore, Color Back) StatusColors(string statut) => statut switch
    {
        "Déposé" => (Blue, Color.FromArgb(230, 240, 255)),
        "En traitement" => (Color.FromArgb(180, 83, 9), Color.FromArgb(255, 243, 221)),
        "Prêt" => (Green, Color.FromArgb(228, 249, 236)),
        "Retiré" => (Muted, Color.FromArgb(238, 241, 245)),
        _ => (Slate, Light)
    };

    public static Label Label(
        string texte,
        Font? police = null,
        Color? couleur = null,
        DockStyle? dock = null,
        int? hauteur = null,
        bool? autoSize = null) => new()
    {
        Text = texte,
        Font = police ?? F(9f),
        ForeColor = couleur ?? Slate,
        BackColor = Color.Transparent,
        AutoSize = autoSize ?? true,
        Dock = dock ?? DockStyle.None,
        Height = hauteur ?? 23
    };

    public static Label Titre(string texte, DockStyle? dock = null, int? hauteur = null) =>
        Label(texte, F(12f, FontStyle.Bold), Navy, dock, hauteur);

    public static Panel Champ(string libelle, Control controle)
    {
        Panel panneau = new() { BackColor = Color.Transparent, Padding = new Padding(0, 0, 10, 8) };
        Label titre = new()
        {
            Text = libelle,
            Dock = DockStyle.Top,
            Height = 18,
            Font = Theme.F(8.5f, FontStyle.Bold),
            ForeColor = Theme.Muted,
            BackColor = Color.Transparent
        };
        controle.Dock = DockStyle.Fill;
        panneau.Controls.Add(controle);
        panneau.Controls.Add(titre);
        return panneau;
    }

    public static TextBox Texte(string? valeur = null, bool lectureSeule = false) => new()
    {
        Text = valeur ?? string.Empty,
        ReadOnly = lectureSeule,
        Font = Theme.F(9.5f),
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = lectureSeule ? RowAlternate : White,
        ForeColor = lectureSeule ? Muted : Navy,
        Height = 30,
        Margin = new Padding(2)
    };

    public static Label EmptyLabel(string texte = "Aucune donnée disponible.") => new()
    {
        Text = texte,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = Theme.F(9f, FontStyle.Italic),
        ForeColor = Theme.Muted,
        BackColor = Color.Transparent,
        Visible = false
    };

    public static void StyleGrid(DataGridView grid, bool lectureSeule = true)
    {
        grid.BackgroundColor = White;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.None;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249);
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(241, 245, 249);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Muted;
        grid.ColumnHeadersDefaultCellStyle.Font = Theme.F(8.5f, FontStyle.Bold);
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
        grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        grid.ColumnHeadersHeight = 36;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.DefaultCellStyle.Font = Theme.F(9f);
        grid.DefaultCellStyle.ForeColor = Slate;
        grid.DefaultCellStyle.BackColor = Theme.White;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 254);
        grid.DefaultCellStyle.SelectionForeColor = Navy;
        grid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        grid.AlternatingRowsDefaultCellStyle.BackColor = RowAlternate;
        grid.RowHeadersVisible = false;
        grid.RowTemplate.Height = 38;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.ReadOnly = lectureSeule;
        grid.GridColor = Border;
        grid.EditMode = DataGridViewEditMode.EditOnEnter;
        grid.ShowCellToolTips = true;
        grid.StandardTab = true;
    }

    public static void StatusFormatting(object? sender, DataGridViewCellFormattingEventArgs e, string colonne)
    {
        if (sender is not DataGridView grid || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (grid.Columns[e.ColumnIndex].Name != colonne) return;

        var couleurs = StatusColors(e.Value?.ToString() ?? string.Empty);
        e.CellStyle.ForeColor = couleurs.Fore;
        e.CellStyle.BackColor = couleurs.Back;
        e.CellStyle.Font = Theme.F(9f, FontStyle.Bold);
        e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
    }

    public static void ExpressFormatting(object? sender, DataGridViewCellFormattingEventArgs e, string colonne)
    {
        if (sender is not DataGridView grid || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (grid.Columns[e.ColumnIndex].Name != colonne) return;

        string texte = e.Value?.ToString() ?? string.Empty;
        if (texte.Contains("EXPRESS", StringComparison.OrdinalIgnoreCase))
        {
            e.CellStyle.ForeColor = BlueDark;
            e.CellStyle.BackColor = Color.FromArgb(219, 234, 254);
            e.CellStyle.Font = Theme.F(8.5f, FontStyle.Bold);
            e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }
    }

    public static void Error(Exception ex, string contexte = "l'opération")
    {
        MessageBox.Show(
            "Impossible d'effectuer " + contexte + ".\n\n" + ex.Message,
            "Pressing — Erreur",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    public static void Info(string message, string titre = "Pressing") =>
        MessageBox.Show(message, titre, MessageBoxButtons.OK, MessageBoxIcon.Information);

    public static bool Confirm(string message) =>
        MessageBox.Show(message, "Pressing — Confirmation", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
        == DialogResult.Yes;
}

/// <summary>Common DPI-aware base for staff windows and selection dialogs.</summary>
public abstract class PressingForm : Form
{
    protected PressingForm()
    {
        // Base 96 dpi : sans elle, les tailles en pixels (menu, lignes, boutons) ne suivent
        // pas l'échelle Windows (125 %, 150 %...) alors que les polices, si => textes coupés.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = Theme.F(9.5f);
        BackColor = Theme.Light;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
    }
}

/// <summary>Bouton plat à coins arrondis, avec survol et variantes (plein, secondaire, contour).</summary>
public class FlatButton : Button
{
    private Color _fond = Theme.Blue;
    private Color _survol = Theme.BlueDark;
    private Color _texte = Color.White;
    private Color? _bordure;
    private bool _survole;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Rayon { get; set; } = 14;

    public FlatButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Font = Theme.F(9.5f, FontStyle.Bold);
        Height = 38;
        Padding = new Padding(12, 0, 12, 0);
        Cursor = Cursors.Hand;
        TextAlign = ContentAlignment.MiddleCenter;
    }

    /// <summary>Bouton plein : couleur de fond et couleur au survol.</summary>
    public void SetColors(Color fond, Color survol)
    {
        _fond = fond;
        _survol = survol;
        _texte = Color.White;
        _bordure = null;
        Invalidate();
    }

    /// <summary>Bouton secondaire : fond blanc, fine bordure grise.</summary>
    public void SetSecondary()
    {
        _fond = Color.White;
        _survol = Color.FromArgb(241, 245, 249);
        _texte = Theme.Slate;
        _bordure = Theme.Border;
        Invalidate();
    }

    /// <summary>Bouton contour : fond blanc, texte et bordure de la couleur d'accent.</summary>
    public void SetOutline(Color accent)
    {
        _fond = Color.White;
        _survol = Color.FromArgb(
            255 - (255 - accent.R) / 10, 255 - (255 - accent.G) / 10, 255 - (255 - accent.B) / 10);
        _texte = accent;
        _bordure = accent;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        ButtonRenderer.DrawParentBackground(g, ClientRectangle, this);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Rectangle zone = new(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        int diametre = (int)(Rayon * DeviceDpi / 96f);
        Color fond = !Enabled ? Color.FromArgb(226, 232, 240) : _survole ? _survol : _fond;
        Color texte = Enabled ? _texte : Theme.Muted;

        using GraphicsPath chemin = RoundedPanel.CheminArrondi(zone, diametre);
        using (SolidBrush pinceau = new(fond)) g.FillPath(pinceau, chemin);
        if (_bordure.HasValue && Enabled)
        {
            using Pen stylo = new(_bordure.Value, 1f);
            g.DrawPath(stylo, chemin);
        }

        TextFormatFlags drapeaux = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                                   TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        drapeaux |= TextAlign switch
        {
            ContentAlignment.MiddleLeft or ContentAlignment.TopLeft or ContentAlignment.BottomLeft
                => TextFormatFlags.Left,
            ContentAlignment.MiddleRight or ContentAlignment.TopRight or ContentAlignment.BottomRight
                => TextFormatFlags.Right,
            _ => TextFormatFlags.HorizontalCenter
        };
        Rectangle zoneTexte = new(
            Padding.Left, 0, Math.Max(1, Width - Padding.Horizontal), Height);
        TextRenderer.DrawText(g, Text, Font, zoneTexte, texte, drapeaux);

        if (Focused && ShowFocusCues && Enabled)
        {
            using Pen focus = new(Color.FromArgb(147, 197, 253), 2f);
            Rectangle f = Rectangle.Inflate(zone, -2, -2);
            using GraphicsPath cheminFocus = RoundedPanel.CheminArrondi(f, Math.Max(2, diametre - 4));
            g.DrawPath(focus, cheminFocus);
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _survole = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _survole = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
}

/// <summary>Entrée du menu latéral : icône Segoe MDL2, libellé, état survol / actif.</summary>
public sealed class NavButton : Control
{
    private bool _survole;
    private bool _actif;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Icone { get; set; } = string.Empty;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Actif
    {
        get => _actif;
        set { _actif = value; Invalidate(); }
    }

    public NavButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable | ControlStyles.StandardClick, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        Font = Theme.F(10f, FontStyle.Regular);
        Height = 44;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Theme.Navy);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float k = DeviceDpi / 96f;
        Rectangle zone = new(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        if (_actif || _survole || Focused)
        {
            using GraphicsPath chemin = RoundedPanel.CheminArrondi(zone, (int)(16 * k));
            using SolidBrush fond = new(_actif ? Theme.Blue : Theme.NavyLight);
            g.FillPath(fond, chemin);
        }

        Color texte = _actif || _survole ? Color.White : Color.FromArgb(203, 213, 225);
        using Font police = new("Segoe MDL2 Assets", 11.5f);
        TextRenderer.DrawText(g, Icone, police,
            new Rectangle((int)(14 * k), 0, (int)(28 * k), Height), texte,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, Text, Font,
            new Rectangle((int)(52 * k), 0, Math.Max(1, Width - (int)(60 * k)), Height), texte,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix);
    }

    protected override void OnMouseEnter(EventArgs e) { _survole = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _survole = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
        base.OnKeyDown(e);
    }
}

/// <summary>Panneau à coins arrondis avec bordure claire.</summary>
public class RoundedPanel : Panel
{
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Rayon { get; set; } = 12;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color CouleurBordure { get; set; } = Theme.Border;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int EpaisseurBordure { get; set; } = 1;

    public RoundedPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.White;
        Padding = new Padding(14);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle rectangle = new(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using GraphicsPath chemin = CheminArrondi(rectangle, Rayon);
        using SolidBrush pinceau = new(BackColor);
        e.Graphics.FillPath(pinceau, chemin);
        using Pen stylo = new(CouleurBordure, EpaisseurBordure);
        e.Graphics.DrawPath(stylo, chemin);
    }

    public static GraphicsPath CheminArrondi(Rectangle rectangle, int rayon)
    {
        GraphicsPath chemin = new();
        int r = Math.Max(0, Math.Min(rayon, Math.Min(rectangle.Width, rectangle.Height) / 2));
        chemin.AddArc(rectangle.Left, rectangle.Top, r, r, 180, 90);
        chemin.AddArc(rectangle.Right - r, rectangle.Top, r, r, 270, 90);
        chemin.AddArc(rectangle.Right - r, rectangle.Bottom - r, r, r, 0, 90);
        chemin.AddArc(rectangle.Left, rectangle.Bottom - r, r, r, 90, 90);
        chemin.CloseFigure();
        return chemin;
    }
}

/// <summary>Carte de statistique avec valeur dynamique.</summary>
public sealed class StatCard : RoundedPanel
{
    public Label Valeur { get; }

    private readonly Label _note;
    private readonly Color _accent;

    public StatCard(string titre, string valeur, Color accent, string note = "")
    {
        Rayon = 14;
        Padding = new Padding(0);
        _accent = accent;
        Panel contenu = new() { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(26, 14, 10, 10) };

        Label titreLabel = new()
        {
            Text = titre.ToUpperInvariant(),
            Dock = DockStyle.Top,
            Height = 20,
            AutoEllipsis = true,
            Font = Theme.F(8f, FontStyle.Bold),
            ForeColor = Theme.Muted,
            BackColor = Color.Transparent
        };
        Valeur = new Label
        {
            Text = valeur,
            Dock = DockStyle.Top,
            Height = 36,
            AutoEllipsis = true,
            Font = Theme.F(17f, FontStyle.Bold),
            ForeColor = Theme.Navy,
            BackColor = Color.Transparent
        };
        _note = new Label
        {
            Text = note,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Font = Theme.F(8.5f),
            ForeColor = Theme.Muted,
            BackColor = Color.Transparent
        };
        contenu.Controls.Add(_note);
        contenu.Controls.Add(Valeur);
        contenu.Controls.Add(titreLabel);
        Controls.Add(contenu);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        float k = DeviceDpi / 96f;
        Rectangle pastille = new((int)(14 * k), (int)(16 * k), Math.Max(3, (int)(4 * k)),
            Math.Max(8, Height - (int)(32 * k)));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using GraphicsPath chemin = CheminArrondi(pastille, pastille.Width);
        using SolidBrush pinceau = new(_accent);
        e.Graphics.FillPath(pinceau, chemin);
    }

    public void Set(string valeur, string? note = null)
    {
        Valeur.Text = valeur;
        if (note != null) _note.Text = note;
    }
}
