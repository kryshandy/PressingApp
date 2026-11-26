using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Pressing.Client;

namespace Pressing.Desktop.Ui;

/// <summary>Appels natifs : barre de titre sombre (Windows 10/11).</summary>
public static class Native
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// Barre de titre en mode sombre : <c>DWMWA_USE_IMMERSIVE_DARK_MODE</c> (20, ou 19 sur les anciens Windows 10)
    /// puis, sous Windows 11, couleur de légende = fond de l'application (35) et texte clair (36).
    /// </summary>
    public static void DarkTitleBar(Form form)
    {
        try
        {
            int on = 1;
            if (DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(form.Handle, 19, ref on, sizeof(int));
            }

            int caption = ColorTranslator.ToWin32(Theme.Background);
            DwmSetWindowAttribute(form.Handle, 35, ref caption, sizeof(int));
            int text = ColorTranslator.ToWin32(Theme.Text);
            DwmSetWindowAttribute(form.Handle, 36, ref text, sizeof(int));
            int border = ColorTranslator.ToWin32(Theme.Border);
            DwmSetWindowAttribute(form.Handle, 34, ref border, sizeof(int));
        }
        catch
        {
            // Windows plus ancien : la barre de titre reste standard.
        }
    }
}

/// <summary>Barres de défilement natives résiduelles (zones de texte multilignes) : thème sombre de l'Explorateur.</summary>
public static class NativeScroll
{
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);

    public static void Darken(Control control)
    {
        try
        {
            SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
        }
        catch
        {
            // sans importance
        }
    }
}

/// <summary>Exécution d'opérations asynchrones avec curseur d'attente, boutons occupés et erreurs en boîte de dialogue.</summary>
public static class UiRun
{
    private static readonly HashSet<Control> Active = new();

    /// <summary>
    /// Exécute <paramref name="action"/> : curseur d'attente, boutons grisés, <see cref="ApiException"/> affichée
    /// (sauf 401 géré par l'application). Retourne vrai si l'opération a réussi.
    /// </summary>
    public static async Task<bool> RunAsync(Control scope, Func<Task> action, string? context = null)
    {
        if (scope.IsDisposed || !Active.Add(scope))
        {
            return false;
        }

        SetBusy(scope, true);
        try
        {
            await action();
            return true;
        }
        catch (ApiException ex) when (ex.IsUnauthorized)
        {
            return false; // la coque principale renvoie vers l'écran de connexion
        }
        catch (ApiException ex)
        {
            if (!scope.IsDisposed)
            {
                AppDialogs.Error(scope, ex.Message, context);
            }

            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        catch (Exception ex)
        {
            if (!scope.IsDisposed)
            {
                AppDialogs.Error(scope, "Une erreur inattendue est survenue : " + ex.Message, context);
            }

            return false;
        }
        finally
        {
            Active.Remove(scope);
            if (!scope.IsDisposed)
            {
                SetBusy(scope, false);
            }
        }
    }

    public static void SetBusy(Control scope, bool busy)
    {
        scope.UseWaitCursor = busy;
        MarkButtons(scope, busy);
    }

    private static void MarkButtons(Control parent, bool busy)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is FlatButton button)
            {
                button.Busy = busy;
            }

            if (child.HasChildren)
            {
                MarkButtons(child, busy);
            }
        }
    }
}

/// <summary>
/// Fenêtre modale sombre (DESIGN.md) : largeur fixe cohérente (480 · 560 · 760 px), en-tête (titre 16 pt), corps
/// avec gouttière de 32 px et blocs espacés de 16 px, pied avec message d'erreur à gauche et boutons alignés à
/// droite (Secondary puis Primary). La hauteur s'ajuste au contenu ; au-delà de l'écran, le corps défile sans
/// barre native. Les dialogues ajoutent leurs blocs à <see cref="Body"/> avec <c>Dock = Top</c> (le dernier ajouté
/// est en haut, comme d'habitude) ; un bloc <c>Dock = Fill</c> prend la hauteur restante.
/// </summary>
public class DialogForm : Form
{
    private readonly DialogHeader _header;
    private readonly Label _error;
    private readonly FlowLayoutPanel _buttons;
    private readonly Panel _footer;
    private readonly ScrollViewport _viewport;
    private readonly int _requestedHeight;

