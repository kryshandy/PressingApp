using System.Drawing.Drawing2D;
using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Desktop.Dialogs;
using Pressing.Desktop.Services;
using Pressing.Desktop.Ui;
using Pressing.Desktop.Views;

namespace Pressing.Desktop.Forms;

public enum MainExitReason
{
    Exit,
    LoggedOut,
    SessionExpired
}

/// <summary>Coque principale : barre latérale, en-tête (recherche, notifications, utilisateur) et zone de contenu.</summary>
public sealed class FrmMain : Form, IAppHost
{
    private sealed record HistoryEntry(string MenuKey, Func<BaseView> Factory);

    private readonly AppSession _session;
    private readonly Dictionary<string, NavButton> _buttons = new();
    private readonly Dictionary<string, BaseView> _kept = new();
    private readonly HashSet<BaseView> _loaded = new();
    private readonly Stack<HistoryEntry> _history = new();
    private readonly Panel _content;
    private readonly InputBox _search;
    private readonly BellButton _bell;
    private readonly ToastPanel _toast;
    private readonly System.Windows.Forms.Timer _badgeTimer = new() { Interval = 60_000 };
    private BaseView? _current;
    private Func<BaseView>? _currentFactory;
    private string _currentMenuKey = string.Empty;
    private int _navigationVersion;
    private bool _expired;

