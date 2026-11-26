using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using Pressing.Client;
using Pressing.Core;

namespace Pressing.Desktop.Ui;

/// <summary>
/// Palette, polices, espacements et petits utilitaires du thème sombre « Pressing Éclat ».
/// Toutes les vues et boîtes de dialogue n'utilisent que ces valeurs (jamais de couleur codée en dur).
/// </summary>
public static class Theme
{
    // ---- Palette -------------------------------------------------------------------------
    public static readonly Color Background = ColorTranslator.FromHtml("#0F172A");
    public static readonly Color Sidebar = ColorTranslator.FromHtml("#0B1222");
    public static readonly Color Surface = ColorTranslator.FromHtml("#1E293B");
    public static readonly Color SurfaceAlt = ColorTranslator.FromHtml("#162033");
    public static readonly Color SurfaceHover = ColorTranslator.FromHtml("#273449");
    public static readonly Color Border = ColorTranslator.FromHtml("#334155");
    public static readonly Color Text = ColorTranslator.FromHtml("#E2E8F0");
    public static readonly Color TextMuted = ColorTranslator.FromHtml("#94A3B8");
    public static readonly Color TextFaint = ColorTranslator.FromHtml("#64748B");
    public static readonly Color Accent = ColorTranslator.FromHtml("#C8A15A");
    public static readonly Color AccentHover = ColorTranslator.FromHtml("#D9B672");
    public static readonly Color AccentPressed = ColorTranslator.FromHtml("#B38C47");
    public static readonly Color OnAccent = ColorTranslator.FromHtml("#1B1407");
    public static readonly Color Blue = ColorTranslator.FromHtml("#3B82F6");
    public static readonly Color Green = ColorTranslator.FromHtml("#10B981");
    public static readonly Color Amber = ColorTranslator.FromHtml("#F59E0B");
    public static readonly Color Red = ColorTranslator.FromHtml("#EF4444");
    public static readonly Color Violet = ColorTranslator.FromHtml("#8B5CF6");
    public static readonly Color Slate = ColorTranslator.FromHtml("#64748B");

    /// <summary>Sélection de ligne : bleu translucide (composé sur la surface pour GDI).</summary>
    public static readonly Color SelectionBack = Mix(Surface, Blue, 0.30f);

    /// <summary>Couleurs de la hiérarchie typographique (DESIGN.md §3).</summary>
    public static readonly Color TitleText = ColorTranslator.FromHtml("#F1F5F9");
    public static readonly Color Figure = ColorTranslator.FromHtml("#F8FAFC");
    public static readonly Color ScrollThumb = ColorTranslator.FromHtml("#334155");
    public static readonly Color ScrollThumbHot = ColorTranslator.FromHtml("#475569");

    // ---- DPI -----------------------------------------------------------------------------
    /// <summary>Facteur d'échelle (1.0 = 96 dpi). Initialisé au démarrage par <see cref="InitializeDpi"/>.</summary>
    public static float Scale { get; private set; } = 1f;

    /// <summary>Multiplicateur de police (1 sauf simulation de test via PRESSING_UI_SCALE).</summary>
    public static float FontScale { get; private set; } = 1f;

    public static void InitializeDpi()
    {
        try
        {
            using Graphics g = Graphics.FromHwnd(IntPtr.Zero);
            Scale = Math.Max(1f, g.DpiX / 96f);
        }
        catch
        {
            Scale = 1f;
        }

        // Simulation d'un affichage 125 % / 150 % pour les tests de mise en page (écran à 96 dpi).
        if (float.TryParse(Environment.GetEnvironmentVariable("PRESSING_UI_SCALE"), System.Globalization.NumberStyles.Float,
                CultureInfo.InvariantCulture, out float forced) && forced is >= 1f and <= 3f)
        {
            Scale = forced;
            FontScale = forced;
        }
    }

    /// <summary>Convertit une taille en pixels logiques (96 dpi) vers l'écran courant.</summary>
    public static int S(int px) => (int)Math.Round(px * Scale);

    public static float S(float px) => px * Scale;

    public static Padding Pad(int all) => new(S(all));

    public static Padding Pad(int horizontal, int vertical) => new(S(horizontal), S(vertical), S(horizontal), S(vertical));

    public static Padding Pad(int left, int top, int right, int bottom) => new(S(left), S(top), S(right), S(bottom));

    // ---- Polices -------------------------------------------------------------------------
    private static readonly Dictionary<(float, FontStyle), Font> FontCache = new();
    private static readonly Dictionary<float, Font> IconCache = new();
    private static string? _family;
    private static string? _iconFamily;

    public static string FontFamily => _family ??= Detect("Segoe UI Variable Text", "Segoe UI");

    public static string IconFamily => _iconFamily ??= Detect("Segoe Fluent Icons", "Segoe MDL2 Assets");

    private static string Detect(params string[] names)
    {
        using InstalledFontCollection fonts = new();
        foreach (string name in names)
        {
            if (fonts.Families.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return name;
            }
        }

        return names[^1];
    }