    public DialogForm(string title, string? subtitle, int width, int height)
    {
        AutoScaleMode = AutoScaleMode.None;
        Font = Theme.Body;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = title;
        KeyPreview = true;
        int logicalWidth = width <= 500 ? 480 : width <= 620 ? 560 : 760;
        _requestedHeight = height;
        Rectangle area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        ClientSize = new Size(Math.Min(Theme.S(logicalWidth), area.Width - Theme.S(40)), Math.Min(Theme.S(height), area.Height - Theme.S(110)));

        _header = new DialogHeader(title, subtitle) { Dock = DockStyle.Top };

        Body = new DialogBody();
        _viewport = new ScrollViewport
        {
            Dock = DockStyle.Fill,
            Content = Body,
            Padding = new Padding(Theme.PageGutter, Theme.S(8), Theme.PageGutter, Theme.S(24))
        };

        _footer = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(80), BackColor = Theme.Sidebar };
        Panel line = new() { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border };
        TableLayoutPanel footer = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(Theme.PageGutter, Theme.S(19), Theme.PageGutter, Theme.S(20)),
            BackColor = Theme.Sidebar
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _error = new TextLabel
        {
            Dock = DockStyle.Fill,
            ForeColor = Theme.Red,
            Font = Theme.F(9.5f),
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, Theme.S(12), 0)
        };
        _buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
            Dock = DockStyle.Fill
        };
        footer.Controls.Add(_error, 0, 0);
        footer.Controls.Add(_buttons, 1, 0);
        _footer.Controls.Add(footer);
        _footer.Controls.Add(line);

        Controls.Add(_viewport);
        Controls.Add(_footer);
        Controls.Add(_header);
        _viewport.BringToFront();

        Secondary = new FlatButton { Text = "Annuler", Kind = ButtonKind.Secondary, DialogResult = DialogResult.Cancel, Margin = new Padding(0, 0, Theme.S(8), 0) };
        Primary = new FlatButton { Text = "Valider", Kind = ButtonKind.Primary, Margin = Padding.Empty };
        _buttons.Controls.Add(Primary);
        _buttons.Controls.Add(Secondary);
        CancelButton = Secondary;
        AcceptButton = Primary;
    }

    /// <summary>Corps du dialogue (blocs empilés, 16 px d'écart).</summary>
    public DialogBody Body { get; }

    public FlatButton Primary { get; }

    public FlatButton Secondary { get; }

    /// <summary>Ajoute un bouton supplémentaire à gauche des boutons standards.</summary>
    protected FlatButton AddExtraButton(string text, ButtonKind kind = ButtonKind.Secondary, string icon = "")
    {
        FlatButton button = new() { Text = text, Kind = kind, IconGlyph = icon, Margin = new Padding(0, 0, Theme.S(8), 0) };
        _buttons.Controls.Add(button);
        return button;
    }

    public void ShowError(string? message) => _error.Text = message ?? string.Empty;

    /// <summary>Ajuste la hauteur de la fenêtre au contenu (bornée par l'écran).</summary>
    protected void FitToContent()
    {
        Rectangle area = Screen.FromControl(this).WorkingArea;
        int width = ClientSize.Width;
        int headerHeight = _header.GetPreferredSize(new Size(width, 0)).Height;
        int bodyHeight = Body.HasFill
            ? Theme.S(_requestedHeight) - headerHeight - _footer.Height
            : Body.GetPreferredSize(new Size(width - _viewport.Padding.Horizontal, 0)).Height + _viewport.Padding.Vertical;
        int total = headerHeight + bodyHeight + _footer.Height;
        ClientSize = new Size(width, Math.Clamp(total, Theme.S(180), area.Height - Theme.S(80)));
    }

    protected override void OnLoad(EventArgs e)
    {
        FitToContent();
        base.OnLoad(e);
        if (StartPosition == FormStartPosition.CenterParent && Owner is not null)
        {
            CenterToParent();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.DarkTitleBar(this);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
    }

    private sealed class DialogHeader : LayoutControl
    {
        private readonly TextLabel _title;
        private readonly TextLabel _subtitle;

        public DialogHeader(string title, string? subtitle)
        {
            BackColor = Theme.Background;
            _title = new TextLabel { Text = title, Font = Theme.Semi(16f), ForeColor = Theme.TitleText };
            _subtitle = new TextLabel { Text = subtitle ?? string.Empty, Font = Theme.F(10f), ForeColor = Theme.TextMuted, Wrap = true };
            Controls.Add(_title);
            Controls.Add(_subtitle);
        }

        private int SubtitleHeight(int width) => _subtitle.Text.Length == 0 ? 0
            : Theme.S(4) + _subtitle.GetPreferredSize(new Size(Math.Max(10, width - 2 * Theme.PageGutter), 0)).Height;

        public override Size GetPreferredSize(Size proposedSize)
        {
            int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
            return new Size(width, Theme.S(28) + _title.Font.Height + SubtitleHeight(width) + Theme.S(16));
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            int wanted = GetPreferredSize(new Size(Width, 0)).Height;
            if (Height != wanted && Width > 0)
            {
                Height = wanted;
            }

            base.OnLayout(levent);
        }

        protected override void Arrange()
        {
            int x = Theme.PageGutter;
            int w = Math.Max(10, Width - 2 * x);
            _title.SetBounds(x, Theme.S(28), w, _title.Font.Height);
            _subtitle.Visible = _subtitle.Text.Length > 0;
            _subtitle.SetBounds(x, Theme.S(28) + _title.Font.Height + Theme.S(4), w, Math.Max(0, SubtitleHeight(Width) - Theme.S(4)));
        }
    }
}