    public FrmMain(AppSession session)
    {
        _session = session;
        AutoScaleMode = AutoScaleMode.None;
        Text = "Pressing Éclat";
        Font = Theme.Body;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        KeyPreview = true;
        Rectangle area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        MinimumSize = new Size(Math.Min(Theme.S(1280), area.Width), Math.Min(Theme.S(760), area.Height));
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;

        TableLayoutPanel root = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Background };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(240)));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildSidebar(), 0, 0);

        _content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Margin = Padding.Empty };
        _search = new InputBox
        {
            IconGlyph = Icons.Search,
            PlaceholderText = "Rechercher un ticket, un client…  (F3)",
            Width = Theme.S(420),
            Anchor = AnchorStyles.Left,
            Margin = Padding.Empty
        };
        _bell = new BellButton { Anchor = AnchorStyles.Right };
        root.Controls.Add(BuildMainArea(), 1, 0);
        Controls.Add(root);

        _toast = new ToastPanel { Visible = false };
        Controls.Add(_toast);
        _toast.BringToFront();

        _search.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                await GlobalSearchAsync(_search.Text);
            }
            else if (e.KeyCode == Keys.Escape)
            {
                _search.Text = string.Empty;
                _content.Focus();
            }
        };
        _bell.Click += async (_, _) => await ShowNotificationsAsync();
        _badgeTimer.Tick += async (_, _) => await RefreshBadgesAsync();
        _session.Api.Unauthorized += OnUnauthorized;
        FormClosed += (_, _) =>
        {
            _badgeTimer.Stop();
            _badgeTimer.Dispose();
            _session.Api.Unauthorized -= OnUnauthorized;
            foreach (BaseView view in _kept.Values)
            {
                view.Dispose();
            }
        };
        Shown += async (_, _) =>
        {
            _badgeTimer.Start();
            await RefreshBadgesAsync();
        };
        Load += (_, _) => Navigate(NavRegistry.Dashboard);
    }

    public MainExitReason ExitReason { get; private set; } = MainExitReason.Exit;

    // ---- IAppHost ------------------------------------------------------------------------
    public AppSession Session => _session;

    public PressingApiClient Api => _session.Api;

    public Form Window => this;

    public void Navigate(string key, object? parameter = null)
    {
        NavItem? item = NavRegistry.Find(key);
        if (item is null)
        {
            return;
        }

        if (!item.IsAllowed(_session.Role))
        {
            Toast("Vous n'avez pas accès à cet écran.", MessageKind.Warning);
            return;
        }

        _history.Clear();
        _ = ShowAsync(key, () => CreateFromItem(item, parameter), recordCurrent: false);
    }

    public void OpenOrder(int orderId) => OpenView(NavRegistry.Orders, () => new OrderDetailView(this, orderId));

    public void OpenClient(int clientId) => OpenView(NavRegistry.Clients, () => new ClientDetailView(this, clientId));

    public void OpenView(string navKey, Func<BaseView> factory) => _ = ShowAsync(navKey, factory, recordCurrent: true);

    public void GoBack()
    {
        if (_history.Count > 0)
        {
            HistoryEntry entry = _history.Pop();
            _ = ShowAsync(entry.MenuKey, entry.Factory, recordCurrent: false);
        }
        else
        {
            Navigate(NavRegistry.Dashboard);
        }
    }

    public void StartNewOrder(ClientDto? client = null) => Navigate(NavRegistry.NewOrder, client);

    public async Task RefreshBadgesAsync()
    {
        try
        {
            int count = await _session.Api.GetUnreadNotificationCountAsync();
            if (!IsDisposed)
            {
                _bell.Count = count;
            }
        }
        catch (ApiException)
        {
            // Compteur facultatif : une panne passagère ne doit pas déranger l'utilisateur.
        }
    }

    public void Toast(string message, MessageKind kind = MessageKind.Success) => _toast.Display(this, message, kind);

    // ---- Construction --------------------------------------------------------------------
    private Control BuildSidebar()
    {
        Panel sidebar = new() { Dock = DockStyle.Fill, BackColor = Theme.Sidebar, Margin = Padding.Empty };

        BrandHeader brand = new() { Dock = DockStyle.Top, Height = Theme.S(76) };
        Label footer = new()
        {
            Dock = DockStyle.Bottom,
            Height = Theme.S(44),
            Text = "F2 dépôt · F3 recherche · F5 actualiser" + Environment.NewLine + "v" + typeof(FrmMain).Assembly.GetName().Version?.ToString(3),
            ForeColor = Theme.TextFaint,
            Font = Theme.F(7.5f),
            AutoEllipsis = true,
            Padding = new Padding(Theme.S(14), 0, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft
        };

        TableLayoutPanel menu = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            BackColor = Theme.Sidebar,
            Padding = new Padding(Theme.S(12), Theme.S(4), Theme.S(12), Theme.S(4))
        };
        menu.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Panel scroller = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Sidebar };
        scroller.Controls.Add(menu);

        string? section = null;
        int row = 0;
        foreach (NavItem item in NavRegistry.Items.Where(i => i.Key != NavRegistry.Account && i.IsAllowed(_session.Role)))
        {
            if (section != item.Section)
            {
                section = item.Section;
                Label caption = new()
                {
                    Text = item.Section.ToUpperInvariant(),
                    Dock = DockStyle.Fill,
                    Height = Theme.S(row == 0 ? 6 : 24),
                    Font = Theme.F(8f, FontStyle.Bold),
                    ForeColor = Theme.TextFaint,
                    TextAlign = ContentAlignment.BottomLeft,
                    Padding = new Padding(Theme.S(12), 0, 0, Theme.S(4)),
                    Visible = row != 0,
                    Margin = Padding.Empty
                };
                menu.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                menu.Controls.Add(caption, 0, row++);
            }

            NavButton button = new() { Text = item.Title, Glyph = item.Glyph, Key = item.Key, Dock = DockStyle.Fill };
            string key = item.Key;
            button.Click += (_, _) => Navigate(key);
            _buttons[key] = button;
            menu.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            menu.Controls.Add(button, 0, row++);
        }

        sidebar.Controls.Add(scroller);
        sidebar.Controls.Add(brand);
        sidebar.Controls.Add(footer);
        scroller.BringToFront();
        return sidebar;
    }

    private Control BuildMainArea()
    {
        TableLayoutPanel area = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Background, Margin = Padding.Empty };
        area.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(72)));
        area.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        UserChip chip = new(_session.Staff);
        chip.Click += (_, _) => Navigate(NavRegistry.Account);
        FlatButton logout = new()
        {
            Kind = ButtonKind.Secondary, IconGlyph = Icons.Logout, Text = string.Empty,
            AutoSize = false, Size = new Size(Theme.S(40), Theme.S(40)),
            AccessibleName = "Se déconnecter", AccessibleDescription = "Fermer la session du personnel"
        };
        logout.Click += (_, _) => Logout();
        new ToolTip().SetToolTip(logout, "Se déconnecter");
        new ToolTip().SetToolTip(_bell, "Notifications");
        ShellHeader header = new(_search, _bell, chip, logout) { Dock = DockStyle.Fill, Margin = Padding.Empty };

        area.Controls.Add(header, 0, 0);
        area.Controls.Add(_content, 0, 1);
        return area;
    }

    // ---- Navigation ----------------------------------------------------------------------
    private BaseView CreateFromItem(NavItem item, object? parameter)
    {
        if (_kept.TryGetValue(item.Key, out BaseView? existing) && !existing.IsDisposed)
        {
            if (parameter is not null)
            {
                existing.ApplyParameter(parameter);
            }

            return existing;
        }

        BaseView view = item.Create(this);
        view.ApplyParameter(parameter);
        if (view.KeepAlive)
        {
            _kept[item.Key] = view;
        }

        return view;
    }

    private async Task ShowAsync(string menuKey, Func<BaseView> factory, bool recordCurrent)
    {
        int version = ++_navigationVersion;
        if (recordCurrent && _current is not null && _currentFactory is not null)
        {
            _history.Push(new HistoryEntry(_currentMenuKey, _currentFactory));
            if (_history.Count > 25)
            {
                HistoryEntry[] trimmed = _history.Take(20).Reverse().ToArray();
                _history.Clear();
                foreach (HistoryEntry entry in trimmed)
                {
                    _history.Push(entry);
                }
            }
        }

        BaseView view;
        try
        {
            view = factory();
        }
        catch (Exception ex)
        {
            AppDialogs.Error(this, "Impossible d'ouvrir cet écran : " + ex.Message);
            return;
        }

        BaseView? old = _current;
        if (!ReferenceEquals(old, view))
        {
            _content.SuspendLayout();
            if (old is not null)
            {
                _content.Controls.Remove(old);
                if (!old.KeepAlive)
                {
                    _loaded.Remove(old);
                    old.Dispose();
                }
            }

            view.Dock = DockStyle.Fill;
            _content.Controls.Add(view);
            _content.ResumeLayout(true);
        }

        _current = view;
        _currentFactory = factory;
        _currentMenuKey = menuKey;
        foreach ((string key, NavButton button) in _buttons)
        {
            button.Active = key == menuKey;
        }

        Text = view.Title + " — Pressing Éclat";
        view.Focus();

        bool first = _loaded.Add(view);
        await UiRun.RunAsync(view, async () =>
        {
            if (first)
            {
                await view.LoadAsync();
            }
            else
            {
                await view.ReactivateAsync();
            }
        }, "Chargement de « " + view.Title + " »");
        if (version == _navigationVersion && !view.IsDisposed)
        {
            view.Focus();
        }
    }

    // ---- Recherche globale ---------------------------------------------------------------
    private async Task GlobalSearchAsync(string raw)
    {
        string text = raw.Trim();
        if (text.Length == 0)
        {
            return;
        }

        await UiRun.RunAsync(this, async () =>
        {
            string? ticket = TicketParser.Normalize(text);
            if (ticket is not null)
            {
                OrderDto? order = await Api.GetOrderByTicketAsync(ticket);
                if (order is not null)
                {
                    _search.Text = string.Empty;
                    OpenOrder(order.Id);
                    return;
                }
            }

            List<ClientDto> clients = await Api.LookupClientsAsync(text, 20) ?? new List<ClientDto>();
            if (clients.Count == 1)
            {
                _search.Text = string.Empty;
                OpenClient(clients[0].Id);
                return;
            }

            if (clients.Count > 1)
            {
                _search.Text = string.Empty;
                Navigate(NavRegistry.Clients, text);
                return;
            }

            PagedResult<OrderSummaryDto>? orders = await Api.SearchOrdersAsync(search: text, pageSize: 2);
            if (orders is { Total: 1 })
            {
                _search.Text = string.Empty;
                OpenOrder(orders.Items[0].Id);
            }
            else if (orders is { Total: > 1 })
            {
                _search.Text = string.Empty;
                Navigate(NavRegistry.Orders, new OrdersFilter { Search = text });
            }
            else
            {
                Toast($"Aucun résultat pour « {text} ».", MessageKind.Warning);
            }
        });
    }

    // ---- Notifications -------------------------------------------------------------------
    private async Task ShowNotificationsAsync()
    {
        List<NotificationDto>? items = null;
        bool ok = await UiRun.RunAsync(this, async () => items = await Api.GetNotificationsAsync(20));
        if (!ok || items is null)
        {
            return;
        }

        using NotificationsDialog dialog = new(Api, items);
        dialog.ShowDialog(this);
        await RefreshBadgesAsync();
        if (dialog.OpenOrderId is int id)
        {
            OpenOrder(id);
        }
    }

    // ---- Session -------------------------------------------------------------------------
    private void OnUnauthorized(object? sender, EventArgs e)
    {
        if (_expired || IsDisposed)
        {
            return;
        }

        _expired = true;
        BeginInvoke(() =>
        {
            ExitReason = MainExitReason.SessionExpired;
            Close();
        });
    }

    private void Logout()
    {
        if (!AppDialogs.Confirm(this, "Se déconnecter", "Voulez-vous fermer votre session ?", "Se déconnecter"))
        {
            return;
        }

        _session.Api.Logout();
        ExitReason = MainExitReason.LoggedOut;
        Close();
    }

    // ---- Clavier -------------------------------------------------------------------------
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_current?.HandleShortcut(keyData) == true)
        {
            return true;
        }

        switch (keyData)
        {
            case Keys.F2:
                Navigate(NavRegistry.NewOrder);
                return true;
            case Keys.F3 or (Keys.Control | Keys.K):
                _search.Focus();
                return true;
            case Keys.F5:
                _ = RefreshCurrentAsync();
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private async Task RefreshCurrentAsync()
    {
        BaseView? view = _current;
        if (view is null || view.IsDisposed)
        {
            return;
        }

        await UiRun.RunAsync(view, () => view.RefreshAsync(), "Actualisation");
        await RefreshBadgesAsync();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.DarkTitleBar(this);
    }

    /// <summary>Auto-test : autorise une fenêtre plus grande que l'écran (captures à 1600×900 sur un petit écran).</summary>
    internal static bool AllowOversize { get; set; }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == 0x0024 && AllowOversize && m.LParam != IntPtr.Zero)
        {
            // MINMAXINFO.ptMaxTrackSize (x, y) aux octets 32 et 36.
            System.Runtime.InteropServices.Marshal.WriteInt32(m.LParam, 32, 10000);
            System.Runtime.InteropServices.Marshal.WriteInt32(m.LParam, 36, 10000);
        }
    }
}

