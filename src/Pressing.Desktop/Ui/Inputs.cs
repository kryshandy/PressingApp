using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace Pressing.Desktop.Ui;

/// <summary>Champ de saisie arrondi (TextBox sans bordure dans un cadre dessiné) avec icône et suffixe facultatifs.</summary>
public class InputBox : Control
{
    private readonly CueTextBox _box;
    private bool _focused;
    private string _icon = string.Empty;
    private string _suffix = string.Empty;
    private bool _multiline;

    public InputBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.ContainerControl, true);
        Height = Theme.S(40);
        BackColor = Theme.SurfaceAlt;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        _box = new CueTextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.SurfaceAlt,
            ForeColor = Theme.Text,
            Font = Theme.Body,
            TabStop = true,
            AccessibleRole = AccessibleRole.Text
        };
        _box.GotFocus += (_, _) => { _focused = true; Invalidate(); if (SelectAllOnFocus) _box.SelectAll(); };
        _box.LostFocus += (_, _) => { _focused = false; Invalidate(); };
        _box.TextChanged += (_, e) => OnTextChanged(e);
        _box.KeyDown += (_, e) => OnKeyDown(e);
        _box.KeyPress += (_, e) => OnKeyPress(e);
        Controls.Add(_box);
        LayoutInner();
    }

    [Browsable(true), DefaultValue("")]
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => _box.Text;
        set => _box.Text = value ?? string.Empty;
    }

    /// <summary>Texte d'aide affiché (dessiné au thème) quand le champ est vide ; sert aussi de nom accessible.</summary>
    public string PlaceholderText
    {
        get => _box.Cue;
        set
        {
            _box.Cue = value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(_box.AccessibleName)) _box.AccessibleName = _box.Cue;
        }
    }

    public bool ReadOnly
    {
        get => _box.ReadOnly;
        set { _box.ReadOnly = value; Invalidate(); }
    }

    public char PasswordChar
    {
        get => _box.PasswordChar;
        set => _box.PasswordChar = value;
    }

    public int MaxLength
    {
        get => _box.MaxLength;
        set => _box.MaxLength = value;
    }

    public HorizontalAlignment TextAlign
    {
        get => _box.TextAlign;
        set => _box.TextAlign = value;
    }

    public bool SelectAllOnFocus { get; set; }

    /// <summary>Force la saisie en majuscules (codes promo, tickets).</summary>
    public bool CharacterCasingUpper
    {
        get => _box.CharacterCasing == CharacterCasing.Upper;
        set => _box.CharacterCasing = value ? CharacterCasing.Upper : CharacterCasing.Normal;
    }

    public bool Multiline
    {
        get => _multiline;
        set
        {
            _multiline = value;
            _box.Multiline = value;
            _box.AcceptsReturn = value;
            _box.ScrollBars = ScrollBars.None;
            if (value && Height < Theme.S(80))
            {
                Height = Theme.S(96);
            }

            LayoutInner();
        }
    }

    /// <summary>Glyphe affiché à gauche (voir <see cref="Icons"/>).</summary>
    public string IconGlyph
    {
        get => _icon;
        set { _icon = value ?? string.Empty; LayoutInner(); Invalidate(); }
    }

    /// <summary>Texte fixe à droite (ex. « FCFA »).</summary>
    public string Suffix
    {
        get => _suffix;
        set { _suffix = value ?? string.Empty; LayoutInner(); Invalidate(); }
    }

    public TextBox InnerTextBox => _box;

    public new void Focus() => _box.Focus();

    public void SelectAll() => _box.SelectAll();

    /// <summary>Montant saisi (0 si vide ou invalide).</summary>
    public decimal AmountValue
    {
        get => Theme.TryParseAmount(Text, out decimal v) ? v : 0m;
        set => Text = Theme.AmountText(value);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        _box.Enabled = Enabled;
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutInner();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        _box.Focus();
    }

    private void LayoutInner()
    {
        if (_box is null)
        {
            return;
        }

        int left = Theme.S(12) + (_icon.Length > 0 ? Theme.S(26) : 0);
        int right = Theme.S(12);
        if (_suffix.Length > 0)
        {
            right += TextRenderer.MeasureText(_suffix, Theme.F(9.5f), new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding).Width + Theme.S(6);
        }

        if (_multiline)
        {
            _box.SetBounds(left, Theme.S(10), Math.Max(10, Width - left - right), Math.Max(10, Height - Theme.S(20)));
        }
        else
        {
            int h = _box.PreferredHeight;
            _box.SetBounds(left, Math.Max(0, (Height - h) / 2), Math.Max(10, Width - left - right), h);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Theme.BackOf(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle rect = new(0, 0, Width - 1, Height - 1);
        Color fill = Enabled ? Theme.SurfaceAlt : Theme.Mix(Theme.SurfaceAlt, Theme.Border, 0.2f);
        _box.BackColor = fill;
        _box.ForeColor = Enabled ? Theme.Text : Theme.TextFaint;
        using GraphicsPath path = Theme.RoundedRect(rect, Theme.S(8));
        using SolidBrush brush = new(fill);
        g.FillPath(brush, path);
        using Pen pen = new(_focused ? Theme.Accent : Theme.Border, _focused ? 1.6f : 1f);
        g.DrawPath(pen, path);

        TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        if (_icon.Length > 0)
        {
            TextRenderer.DrawText(g, _icon, Theme.Icon(10.5f), new Rectangle(Theme.S(12), 0, Theme.S(22), Height),
                _focused ? Theme.Accent : Theme.TextFaint, flags | TextFormatFlags.HorizontalCenter);
        }

        if (_suffix.Length > 0)
        {
            TextRenderer.DrawText(g, _suffix, Theme.F(9.5f), new Rectangle(0, 0, Width - Theme.S(12), Height),
                Theme.TextMuted, flags | TextFormatFlags.Right);
        }
    }
}

/// <summary>Liste déroulante entièrement dessinée (le ComboBox natif reste gris en mode sombre).</summary>
public sealed class SelectBox : Control
{
    private readonly List<object> _items = new();
    private int _index = -1;
    private bool _hover;
    private ToolStripDropDown? _drop;

    public SelectBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        TabStop = true;
        Height = Theme.S(40);
        Cursor = Cursors.Hand;
        Font = Theme.Body;
        BackColor = Theme.SurfaceAlt;
    }

    public Func<object, string> Format { get; set; } = o => o.ToString() ?? string.Empty;

    public string Placeholder { get; set; } = "Sélectionner…";

    public IReadOnlyList<object> Items => _items;

    public event EventHandler? SelectedIndexChanged;

    public int SelectedIndex
    {
        get => _index;
        set
        {
            value = value < 0 || value >= _items.Count ? -1 : value;
            if (value == _index)
            {
                return;
            }

            _index = value;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public object? SelectedItem
    {
        get => _index >= 0 && _index < _items.Count ? _items[_index] : null;
        set => SelectedIndex = value is null ? -1 : _items.IndexOf(value);
    }

    public T? Selected<T>() where T : class => SelectedItem as T;

    public void SetItems(IEnumerable<object> items, int selectedIndex = 0)
    {
        _items.Clear();
        _items.AddRange(items);
        int previous = _index;
        _index = _items.Count == 0 ? -1 : Math.Clamp(selectedIndex, -1, _items.Count - 1);
        Invalidate();
        if (previous != _index)
        {
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Theme.BackOf(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle rect = new(0, 0, Width - 1, Height - 1);
        using GraphicsPath path = Theme.RoundedRect(rect, Theme.S(8));
        Color fill = !Enabled ? Theme.Mix(Theme.SurfaceAlt, Theme.Border, 0.2f) : _hover ? Theme.Mix(Theme.SurfaceAlt, Theme.SurfaceHover, 0.5f) : Theme.SurfaceAlt;
        using SolidBrush brush = new(fill);
        g.FillPath(brush, path);
        using Pen pen = new(Focused && ShowFocusCues ? Theme.Accent : Theme.Border, Focused ? 1.6f : 1f);
        g.DrawPath(pen, path);

        TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine |
                                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        string text = SelectedItem is { } item ? Format(item) : Placeholder;
        Color color = !Enabled ? Theme.TextFaint : SelectedItem is null ? Theme.TextFaint : Theme.Text;
        TextRenderer.DrawText(g, text, Font, new Rectangle(Theme.S(12), 0, Math.Max(1, Width - Theme.S(44)), Height), color, flags);
        TextRenderer.DrawText(g, Icons.ChevronDown, Theme.Icon(8f), new Rectangle(Width - Theme.S(34), 0, Theme.S(24), Height),
            Theme.TextMuted, flags | TextFormatFlags.HorizontalCenter);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Up or Keys.Down or Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button == MouseButtons.Left && Enabled)
        {
            if (_drop is { Visible: true })
            {
                CloseDropDown();
            }
            else
            {
                OpenDropDown();
            }
        }
    }

    private void CloseDropDown()
    {
        if (_drop is { IsDisposed: false } drop)
        {
            drop.Close();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!Enabled || _items.Count == 0)
        {
            return;
        }

        switch (e.KeyCode)
        {
            case Keys.Down when !e.Alt:
                SelectedIndex = Math.Min(_items.Count - 1, _index + 1);
                e.Handled = true;
                break;
            case Keys.Up:
                SelectedIndex = Math.Max(0, _index - 1);
                e.Handled = true;
                break;
            case Keys.Space or Keys.Enter or Keys.Down:
                OpenDropDown();
                e.Handled = true;
                break;
        }
    }

    private void OpenDropDown()
    {
        if (_items.Count == 0)
        {
            return;
        }

        CloseDropDown();
        int itemHeight = Theme.S(34);
        int visible = Math.Min(_items.Count, 8);
        ListBox list = new()
        {
            BorderStyle = BorderStyle.None,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = itemHeight,
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            Font = Font,
            IntegralHeight = false,
            Dock = DockStyle.Fill
        };
        list.Items.AddRange(_items.Select(Format).Cast<object>().ToArray());
        list.SelectedIndex = _index;
        int hot = -1;
        list.DrawItem += (_, args) =>
        {
            if (args.Index < 0)
            {
                return;
            }

            bool selected = args.Index == _index;
            bool isHot = args.Index == hot;
            Rectangle bounds = Rectangle.Inflate(args.Bounds, -Theme.S(4), -Theme.S(2));
            args.Graphics.FillRectangle(Brushes.Transparent, args.Bounds);
            using (SolidBrush back = new(Theme.Surface))
            {
                args.Graphics.FillRectangle(back, args.Bounds);
            }

            if (selected || isHot)
            {
                args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using GraphicsPath p = Theme.RoundedRect(bounds, Theme.S(6));
                using SolidBrush b = new(selected ? Theme.SelectionBack : Theme.SurfaceHover);
                args.Graphics.FillPath(b, p);
            }

            TextRenderer.DrawText(args.Graphics, list.Items[args.Index]?.ToString(), Font,
                new Rectangle(bounds.X + Theme.S(8), bounds.Y, bounds.Width - Theme.S(16), bounds.Height),
                selected ? Theme.Accent : Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        };
        list.MouseMove += (_, args) =>
        {
            int idx = list.IndexFromPoint(args.Location);
            if (idx != hot)
            {
                hot = idx;
                list.Invalidate();
            }
        };

        Panel frame = new()
        {
            BackColor = Theme.Border,
            Padding = new Padding(1),
            Size = new Size(Math.Max(Width, Theme.S(160)), visible * itemHeight + 2)
        };
        frame.Controls.Add(list);

        ToolStripDropDown drop = new()
        {
            AutoClose = true,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
            DropShadowEnabled = false,
            BackColor = Theme.Border
        };
        ToolStripControlHost host = new(frame) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = frame.Size };
        drop.Items.Add(host);

        void Commit()
        {
            int idx = list.SelectedIndex;
            CloseDropDown();
            if (idx >= 0)
            {
                SelectedIndex = idx;
            }

            Focus();
        }

        list.MouseUp += (_, args) =>
        {
            int idx = list.IndexFromPoint(args.Location);
            if (idx >= 0)
            {
                list.SelectedIndex = idx;
                Commit();
            }
        };
        list.KeyDown += (_, args) =>
        {
            if (args.KeyCode == Keys.Enter)
            {
                Commit();
                args.Handled = true;
            }
            else if (args.KeyCode == Keys.Escape)
            {
                CloseDropDown();
                Focus();
                args.Handled = true;
            }
        };
        drop.Closed += (_, _) =>
        {
            if (ReferenceEquals(_drop, drop))
            {
                _drop = null;
            }

            DropDownDisposer.DisposeAfterMessages(drop);
        };
        _drop = drop;
        drop.Show(this, new Point(0, Height + Theme.S(2)));
        list.Focus();
    }
}

/// <summary>Style de <see cref="ToggleBox"/>.</summary>
public enum ToggleStyle
{
    /// <summary>Interrupteur (par défaut).</summary>
    Switch,
    /// <summary>Case carrée dessinée au thème.</summary>
    Check
}

/// <summary>Interrupteur (ou case) dessiné au thème avec libellé — jamais la case à cocher native de Windows.</summary>
public sealed class ToggleBox : Control
{
    private bool _checked;
    private bool _hover;

    public ToggleBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        Font = Theme.Body;
        Height = Theme.S(28);
        AutoSize = true;
    }

    public event EventHandler? CheckedChanged;

    public ToggleStyle Style { get; set; } = ToggleStyle.Switch;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
            {
                return;
            }

            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private int MarkWidth => Style == ToggleStyle.Switch ? Theme.S(36) : Theme.S(20);

    public override Size GetPreferredSize(Size proposedSize)
    {
        Size text = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        return new Size(MarkWidth + (Text.Length > 0 ? Theme.S(10) + text.Width : 0) + 2, Math.Max(Theme.S(28), text.Height + Theme.S(6)));
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Theme.BackOf(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        bool hot = (_hover || Focused) && Enabled;
        if (Style == ToggleStyle.Switch)
        {
            int h = Theme.S(20);
            Rectangle track = new(1, (Height - h) / 2, MarkWidth - 2, h);
            using (GraphicsPath path = Theme.RoundedRect(track, h / 2))
            {
                Color fill = !Enabled ? Theme.Mix(Theme.SurfaceAlt, Theme.Border, 0.4f)
                    : _checked ? (hot ? Theme.AccentHover : Theme.Accent)
                    : hot ? Theme.SurfaceHover : Theme.Border;
                using SolidBrush brush = new(fill);
                g.FillPath(brush, path);
                if (Focused && ShowFocusCues)
                {
                    using Pen focus = new(Theme.Alpha(Theme.Blue, 200), 1.5f);
                    g.DrawPath(focus, path);
                }
            }

            int knob = h - Theme.S(6);
            int x = _checked ? track.Right - knob - Theme.S(3) : track.Left + Theme.S(3);
            using SolidBrush knobBrush = new(_checked ? Theme.OnAccent : Theme.Text);
            g.FillEllipse(knobBrush, x, track.Top + Theme.S(3), knob, knob);
        }
        else
        {
            int size = Theme.S(20);
            Rectangle box = new(1, (Height - size) / 2, size, size);
            using GraphicsPath path = Theme.RoundedRect(box, Theme.S(5));
            using SolidBrush fill = new(_checked ? Theme.Accent : Theme.SurfaceAlt);
            g.FillPath(fill, path);
            using Pen pen = new(_checked ? Theme.Accent : hot ? Theme.AccentHover : Theme.Border, 1.4f);
            g.DrawPath(pen, path);
            if (_checked)
            {
                TextRenderer.DrawText(g, Icons.Check, Theme.Icon(9f), box, Theme.OnAccent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine |
                                TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
        int left = MarkWidth + Theme.S(10);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(left, 0, Math.Max(1, Width - left), Height),
            Enabled ? Theme.Text : Theme.TextFaint, flags);
    }

    protected override void OnClick(EventArgs e)
    {
        if (Enabled)
        {
            Checked = !Checked;
        }

        base.OnClick(e);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override bool IsInputKey(Keys keyData) => keyData == Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }
}

/// <summary>Sélecteur de date : champ + calendrier déroulant dessiné.</summary>
public sealed class DateBox : Control
{
    private DateTime _value = DateTime.Today;
    private bool _hover;
    private ToolStripDropDown? _drop;

    public DateBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        TabStop = true;
        Height = Theme.S(40);
        Cursor = Cursors.Hand;
        Font = Theme.Body;
    }

    public event EventHandler? ValueChanged;

    public DateTime Value
    {
        get => _value;
        set
        {
            value = value.Date;
            if (value == _value)
            {
                return;
            }

            _value = value;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Theme.BackOf(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle rect = new(0, 0, Width - 1, Height - 1);
        using GraphicsPath path = Theme.RoundedRect(rect, Theme.S(8));
        using SolidBrush brush = new(!Enabled ? Theme.Mix(Theme.SurfaceAlt, Theme.Border, 0.2f) : _hover ? Theme.Mix(Theme.SurfaceAlt, Theme.SurfaceHover, 0.5f) : Theme.SurfaceAlt);
        g.FillPath(brush, path);
        using Pen pen = new(Focused ? Theme.Accent : Theme.Border, Focused ? 1.6f : 1f);
        g.DrawPath(pen, path);
        TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        TextRenderer.DrawText(g, _value.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("fr-FR")), Font,
            new Rectangle(Theme.S(12), 0, Width - Theme.S(44), Height), Enabled ? Theme.Text : Theme.TextFaint, flags);
        TextRenderer.DrawText(g, "", Theme.Icon(10f), new Rectangle(Width - Theme.S(36), 0, Theme.S(26), Height),
            Theme.TextMuted, flags | TextFormatFlags.HorizontalCenter);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down or Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Up: Value = _value.AddDays(1); e.Handled = true; break;
            case Keys.Down: Value = _value.AddDays(-1); e.Handled = true; break;
            case Keys.Enter or Keys.Space: OpenCalendar(); e.Handled = true; break;
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button == MouseButtons.Left && Enabled)
        {
            if (_drop is { Visible: true })
            {
                CloseDropDown();
            }
            else
            {
                OpenCalendar();
            }
        }
    }

    private void CloseDropDown()
    {
        if (_drop is { IsDisposed: false } drop)
        {
            drop.Close();
        }
    }

    private void OpenCalendar()
    {
        CloseDropDown();
        CalendarView calendar = new(_value);
        Panel frame = new()
        {
            BackColor = Theme.Border,
            Padding = new Padding(1),
            Size = new Size(calendar.Width + 2, calendar.Height + 2)
        };
        frame.Controls.Add(calendar);
        calendar.Location = new Point(1, 1);
        ToolStripDropDown drop = new()
        {
            AutoClose = true,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
            DropShadowEnabled = false,
            BackColor = Theme.Border
        };
        drop.Items.Add(new ToolStripControlHost(frame) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = frame.Size });
        calendar.DatePicked += (_, d) =>
        {
            CloseDropDown();
            Value = d;
            Focus();
        };
        drop.Closed += (_, _) =>
        {
            if (ReferenceEquals(_drop, drop))
            {
                _drop = null;
            }

            DropDownDisposer.DisposeAfterMessages(drop);
        };
        _drop = drop;
        drop.Show(this, new Point(0, Height + Theme.S(2)));
    }
}

/// <summary>
/// Libère un menu déroulant après la fin du traitement des messages Windows. Une sélection peut
/// déclencher deux fermetures (appel explicite puis fermeture automatique) ; détruire le contrôle
/// pendant la première rend la seconde invalide et provoque « Cannot access a disposed object ».
/// </summary>
internal static class DropDownDisposer
{
    internal static void DisposeAfterMessages(ToolStripDropDown drop)
    {
        void OnIdle(object? sender, EventArgs e)
        {
            Application.Idle -= OnIdle;
            if (!drop.IsDisposed)
            {
                drop.Dispose();
            }
        }

        Application.Idle += OnIdle;
    }
}

/// <summary>Calendrier mensuel dessiné à la main (utilisé par <see cref="DateBox"/>).</summary>
public sealed class CalendarView : Control
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private DateTime _month;
    private readonly DateTime _selected;
    private int _hoverCell = -1;
    private int _hoverNav;
    private readonly int _cell = Theme.S(38);
    private readonly int _head = Theme.S(46);

    public CalendarView(DateTime selected)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        _selected = selected.Date;
        _month = new DateTime(selected.Year, selected.Month, 1);
        Size = new Size(_cell * 7 + Theme.S(16), _head + _cell * 7 + Theme.S(16) + Theme.S(4));
        BackColor = Theme.Surface;
        Cursor = Cursors.Hand;
    }

    public event EventHandler<DateTime>? DatePicked;

    private DateTime FirstCell
    {
        get
        {
            int offset = ((int)_month.DayOfWeek + 6) % 7; // lundi = 0
            return _month.AddDays(-offset);
        }
    }

    private Rectangle CellRect(int index) =>
        new(Theme.S(8) + index % 7 * _cell, _head + _cell + index / 7 * _cell, _cell, _cell);

    private Rectangle PrevRect => new(Theme.S(8), Theme.S(6), Theme.S(34), Theme.S(34));
    private Rectangle NextRect => new(Width - Theme.S(8) - Theme.S(34), Theme.S(6), Theme.S(34), Theme.S(34));

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Theme.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        TextFormatFlags center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;

        string title = Fr.TextInfo.ToTitleCase(_month.ToString("MMMM yyyy", Fr));
        TextRenderer.DrawText(g, title, Theme.F(11f, FontStyle.Bold), new Rectangle(0, Theme.S(6), Width, Theme.S(34)), Theme.Text, center);
        foreach ((Rectangle r, string glyph, int id) in new[] { (PrevRect, Icons.ChevronLeft, 1), (NextRect, Icons.ChevronRight, 2) })
        {
            if (_hoverNav == id)
            {
                using GraphicsPath p = Theme.RoundedRect(r, Theme.S(8));
                using SolidBrush b = new(Theme.SurfaceHover);
                g.FillPath(b, p);
            }

            TextRenderer.DrawText(g, glyph, Theme.Icon(10f), r, Theme.TextMuted, center);
        }

        string[] days = { "L", "M", "M", "J", "V", "S", "D" };
        for (int i = 0; i < 7; i++)
        {
            Rectangle r = new(Theme.S(8) + i * _cell, _head, _cell, _cell);
            TextRenderer.DrawText(g, days[i], Theme.F(9f, FontStyle.Bold), r, Theme.TextFaint, center);
        }

        DateTime first = FirstCell;
        for (int i = 0; i < 42; i++)
        {
            DateTime day = first.AddDays(i);
            Rectangle r = Rectangle.Inflate(CellRect(i), -Theme.S(2), -Theme.S(2));
            bool inMonth = day.Month == _month.Month;
            bool selected = day == _selected;
            bool today = day == DateTime.Today;
            if (selected || _hoverCell == i)
            {
                using GraphicsPath p = Theme.RoundedRect(r, Theme.S(8));
                using SolidBrush b = new(selected ? Theme.Accent : Theme.SurfaceHover);
                g.FillPath(b, p);
            }
            else if (today)
            {
                using GraphicsPath p = Theme.RoundedRect(r, Theme.S(8));
                using Pen pen = new(Theme.Accent, 1.2f);
                g.DrawPath(pen, p);
            }

            Color color = selected ? Theme.OnAccent : inMonth ? Theme.Text : Theme.TextFaint;
            TextRenderer.DrawText(g, day.Day.ToString(), Theme.F(10f, selected ? FontStyle.Bold : FontStyle.Regular), r, color, center);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int cell = -1;
        for (int i = 0; i < 42; i++)
        {
            if (CellRect(i).Contains(e.Location))
            {
                cell = i;
                break;
            }
        }

        int nav = PrevRect.Contains(e.Location) ? 1 : NextRect.Contains(e.Location) ? 2 : 0;
        if (cell != _hoverCell || nav != _hoverNav)
        {
            _hoverCell = cell;
            _hoverNav = nav;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverCell = -1;
        _hoverNav = 0;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (PrevRect.Contains(e.Location))
        {
            _month = _month.AddMonths(-1);
            Invalidate();
            return;
        }

        if (NextRect.Contains(e.Location))
        {
            _month = _month.AddMonths(1);
            Invalidate();
            return;
        }

        for (int i = 0; i < 42; i++)
        {
            if (CellRect(i).Contains(e.Location))
            {
                DatePicked?.Invoke(this, FirstCell.AddDays(i));
                return;
            }
        }
    }
}

/// <summary>Quantité : − [n] + (entier borné).</summary>
public sealed class StepperBox : Control
{
    private readonly FlatButton _minus;
    private readonly FlatButton _plus;
    private readonly Label _value;
    private int _number = 1;

    public StepperBox()
    {
        Height = Theme.S(40);
        _minus = Make("−");
        _plus = Make("+");
        _value = new Label
        {
            Text = "1",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = Theme.F(11f, FontStyle.Bold),
            ForeColor = Theme.Text,
            BackColor = Theme.SurfaceAlt
        };
        _minus.Click += (_, _) => Number -= 1;
        _plus.Click += (_, _) => Number += 1;
        Controls.Add(_value);
        Controls.Add(_minus);
        Controls.Add(_plus);
        Layout += (_, _) => DoLayout();
    }

    public int Minimum { get; set; } = 1;

    public int Maximum { get; set; } = 999;

    public event EventHandler? NumberChanged;

    public int Number
    {
        get => _number;
        set
        {
            value = Math.Clamp(value, Minimum, Maximum);
            if (value == _number)
            {
                return;
            }

            _number = value;
            _value.Text = value.ToString();
            NumberChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private FlatButton Make(string text) => new()
    {
        Text = text,
        Kind = ButtonKind.Secondary,
        AutoSize = false,
        Font = Theme.F(12f, FontStyle.Bold),
        Margin = Padding.Empty
    };

    private void DoLayout()
    {
        int b = Theme.S(40);
        _minus.SetBounds(0, 0, b, Height);
        _plus.SetBounds(Width - b, 0, b, Height);
        _value.SetBounds(b, 0, Math.Max(1, Width - 2 * b), Height);
    }
}

/// <summary>
/// TextBox dont le texte d'aide (placeholder) est dessiné au thème quand le champ est vide — à l'écran (WM_PAINT)
/// comme dans les captures (WM_PRINT / WM_PRINTCLIENT), y compris pendant la saisie tant que rien n'est tapé.
/// </summary>
internal sealed class CueTextBox : TextBox
{
    private const int WmPaint = 0x000F;
    private const int WmPrint = 0x0317;
    private const int WmPrintClient = 0x0318;
    private string _cue = string.Empty;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool HideCaret(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowCaret(IntPtr hWnd);

    public string Cue
    {
        get => _cue;
        set
        {
            _cue = value ?? string.Empty;
            Invalidate();
        }
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg is not (WmPaint or WmPrint or WmPrintClient) || _cue.Length == 0 || !IsHandleCreated || TextLength > 0)
        {
            return;
        }

        if (m.Msg == WmPaint)
        {
            bool caret = Focused && HideCaret(Handle);
            using (Graphics g = CreateGraphics())
            {
                DrawCue(g);
            }

            if (caret)
            {
                ShowCaret(Handle);
            }
        }
        else if ((m.Msg == WmPrintClient || m.Msg == WmPrint) && m.WParam != IntPtr.Zero)
        {
            using Graphics g = Graphics.FromHdc(m.WParam);
            DrawCue(g);
        }
    }

    private void DrawCue(Graphics g)
    {
        TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
        flags |= Multiline ? TextFormatFlags.WordBreak | TextFormatFlags.Top : TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
        flags |= TextAlign switch
        {
            HorizontalAlignment.Right => TextFormatFlags.Right,
            HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
            _ => TextFormatFlags.Left
        };
        Rectangle r = ClientRectangle;
        r.Inflate(-Theme.S(1), 0);
        if (!Multiline)
        {
            r.X += Theme.S(1);
        }

        TextRenderer.DrawText(g, _cue, Font, r, Theme.TextFaint, BackColor, flags);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }
}