/// <summary>
/// Corps d'un dialogue : empile ses enfants comme <c>Dock = Top</c> (le dernier ajouté en haut), 16 px d'écart,
/// chacun à sa hauteur préférée ; un enfant <c>Dock = Fill</c> prend la hauteur restante.
/// </summary>
public sealed class DialogBody : LayoutControl
{
    public int Gap { get; set; } = Theme.S(16);

    private IEnumerable<Control> Ordered => Controls.Cast<Control>().Reverse().Where(LayoutUtil.IsShown);

    /// <summary>Vrai si un bloc doit remplir la hauteur (grille).</summary>
    public bool HasFill => Controls.Cast<Control>().Any(c => c.Tag as string == "fill" || c.Dock == DockStyle.Fill);

    private static bool IsFill(Control c) => c.Tag as string == "fill" || c.Dock == DockStyle.Fill;

    private static int HeightOf(Control c, int width) =>
        c is LayoutControl or TableLayoutPanel or TextLabel or RoundedPanel { AutoSize: true } || c.AutoSize || c is FieldBox
            ? LayoutUtil.PreferredHeight(c, width)
            : c.Height;

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (e.Control is { } c)
        {
            if (c.Dock == DockStyle.Fill)
            {
                c.Tag ??= "fill";
            }

            c.Dock = DockStyle.None;
            c.Margin = Padding.Empty;
        }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        List<Control> list = Ordered.ToList();
        int height = list.Sum(c => IsFill(c) ? Math.Max(c.MinimumSize.Height, Theme.S(120)) : HeightOf(c, width));
        return new Size(width, height + Math.Max(0, list.Count - 1) * Gap);
    }

    protected override void Arrange()
    {
        List<Control> list = Ordered.ToList();
        int fixedHeight = list.Where(c => !IsFill(c)).Sum(c => HeightOf(c, Width)) + Math.Max(0, list.Count - 1) * Gap;
        int fills = list.Count(IsFill);
        int fillHeight = fills == 0 ? 0 : Math.Max(Theme.S(120), (Height - fixedHeight) / fills);
        int y = 0;
        foreach (Control c in list)
        {
            int h = IsFill(c) ? fillHeight : HeightOf(c, Width);
            c.SetBounds(0, y, Width, h);
            y += h + Gap;
        }
    }
}

public enum MessageKind
{
    Info,
    Success,
    Warning,
    Error,
    Question
}

/// <summary>Boîte de message stylée (information, erreur, confirmation).</summary>
public sealed class MessageDialog : DialogForm
{
    public MessageDialog(string title, string message, MessageKind kind, string primaryText, string? secondaryText, bool danger)
        : base(title, null, 480, 240)
    {
        Color accent = kind switch
        {
            MessageKind.Error => Theme.Red,
            MessageKind.Warning => Theme.Amber,
            MessageKind.Success => Theme.Green,
            MessageKind.Question => Theme.Accent,
            _ => Theme.Blue
        };
        string glyph = kind switch
        {
            MessageKind.Error => Icons.Error,
            MessageKind.Warning => Icons.Warning,
            MessageKind.Success => Icons.Check,
            MessageKind.Question => Icons.Info,
            _ => Icons.Info
        };

        Primary.Text = primaryText;
        Primary.Kind = danger ? ButtonKind.Danger : ButtonKind.Primary;
        if (secondaryText is null)
        {
            Secondary.Visible = false;
            CancelButton = Primary;
            Primary.DialogResult = DialogResult.OK;
        }
        else
        {
            Secondary.Text = secondaryText;
            Primary.DialogResult = DialogResult.OK;
        }

        Body.Controls.Add(new MessageBlock(new IconBadge(glyph, accent), message));
    }

    /// <summary>Pastille à gauche, message (retour à la ligne) à droite, alignés en haut.</summary>
    private sealed class MessageBlock : LayoutControl
    {
        private readonly IconBadge _badge;
        private readonly TextLabel _text;

        public MessageBlock(IconBadge badge, string message)
        {
            _badge = badge;
            _text = new TextLabel { Text = message, Wrap = true, Font = Theme.F(10.5f), ForeColor = Theme.Text };
            Controls.Add(badge);
            Controls.Add(_text);
        }

        private int TextLeft => _badge.Width + Theme.S(16);

