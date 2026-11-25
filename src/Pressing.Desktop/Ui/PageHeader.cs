namespace Pressing.Desktop.Ui;

/// <summary>En-tête de page : titre, sous-titre et actions alignées à droite.</summary>
public sealed class PageHeader : TableLayoutPanel
{
    private readonly Label _title;
    private readonly Label _subtitle;
    private readonly FlowLayoutPanel _actions;

    public PageHeader(string title, string? subtitle = null)
    {
        Dock = DockStyle.Top;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        ColumnCount = 2;
        RowCount = 1;
        Margin = Padding.Empty;
        Padding = new Padding(0, 0, 0, Theme.S(16));
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        TableLayoutPanel text = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };
        _title = new Label
        {
            Text = title,
            AutoSize = true,
            Font = Theme.F(19f, FontStyle.Bold),
            ForeColor = Theme.Text,
            Margin = Padding.Empty
        };
        _subtitle = new Label
        {
            Text = subtitle ?? string.Empty,
            AutoSize = true,
            Font = Theme.F(10f),
            ForeColor = Theme.TextMuted,
            Margin = new Padding(0, Theme.S(2), 0, 0),
            Visible = !string.IsNullOrEmpty(subtitle)
        };
        text.Controls.Add(_title, 0, 0);
        text.Controls.Add(_subtitle, 0, 1);

        _actions = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Anchor = AnchorStyles.Right,
            Margin = Padding.Empty
        };
        Controls.Add(text, 0, 0);
        Controls.Add(_actions, 1, 0);
    }

    public string Subtitle
    {
        get => _subtitle.Text;
        set
        {
            _subtitle.Text = value;
            _subtitle.Visible = !string.IsNullOrEmpty(value);
        }
    }

    public string TitleText
    {
        get => _title.Text;
        set => _title.Text = value;
    }

    public FlatButton AddAction(string text, ButtonKind kind, string icon = "", EventHandler? onClick = null)
    {
        FlatButton button = new() { Text = text, Kind = kind, IconGlyph = icon, Margin = new Padding(Theme.S(10), 0, 0, 0) };
        if (onClick is not null)
        {
            button.Click += onClick;
        }

        _actions.Controls.Add(button);
        return button;
    }

    public void AddControl(Control control)
    {
        control.Margin = new Padding(Theme.S(10), 0, 0, 0);
        _actions.Controls.Add(control);
    }
}
