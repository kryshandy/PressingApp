using System.Drawing.Drawing2D;
using Pressing.Client;
using Pressing.Desktop.Services;
using Pressing.Desktop.Ui;

namespace Pressing.Desktop.Forms;

/// <summary>Fenêtre de connexion du personnel.</summary>
public sealed class FrmLogin : Form
{
    private readonly PressingApiClient _api;
    private readonly string _apiUrl;
    private readonly InputBox _username;
    private readonly InputBox _password;
    private readonly FlatButton _signIn;
    private readonly FlatButton _retry;
    private readonly Label _error;
    private readonly Label _status;
    private readonly StatusDot _dot;

    public FrmLogin(PressingApiClient api, string apiUrl, string? notice = null)
    {
        _api = api;
        _apiUrl = apiUrl;
        AutoScaleMode = AutoScaleMode.None;
        Text = "Pressing Éclat — Connexion";
        Font = Theme.Body;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Rectangle area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        ClientSize = new Size(Math.Min(Theme.S(1000), (int)(area.Width * 0.94)), Math.Min(Theme.S(640), (int)(area.Height * 0.92)));
        KeyPreview = true;

        TableLayoutPanel root = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Background };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        root.Controls.Add(new BrandPanel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 0);

        // ---- Formulaire ----
        Panel right = new() { Dock = DockStyle.Fill, BackColor = Theme.Background, Margin = Padding.Empty };
        TableLayoutPanel form = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Width = Theme.S(380),
            Anchor = AnchorStyles.None,
            BackColor = Theme.Background
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(380)));

        Label title = UiKit.Label("Connexion", Theme.F(24f, FontStyle.Bold));
        Label subtitle = UiKit.Muted("Espace réservé au personnel du pressing.", 10.5f);
        subtitle.Margin = new Padding(0, Theme.S(4), 0, Theme.S(28));

        _username = new InputBox { PlaceholderText = "ex. admin", IconGlyph = Icons.Account, Text = LastUser.Read() };
        _password = new InputBox { PlaceholderText = "Votre mot de passe", IconGlyph = Icons.Key, PasswordChar = '•' };
        FieldBox userField = new("Identifiant", _username) { Margin = new Padding(0, 0, 0, Theme.S(14)) };
        FieldBox passField = new("Mot de passe", _password) { Margin = new Padding(0, 0, 0, Theme.S(6)) };

        _error = new Label
        {
            AutoSize = false,
            Height = Theme.S(44),
            Dock = DockStyle.Fill,
            ForeColor = Theme.Red,
            Font = Theme.F(9.5f),
            Text = notice ?? string.Empty,
            Margin = new Padding(0, Theme.S(4), 0, Theme.S(4)),
            TextAlign = ContentAlignment.MiddleLeft
        };
        if (notice is not null)
        {
            _error.ForeColor = Theme.Amber;
        }

        _signIn = new FlatButton { Text = "Se connecter", Kind = ButtonKind.Primary, AutoSize = false, Dock = DockStyle.Fill, Height = Theme.S(46), Margin = Padding.Empty, Font = Theme.F(11f, FontStyle.Bold) };

        // État du serveur
        TableLayoutPanel statusRow = new()
        {
            ColumnCount = 3,
            RowCount = 1,
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(0, Theme.S(26), 0, 0)
        };
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _dot = new StatusDot { Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, Theme.S(10), 0) };
        _status = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(Theme.S(250), 0),
            Font = Theme.F(9.5f),
            ForeColor = Theme.TextMuted,
            Text = "Vérification du serveur…",
            Anchor = AnchorStyles.Left
        };
        _retry = new FlatButton { Text = "Réessayer", Kind = ButtonKind.Secondary, Height = Theme.S(34), Visible = false, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
        statusRow.Controls.Add(_dot, 0, 0);
        statusRow.Controls.Add(_status, 1, 0);
        statusRow.Controls.Add(_retry, 2, 0);

        form.Controls.Add(title, 0, 0);
        form.Controls.Add(subtitle, 0, 1);
        form.Controls.Add(userField, 0, 2);
        form.Controls.Add(passField, 0, 3);
        form.Controls.Add(_error, 0, 4);
        form.Controls.Add(_signIn, 0, 5);
        form.Controls.Add(statusRow, 0, 6);
        right.Controls.Add(form);
        right.Resize += (_, _) => CenterForm(right, form);
        Shown += (_, _) => CenterForm(right, form);
        root.Controls.Add(right, 1, 0);
        Controls.Add(root);

        AcceptButton = _signIn;
        _signIn.Click += async (_, _) => await SignInAsync();
        _retry.Click += async (_, _) => await CheckServerAsync();
        Shown += async (_, _) =>
        {
            if (_username.Text.Length > 0)
            {
                _password.Focus();
            }
            else
            {
                _username.Focus();
            }

            await CheckServerAsync();
        };
    }

    /// <summary>Session ouverte (null si la fenêtre est fermée sans connexion).</summary>
    public AppSession? Session { get; private set; }

    private static void CenterForm(Control container, Control form)
    {
        form.Location = new Point(Math.Max(0, (container.Width - form.Width) / 2), Math.Max(0, (container.Height - form.Height) / 2));
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.DarkTitleBar(this);
    }

    private async Task CheckServerAsync()
    {
        _retry.Visible = false;
        _status.Text = "Vérification du serveur…";
        _status.ForeColor = Theme.TextMuted;
        _dot.DotColor = Theme.Amber;
        try
        {
            var health = await _api.GetHealthAsync();
            if (IsDisposed)
            {
                return;
            }

            _dot.DotColor = Theme.Green;
            _status.ForeColor = Theme.TextMuted;
            _status.Text = $"Serveur en ligne — {health?.ShopName ?? "Pressing Éclat"} · v{health?.Version}";
        }
        catch (ApiException ex)
        {
            if (IsDisposed)
            {
                return;
            }

            _dot.DotColor = Theme.Red;
            _status.ForeColor = Theme.Red;
            _status.Text = ex.IsUnauthorized ? "Serveur en ligne." : ex.Message;
            _retry.Visible = true;
        }
    }

    private async Task SignInAsync()
    {
        string user = _username.Text.Trim();
        string password = _password.Text;
        if (user.Length == 0 || password.Length == 0)
        {
            ShowError(user.Length == 0 ? "Saisissez votre identifiant." : "Saisissez votre mot de passe.");
            (user.Length == 0 ? _username : _password).Focus();
            return;
        }

        ShowError(string.Empty);
        UiRun.SetBusy(this, true);
        _signIn.Text = "Connexion…";
        try
        {
            var response = await _api.LoginStaffAsync(user, password);
            if (response?.Staff is null)
            {
                _api.Logout();
                ShowError(response?.Client is not null
                    ? "Ce compte est un compte client : utilisez le site web."
                    : "Réponse inattendue du serveur.");
                return;
            }

            LastUser.Write(user);
            Session = new AppSession(_api, response.Staff);
            Close();
        }
        catch (ApiException ex)
        {
            ShowError(ex.Message);
            _password.SelectAll();
            _password.Focus();
            if (ex.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            {
                await CheckServerAsync();
            }
        }
        finally
        {
            if (!IsDisposed)
            {
                _signIn.Text = "Se connecter";
                UiRun.SetBusy(this, false);
            }
        }
    }

    private void ShowError(string message)
    {
        _error.ForeColor = Theme.Red;
        _error.Text = message;
    }
}