        public override Size GetPreferredSize(Size proposedSize)
        {
            int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
            int text = _text.GetPreferredSize(new Size(Math.Max(10, width - TextLeft), 0)).Height;
            return new Size(width, Math.Max(_badge.Height, text + Theme.S(12)));
        }

        protected override void Arrange()
        {
            _badge.Location = Point.Empty;
            int text = _text.GetPreferredSize(new Size(Math.Max(10, Width - TextLeft), 0)).Height;
            _text.SetBounds(TextLeft, Math.Max(0, (_badge.Height - text) / 2), Math.Max(10, Width - TextLeft), text);
        }
    }
}

/// <summary>Pastille ronde avec icône.</summary>
public sealed class IconBadge : Control
{
    private readonly string _glyph;
    private readonly Color _color;

    public IconBadge(string glyph, Color color, int size = 48)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        _glyph = glyph;
        _color = color;
        Size = new Size(Theme.S(size), Theme.S(size));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Theme.BackOf(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle rect = new(0, 0, Width - 1, Height - 1);
        using SolidBrush brush = new(Theme.Mix(Theme.BackOf(this), _color, 0.22f));
        g.FillEllipse(brush, rect);
        TextRenderer.DrawText(g, _glyph, Theme.Icon(Math.Max(10f, Height / Theme.Scale / 4.2f)), rect, _color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

/// <summary>Saisie d'un texte unique (motif d'annulation, référence…).</summary>
public sealed class PromptDialog : DialogForm
{
    private readonly InputBox _input;
    private readonly bool _required;

    public PromptDialog(string title, string? subtitle, string label, string confirmText, bool required, bool multiline, string? initial = null, ButtonKind kind = ButtonKind.Primary)
        : base(title, subtitle, 500, multiline ? 360 : 300)
    {
        _required = required;
        Primary.Text = confirmText;
        Primary.Kind = kind;
        _input = new InputBox { Multiline = multiline, Text = initial ?? string.Empty };
        if (multiline)
        {
            _input.Height = Theme.S(110);
        }

        FieldBox field = new(label, _input) { Dock = DockStyle.Top };
        Body.Controls.Add(field);
        Primary.Click += (_, _) =>
        {
            if (_required && string.IsNullOrWhiteSpace(_input.Text))
            {
                ShowError("Ce champ est obligatoire.");
                _input.Focus();
                return;
            }

            DialogResult = DialogResult.OK;
        };
        Shown += (_, _) => _input.Focus();
    }

    public string Value => _input.Text.Trim();
}

/// <summary>Raccourcis pour afficher les boîtes de dialogue stylées.</summary>
public static class AppDialogs
{
    /// <summary>Mode sans fenêtre (auto-test) : les boîtes de dialogue sont journalisées au lieu d'être affichées.</summary>
    public static bool Headless { get; set; }

    public static List<string> HeadlessMessages { get; } = new();

    private static IWin32Window? Owner(Control? scope) => scope?.FindForm() ?? Form.ActiveForm;

    public static void Error(Control? scope, string message, string? title = null)
    {
        if (Headless)
        {
            HeadlessMessages.Add("ERREUR : " + (title is null ? string.Empty : title + " — ") + message);
            return;
        }

        using MessageDialog dialog = new(title ?? "Une erreur est survenue", message, MessageKind.Error, "Fermer", null, false);
        dialog.ShowDialog(Owner(scope));
    }

    public static void Info(Control? scope, string title, string message, MessageKind kind = MessageKind.Info)
    {
        if (Headless)
        {
            HeadlessMessages.Add("INFO : " + title + " — " + message);
            return;
        }

        using MessageDialog dialog = new(title, message, kind, "OK", null, false);
        dialog.ShowDialog(Owner(scope));
    }

    public static bool Confirm(Control? scope, string title, string message, string confirmText = "Confirmer", bool danger = false)
    {
        if (Headless)
        {
            HeadlessMessages.Add("CONFIRMATION : " + title + " — " + message);
            return true;
        }

        using MessageDialog dialog = new(title, message, danger ? MessageKind.Warning : MessageKind.Question, confirmText, "Annuler", danger);
        return dialog.ShowDialog(Owner(scope)) == DialogResult.OK;
    }

    /// <summary>Demande un texte ; retourne null si l'utilisateur annule.</summary>
    public static string? Prompt(Control? scope, string title, string label, string confirmText = "Valider",
        string? subtitle = null, bool required = true, bool multiline = false, string? initial = null, bool danger = false)
    {
        if (Headless)
        {
            HeadlessMessages.Add("SAISIE : " + title);
            return initial ?? "Auto-test";
        }

        using PromptDialog dialog = new(title, subtitle, label, confirmText, required, multiline, initial,
            danger ? ButtonKind.Danger : ButtonKind.Primary);
        return dialog.ShowDialog(Owner(scope)) == DialogResult.OK ? dialog.Value : null;
    }
}