/// <summary>
/// En-tête global : recherche à gauche et actions (cloche, utilisateur, déconnexion) à droite, posées exactement
/// sur la gouttière de page (32 px) pour coïncider avec les bords du titre et des cartes ; filet en bas.
/// </summary>
internal sealed class ShellHeader : LayoutControl
{
    private readonly Control _search;
    private readonly Control _bell;
    private readonly Control _chip;
    private readonly Control _logout;

    public ShellHeader(Control search, Control bell, Control chip, Control logout)
    {
        BackColor = Theme.Background;
        _search = search;
        _bell = bell;
        _chip = chip;
        _logout = logout;
        foreach (Control c in new[] { search, bell, chip, logout })
        {
            LayoutUtil.Prepare(c);
            Controls.Add(c);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using Pen pen = new(Theme.Border);
        e.Graphics.DrawLine(pen, Theme.PageGutter, Height - 1, Width - Theme.PageGutter, Height - 1);
    }

    protected override void Arrange()
    {
        int g = Theme.PageGutter;
        int mid = Height / 2;
        int right = Width - g;
        _logout.SetBounds(right - _logout.Width, mid - _logout.Height / 2, _logout.Width, _logout.Height);
        right = _logout.Left - Theme.S(8);
        _chip.SetBounds(right - _chip.Width, mid - _chip.Height / 2, _chip.Width, _chip.Height);
        right = _chip.Left - Theme.S(8);
        _bell.SetBounds(right - _bell.Width, mid - _bell.Height / 2, _bell.Width, _bell.Height);
        int searchWidth = Math.Min(Theme.S(420), Math.Max(Theme.S(200), _bell.Left - g - Theme.S(24)));
        _search.SetBounds(g, mid - _search.Height / 2, searchWidth, _search.Height);
    }
}

/// <summary>En-tête de la barre latérale : logo + nom de la boutique.</summary>
internal sealed class BrandHeader : Control
{
    public BrandHeader()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Theme.Sidebar);
        int size = Theme.S(40);
        BrandMark.Draw(g, new Rectangle(Theme.S(20), (Height - size) / 2, size, size));
        TextRenderer.DrawText(g, "Pressing Éclat", Theme.F(13f, FontStyle.Bold),
            new Rectangle(Theme.S(70), Theme.S(18), Width - Theme.S(76), Theme.S(26)), Theme.Text,
            TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, "Espace du personnel", Theme.F(9f),
            new Rectangle(Theme.S(70), Theme.S(42), Width - Theme.S(76), Theme.S(20)), Theme.TextMuted,
            TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Cloche des notifications avec pastille de compteur.</summary>
internal sealed class BellButton : Control
{
    private int _count;
    private bool _hover;

    public BellButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        TabStop = true;
        Size = new Size(Theme.S(40), Theme.S(40));
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = "Notifications";
        AccessibleDescription = "Afficher les notifications";
    }

    public int Count
    {
        get => _count;
        set
        {
            _count = value;
            AccessibleDescription = _count > 0
                ? $"{_count} notification(s) en attente"
                : "Aucune notification en attente";
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Theme.BackOf(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle rect = new(0, 0, Width - 1, Height - 1);
        using (GraphicsPath path = Theme.RoundedRect(rect, Theme.S(10)))
        {
            using SolidBrush brush = new(_hover ? Theme.SurfaceHover : Theme.SurfaceAlt);
            g.FillPath(brush, path);
            using Pen pen = new(Focused ? Theme.Accent : Theme.Border);
            g.DrawPath(pen, path);
        }

        TextRenderer.DrawText(g, Icons.Bell, Theme.Icon(12f), rect, _count > 0 ? Theme.Accent : Theme.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        if (_count > 0)
        {
            string text = _count > 99 ? "99+" : _count.ToString();
            int w = Math.Max(Theme.S(18), TextRenderer.MeasureText(text, Theme.F(8f, FontStyle.Bold)).Width + Theme.S(4));
            Rectangle badge = new(Width - w - Theme.S(2), Theme.S(1), w, Theme.S(18));
            using GraphicsPath path = Theme.RoundedRect(badge, Theme.S(9));
            using SolidBrush brush = new(Theme.Red);
            g.FillPath(brush, path);
            TextRenderer.DrawText(g, text, Theme.F(8f, FontStyle.Bold), badge, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
}

/// <summary>Pastille de l'utilisateur connecté (initiales, nom, rôle).</summary>
internal sealed class UserChip : Control
{
    private readonly StaffUserDto _staff;
    private bool _hover;

    public UserChip(StaffUserDto staff)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.StandardClick, true);
        _staff = staff;
        Cursor = Cursors.Hand;
        Size = new Size(Theme.S(210), Theme.S(44));
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = $"Mon compte : {_staff.FullName} ({_staff.Role.Label()})";
        AccessibleDescription = "Ouvrir mon compte";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Theme.BackOf(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (_hover)
        {
            using GraphicsPath path = Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), Theme.S(10));
            using SolidBrush brush = new(Theme.Surface);
            g.FillPath(brush, path);
        }

        int d = Theme.S(36);
        Rectangle avatar = new(Theme.S(6), (Height - d) / 2, d, d);
        using (SolidBrush brush = new(Theme.Mix(Theme.Background, Theme.Accent, 0.28f)))
        {
            g.FillEllipse(brush, avatar);
        }

        string initials = string.Concat(_staff.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpperInvariant(p[0])));
        TextRenderer.DrawText(g, initials, Theme.F(10f, FontStyle.Bold), avatar, Theme.Accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        TextRenderer.DrawText(g, _staff.FullName, Theme.F(10f, FontStyle.Bold),
            new Rectangle(avatar.Right + Theme.S(10), Theme.S(5), Width - avatar.Right - Theme.S(16), Theme.S(20)), Theme.Text, flags);
        TextRenderer.DrawText(g, _staff.Role.Label(), Theme.F(9f),
            new Rectangle(avatar.Right + Theme.S(10), Theme.S(24), Width - avatar.Right - Theme.S(16), Theme.S(18)), Theme.Accent, flags);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
}

/// <summary>Message éphémère en bas à droite.</summary>
internal sealed class ToastPanel : Control
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 3800 };
    private string _message = string.Empty;
    private Color _color = Theme.Green;

    public ToastPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        _timer.Tick += (_, _) => { _timer.Stop(); Visible = false; };
        Cursor = Cursors.Hand;
        Click += (_, _) => { _timer.Stop(); Visible = false; };
    }

    public void Display(Form owner, string message, MessageKind kind)
    {
        _message = message;
        _color = kind switch
        {
            MessageKind.Error => Theme.Red,
            MessageKind.Warning => Theme.Amber,
            MessageKind.Info => Theme.Blue,
            _ => Theme.Green
        };
        int textWidth = Math.Min(Theme.S(420), Math.Max(Theme.S(200), owner.ClientSize.Width / 3));
        Size text = TextRenderer.MeasureText(message, Theme.F(10f), new Size(textWidth, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        Size = new Size(textWidth + Theme.S(64), Math.Max(Theme.S(52), text.Height + Theme.S(28)));
        Location = new Point(owner.ClientSize.Width - Width - Theme.S(28), owner.ClientSize.Height - Height - Theme.S(28));
        Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        Visible = true;
        BringToFront();
        Invalidate();
        _timer.Stop();
        _timer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Theme.BackOf(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle rect = new(0, 0, Width - 1, Height - 1);
        using GraphicsPath path = Theme.RoundedRect(rect, Theme.S(10));
        using SolidBrush back = new(Theme.Sidebar);
        g.FillPath(back, path);
        using Pen pen = new(Theme.Mix(Theme.Border, _color, 0.6f), 1.2f);
        g.DrawPath(pen, path);
        using SolidBrush strip = new(_color);
        g.FillRectangle(strip, Theme.S(1), Theme.S(12), Theme.S(4), Height - Theme.S(24));
        TextRenderer.DrawText(g, _message, Theme.F(10f), new Rectangle(Theme.S(24), 0, Width - Theme.S(40), Height), Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }
}