    /// <summary>Police du thème (mise en cache : ne pas la libérer).</summary>
    public static Font F(float size, FontStyle style = FontStyle.Regular)
    {
        lock (FontCache)
        {
            if (!FontCache.TryGetValue((size, style), out Font? font))
            {
                font = new Font(FontFamily, size * FontScale, style, GraphicsUnit.Point);
                FontCache[(size, style)] = font;
            }

            return font;
        }
    }

    private static string? _semiFamily;
    private static readonly Dictionary<float, Font> SemiCache = new();

    /// <summary>Famille « Semibold » (titres) : Segoe UI Variable / Segoe UI Semibold, sinon gras.</summary>
    public static string? SemiboldFamily => _semiFamily ??= DetectOrEmpty("Segoe UI Variable Text Semibold", "Segoe UI Semibold");

    private static string DetectOrEmpty(params string[] names)
    {
        using InstalledFontCollection fonts = new();
        foreach (string name in names)
        {
            if (fonts.Families.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return name;
            }
        }

        return string.Empty;
    }

    /// <summary>Police semi-grasse (titres de page et de carte, libellés de champ).</summary>
    public static Font Semi(float size)
    {
        if (string.IsNullOrEmpty(SemiboldFamily))
        {
            return F(size, FontStyle.Bold);
        }

        lock (SemiCache)
        {
            if (!SemiCache.TryGetValue(size, out Font? font))
            {
                font = new Font(SemiboldFamily, size * FontScale, FontStyle.Regular, GraphicsUnit.Point);
                SemiCache[size] = font;
            }

            return font;
        }
    }

    // ---- Charte de mise en page (DESIGN.md §1) ------------------------------------------
    /// <summary>Gouttière de page (bords gauche/droit communs à l'en-tête global, au titre et aux cartes) : 32 px.</summary>
    public static int PageGutter => S(32);

    /// <summary>Gouttière entre colonnes de la grille de 12 et espacement vertical entre blocs : 24 px.</summary>
    public static int Gap => S(24);

    /// <summary>Padding interne des cartes : 24 px.</summary>
    public static int CardPadding => S(24);

    /// <summary>Hauteur standard des boutons et des champs : 40 px.</summary>
    public static int ControlHeight => S(40);

    public static Font PageTitle => Semi(22f);
    public static Font CardTitle => Semi(12f);
    public static Font FieldLabel => Semi(8.5f);

    public static Font Body => F(10f);
    public static Font BodyBold => F(10f, FontStyle.Bold);
    public static Font Small => F(9f);
    public static Font SmallBold => F(9f, FontStyle.Bold);
    public static Font Title => F(20f, FontStyle.Bold);
    public static Font Heading => F(14f, FontStyle.Bold);
    public static Font SubHeading => F(11f, FontStyle.Bold);

    public static Font Icon(float size)
    {
        lock (IconCache)
        {
            if (!IconCache.TryGetValue(size, out Font? font))
            {
                font = new Font(IconFamily, size * FontScale, FontStyle.Regular, GraphicsUnit.Point);
                IconCache[size] = font;
            }

            return font;
        }
    }

    // ---- Couleurs ------------------------------------------------------------------------
    public static Color FromHex(string hex)
    {
        try
        {
            return ColorTranslator.FromHtml(hex);
        }
        catch
        {
            return Slate;
        }
    }

    public static Color Mix(Color under, Color over, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)(under.R + (over.R - under.R) * amount),
            (int)(under.G + (over.G - under.G) * amount),
            (int)(under.B + (over.B - under.B) * amount));
    }

    /// <summary>Couleur de fond opaque effective d'un contrôle (remonte les parents transparents).</summary>
    public static Color BackOf(Control? control)
    {
        for (Control? p = control?.Parent; p is not null; p = p.Parent)
        {
            if (p.BackColor.A == 255)
            {
                return p.BackColor;
            }
        }

        return Background;
    }

    public static Color Alpha(Color color, int alpha) => Color.FromArgb(alpha, color);

    public static Color StatusColor(OrderStatus status) => FromHex(Pressing.Client.Fmt.StatusColor(status));

    public static Color StatusColor(ItemStatus status) => FromHex(Pressing.Client.Fmt.StatusColor(status));

    // ---- Géométrie -----------------------------------------------------------------------
    public static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        GraphicsPath path = new();
        int d = Math.Max(0, Math.Min(radius * 2, Math.Min(rect.Width, rect.Height)));
        if (d <= 1)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Barre arrondie seulement en haut (graphiques).</summary>
    public static GraphicsPath TopRoundedRect(RectangleF rect, float radius)
    {
        GraphicsPath path = new();
        float d = Math.Max(0, Math.Min(radius * 2, Math.Min(rect.Width, rect.Height * 2)));
        if (d <= 1)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddLine(rect.Right, rect.Bottom, rect.Left, rect.Bottom);
        path.CloseFigure();
        return path;
    }

    // ---- Saisie / formats ----------------------------------------------------------------
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Lit un montant saisi à la française (« 12 500 », « 1500,50 », « 1.500,50 », « 1 500 FCFA »).</summary>
    public static bool TryParseAmount(string? text, out decimal value) => AmountParser.TryParseAmount(text, out value);

    public static string AmountText(decimal value) =>
        Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("0", Fr);
}