/// <summary>Mémorise le dernier identifiant saisi (jamais le mot de passe).</summary>
internal static class LastUser
{
    private static string PathFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PressingEclat", "last-user.txt");

    public static string Read()
    {
        try
        {
            return File.Exists(PathFile) ? File.ReadAllText(PathFile).Trim() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static void Write(string user)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathFile)!);
            File.WriteAllText(PathFile, user);
        }
        catch
        {
            // Préférence facultative.
        }
    }
}

/// <summary>Point de couleur (état du serveur).</summary>
public sealed class StatusDot : Control
{
    private Color _color = Theme.Slate;

    public StatusDot()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Size = new Size(Theme.S(12), Theme.S(12));
    }

    public Color DotColor
    {
        get => _color;
        set { _color = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.BackOf(this));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using SolidBrush halo = new(Theme.Alpha(_color, 60));
        e.Graphics.FillEllipse(halo, 0, 0, Width - 1, Height - 1);
        using SolidBrush core = new(_color);
        int inset = Theme.S(3);
        e.Graphics.FillEllipse(core, inset, inset, Width - 2 * inset - 1, Height - 2 * inset - 1);
    }
}

/// <summary>Panneau de marque (dégradé, nom, slogan, avantages).</summary>
public sealed class BrandPanel : Control
{
    public BrandPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (LinearGradientBrush back = new(ClientRectangle, Theme.Sidebar, Theme.Mix(Theme.Sidebar, Theme.Accent, 0.14f), 55f))
        {
            g.FillRectangle(back, ClientRectangle);
        }

