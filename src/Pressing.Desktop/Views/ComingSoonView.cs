using Pressing.Desktop.Services;
using Pressing.Desktop.Ui;

namespace Pressing.Desktop.Views;

/// <summary>
/// Écran provisoire des modules d'administration (Promotions, Rapports, Personnel, Paramètres).
/// À remplacer dans <see cref="NavRegistry"/> par la vue réelle.
/// </summary>
public sealed class ComingSoonView : BaseView
{
    private readonly string _title;

    public ComingSoonView(IAppHost host, string title, string description, string glyph) : base(host)
    {
        _title = title;
        PageHeader header = new(title, "Module en cours de développement");

        RoundedPanel card = new() { Padding = Theme.Pad(40) };
        TableLayoutPanel center = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            AutoSize = false
        };
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        center.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        TableLayoutPanel content = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Anchor = AnchorStyles.None
        };
        IconBadge badge = new(glyph, Theme.Accent, 72) { Anchor = AnchorStyles.None, Margin = new Padding(0, 0, 0, Theme.S(16)) };
        Label name = UiKit.Label(title, Theme.F(16f, FontStyle.Bold));
        name.Anchor = AnchorStyles.None;
        Label text = UiKit.Muted(description, 10.5f);
        text.Anchor = AnchorStyles.None;
        text.Margin = new Padding(0, Theme.S(6), 0, Theme.S(4));
        Label soon = UiKit.Label("Bientôt disponible dans l'application de bureau.", Theme.F(10f), Theme.Accent);
        soon.Anchor = AnchorStyles.None;
        content.Controls.Add(badge, 0, 0);
        content.Controls.Add(name, 0, 1);
        content.Controls.Add(text, 0, 2);
        content.Controls.Add(soon, 0, 3);
        center.Controls.Add(content, 0, 1);
        card.Controls.Add(center);
        SetPage(header, card);
    }

    public override string Title => _title;
}
