using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Pressing.Client;

namespace Pressing.Desktop.Ui;

/// <summary>Appels natifs : barre de titre sombre (Windows 10/11).</summary>
public static class Native
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void DarkTitleBar(Form form)
    {
        try
        {
            int on = 1;
            DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int));
            int caption = ColorTranslator.ToWin32(Theme.Sidebar);
            DwmSetWindowAttribute(form.Handle, 35, ref caption, sizeof(int));
            int text = ColorTranslator.ToWin32(Theme.Text);
            DwmSetWindowAttribute(form.Handle, 36, ref text, sizeof(int));
        }
        catch
        {
            // Windows plus ancien : la barre de titre reste standard.
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

/// <summary>Fenêtre modale sombre : en-tête, corps, pied avec boutons, message d'erreur inline.</summary>
public class DialogForm : Form
{
    private readonly Label _title;
    private readonly Label _subtitle;
    private readonly Label _error;
    private readonly FlowLayoutPanel _buttons;

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
        Rectangle area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        ClientSize = new Size(Math.Min(Theme.S(width), area.Width - Theme.S(40)), Math.Min(Theme.S(height), area.Height - Theme.S(110)));

        Panel header = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(Theme.S(28), Theme.S(22), Theme.S(28), Theme.S(8)),
            BackColor = Theme.Background
        };
        _subtitle = new Label
        {
            Text = subtitle ?? string.Empty,
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(Theme.S(width - 56), 0),
            Font = Theme.F(10f),
            ForeColor = Theme.TextMuted,
            Visible = !string.IsNullOrEmpty(subtitle),
            Padding = new Padding(0, Theme.S(4), 0, 0)
        };
        _title = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            AutoSize = true,
            Font = Theme.F(16f, FontStyle.Bold),
            ForeColor = Theme.Text
        };
        header.Controls.Add(_subtitle);
        header.Controls.Add(_title);

        Body = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(Theme.S(28), Theme.S(12), Theme.S(28), Theme.S(12)),
            BackColor = Theme.Background
        };

        TableLayoutPanel footer = new()
        {
            Dock = DockStyle.Bottom,
            Height = Theme.S(76),
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(Theme.S(28), Theme.S(16), Theme.S(28), Theme.S(16)),
            BackColor = Theme.Sidebar
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _error = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Theme.Red,
            Font = Theme.F(9.5f),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
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

        Controls.Add(Body);
        Controls.Add(footer);
        Controls.Add(header);
        header.BringToFront();
        Body.BringToFront();

        Secondary = new FlatButton { Text = "Annuler", Kind = ButtonKind.Secondary, DialogResult = DialogResult.Cancel, Margin = new Padding(0, 0, Theme.S(10), 0) };
        Primary = new FlatButton { Text = "Valider", Kind = ButtonKind.Primary, Margin = Padding.Empty };
        _buttons.Controls.Add(Primary);
        _buttons.Controls.Add(Secondary);
        CancelButton = Secondary;
        AcceptButton = Primary;
    }

    public Panel Body { get; }

    public FlatButton Primary { get; }

    public FlatButton Secondary { get; }

    /// <summary>Ajoute un bouton supplémentaire à gauche des boutons standards.</summary>
    protected FlatButton AddExtraButton(string text, ButtonKind kind = ButtonKind.Secondary, string icon = "")
    {
        FlatButton button = new() { Text = text, Kind = kind, IconGlyph = icon, Margin = new Padding(0, 0, Theme.S(10), 0) };
        _buttons.Controls.Add(button);
        return button;
    }

    public void ShowError(string? message) => _error.Text = message ?? string.Empty;

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

        TableLayoutPanel layout = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Background };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(64)));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        IconBadge badge = new(glyph, accent) { Anchor = AnchorStyles.Top | AnchorStyles.Left, Margin = new Padding(0, Theme.S(4), 0, 0) };
        Label text = new()
        {
            Text = message,
            Dock = DockStyle.Fill,
            Font = Theme.F(10.5f),
            ForeColor = Theme.Text,
            Padding = new Padding(0, Theme.S(6), 0, 0)
        };
        layout.Controls.Add(badge, 0, 0);
        layout.Controls.Add(text, 1, 0);
        Body.Controls.Add(layout);

        // Hauteur adaptée au message.
        int textWidth = Theme.S(480) - Theme.S(56) - Theme.S(64);
        Size measured = TextRenderer.MeasureText(message, Theme.F(10.5f), new Size(textWidth, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        int extra = Math.Max(0, measured.Height - Theme.S(64));
        ClientSize = new Size(ClientSize.Width, Math.Min(Theme.S(520), ClientSize.Height + extra));
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