        // Cercles décoratifs
        using (SolidBrush ring = new(Theme.Alpha(Theme.Accent, 18)))
        {
            g.FillEllipse(ring, -Width / 3, Height - Height / 3, Width, Width);
            g.FillEllipse(ring, Width - Width / 3, -Width / 4, Width / 2 + Theme.S(60), Width / 2 + Theme.S(60));
        }

        int pad = Theme.S(48);
        int top = Math.Max(Theme.S(40), Height / 2 - Theme.S(170));
        BrandMark.Draw(g, new Rectangle(pad, top, Theme.S(64), Theme.S(64)));
        TextRenderer.DrawText(g, "Pressing Éclat", Theme.F(30f, FontStyle.Bold), new Rectangle(pad, top + Theme.S(88), Width - 2 * pad, Theme.S(54)),
            Theme.Text, TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, "Le soin du linge, l'exigence du service.", Theme.F(11.5f),
            new Rectangle(pad, top + Theme.S(146), Width - 2 * pad, Theme.S(48)), Theme.Accent,
            TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak);

        string[] bullets =
        {
            "Dépôts et retraits en quelques secondes",
            "Suivi des articles, paiements et impayés",
            "Reçus PDF et notifications clients"
        };
        int y = top + Theme.S(222);
        foreach (string bullet in bullets)
        {
            TextRenderer.DrawText(g, Icons.Check, Theme.Icon(10f), new Rectangle(pad, y, Theme.S(24), Theme.S(26)), Theme.Accent,
                TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, bullet, Theme.F(10.5f), new Rectangle(pad + Theme.S(32), y, Width - 2 * pad - Theme.S(32), Theme.S(26)),
                Theme.TextMuted, TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            y += Theme.S(36);
        }

        TextRenderer.DrawText(g, "Application du personnel · v" + typeof(BrandPanel).Assembly.GetName().Version?.ToString(3),
            Theme.F(9f), new Rectangle(pad, Height - Theme.S(48), Width - 2 * pad, Theme.S(24)), Theme.TextFaint,
            TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Logo : carré arrondi laiton avec la lettre É.</summary>
public static class BrandMark
{
    public static void Draw(Graphics g, Rectangle rect)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using GraphicsPath path = Theme.RoundedRect(rect, rect.Width / 4);
        using LinearGradientBrush brush = new(rect, Theme.AccentHover, Theme.AccentPressed, 45f);
        g.FillPath(brush, path);
        using Font font = new("Georgia", rect.Height * 0.42f, FontStyle.Bold, GraphicsUnit.Pixel);
        TextRenderer.DrawText(g, "É", font, rect, Theme.OnAccent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}
