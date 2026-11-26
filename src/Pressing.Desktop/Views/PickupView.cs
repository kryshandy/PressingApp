using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Desktop.Dialogs;
using Pressing.Desktop.Services;
using Pressing.Desktop.Ui;

namespace Pressing.Desktop.Views;

/// <summary>Retrait rapide : saisie ou scan du ticket, puis ouverture directe du dialogue de retrait.</summary>
public sealed class PickupView : BaseView
{
    private readonly InputBox _ticket = new()
    {
        IconGlyph = Icons.Search,
        PlaceholderText = "Scannez ou saisissez le ticket (ex. PRESS-2026-00012 ou 12)",
        Height = Theme.S(56),
        Font = Theme.F(13f)
    };

    private readonly FlatButton _find = new() { Text = "Rechercher", Kind = ButtonKind.Primary, IconGlyph = Icons.Search, AutoSize = false, Height = Theme.S(56) };
    private readonly TextLabel _message = new() { Font = Theme.F(10f), ForeColor = Theme.Red, Visible = false };
    private readonly RoundedPanel _summary = new() { Visible = false };
    private readonly Label _summaryTicket = new() { AutoSize = true, Font = Theme.F(18f, FontStyle.Bold), ForeColor = Theme.Accent };
    private readonly Label _summaryClient = new() { AutoSize = true, Font = Theme.F(11f), ForeColor = Theme.Text };
    private readonly Label _summaryInfo = new() { AutoSize = true, Font = Theme.F(10f), ForeColor = Theme.TextMuted };
    private readonly PillLabel _summaryPill = new();
    private readonly FlatButton _summaryPickup = new() { Text = "Retrait", Kind = ButtonKind.Primary, IconGlyph = Icons.Pickup };
    private readonly FlatButton _summaryOpen = new() { Text = "Ouvrir la commande", Kind = ButtonKind.Secondary, IconGlyph = Icons.Orders };
    private readonly GridCard _ready = new("Aucune commande prête pour le moment.");
    private OrderDto? _order;

    public PickupView(IAppHost host) : base(host)
    {
        PageHeader header = new("Retraits", "Scannez le ticket du client pour lui remettre ses articles");
        header.AddAction("Actualiser", ButtonKind.Secondary, Icons.Refresh, async (_, _) => await RunAsync(RefreshAsync));

        GridRow entry = new GridRow(56).Add(_ticket, 9).Add(_find, 3);

        // Résumé de la commande trouvée
        _summary.Padding = new Padding(Theme.S(24), Theme.S(18), Theme.S(24), Theme.S(18));
        TableLayoutPanel s = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
        s.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        s.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        FlowLayoutPanel titleRow = new() { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        _summaryTicket.Margin = new Padding(0, 0, Theme.S(14), 0);
        _summaryPill.Margin = new Padding(0, Theme.S(6), 0, 0);
        titleRow.Controls.Add(_summaryTicket);
        titleRow.Controls.Add(_summaryPill);
        FlowLayoutPanel buttons = new() { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
        buttons.Controls.Add(_summaryOpen);
        buttons.Controls.Add(_summaryPickup);
        s.Controls.Add(titleRow, 0, 0);
        s.Controls.Add(_summaryClient, 0, 1);
        s.Controls.Add(_summaryInfo, 0, 2);
        s.Controls.Add(buttons, 1, 0);
        s.SetRowSpan(buttons, 3);
        _summary.Controls.Add(s);

        ThemedGrid grid = _ready.Grid;
        grid.AddText("ticket", "TICKET", nameof(OrderRow.Ticket), fixedWidth: 164);
        grid.AddText("client", "CLIENT", nameof(OrderRow.Client), 55, minWidth: 150);
        grid.AddText("items", "ARTICLES", nameof(OrderRow.Items), 45, minWidth: 140);
        grid.AddText("due", "PRÉVUE LE", nameof(OrderRow.Due), fixedWidth: 120);
        grid.AddText("balance", "RESTE", nameof(OrderRow.Balance), right: true, fixedWidth: 128);
        grid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0 && grid.Rows[e.RowIndex].DataBoundItem is OrderRow row)
            {
                await RunAsync(() => LookupAsync(row.Ticket));
            }
        };
        TitledCard readyCard = new("Prêtes à retirer", _ready, "Double-cliquez pour lancer le retrait", flushContent: true) { Margin = new Padding(0, 0, 0, 0) };

        TitledCard scanCard = new("Scanner un ticket", new VStack(12).Add(entry).Add(_message), "Lecteur de code-barres ou saisie clavier, puis Entrée", autoHeight: true);
        VStack page = new VStack()
            .Add(scanCard)
            .Add(_summary, StackSize.Fixed, 132)
            .Add(readyCard, StackSize.Fill);
        SetPage(header, page);

        _find.Click += async (_, _) => await RunAsync(() => LookupAsync(_ticket.Text));
        _ticket.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                await RunAsync(() => LookupAsync(_ticket.Text));
            }
        };
        _summaryPickup.Click += async (_, _) => await StartPickupAsync();
        _summaryOpen.Click += (_, _) => { if (_order is not null) Host.OpenOrder(_order.Id); };
    }

    public override string Title => "Retraits";

    public override async Task LoadAsync()
    {
        List<OrderSummaryDto>? ready = await Api.GetReadyOrdersAsync(50);
        if (!Alive)
        {
            return;
        }

        _ready.Grid.SetRows((ready ?? new List<OrderSummaryDto>()).Select(o => new OrderRow(o)).ToList());
        _ready.UpdateEmpty();
        _ticket.Focus();
    }

    public override Task ReactivateAsync() => LoadAsync();

    private void ShowMessage(string? text, Color? color = null)
    {
        _message.Text = text ?? string.Empty;
        _message.ForeColor = color ?? Theme.Red;
        _message.Visible = !string.IsNullOrEmpty(text);
    }

    private async Task LookupAsync(string raw)
    {
        string text = raw.Trim();
        if (text.Length == 0)
        {
            ShowMessage("Saisissez ou scannez un numéro de ticket.");
            return;
        }

        string ticket = TicketParser.Normalize(text) ?? text.ToUpperInvariant();
        OrderDto? order = await Api.GetOrderByTicketAsync(ticket);
        if (!Alive)
        {
            return;
        }

        if (order is null)
        {
            _summary.Visible = false;
            _order = null;
            ShowMessage($"Aucune commande trouvée pour le ticket « {ticket} ».");
            _ticket.SelectAll();
            _ticket.Focus();
            return;
        }

        ShowMessage(null);
        ShowSummary(order);
        await StartPickupAsync();
    }

    private void ShowSummary(OrderDto order)
    {
        _order = order;
        _summary.Visible = true;
        _summaryTicket.Text = order.TicketNumber;
        _summaryPill.Text = order.StatusLabel;
        _summaryPill.PillColor = Theme.StatusColor(order.Status);
        _summaryClient.Text = $"{order.ClientName} · {order.ClientPhone}";
        int ready = order.Items.Where(i => i.Status == ItemStatus.Ready).Sum(i => i.RemainingQuantity);
        int total = order.Items.Sum(i => i.Quantity);
        _summaryInfo.Text = $"{ready} article(s) prêt(s) sur {total} · " +
                            (order.Balance > 0 ? $"reste à payer {Fmt.Money(order.Balance)}" : "entièrement réglée");
        _summaryPickup.Enabled = ready > 0 && order.Status != OrderStatus.Cancelled;
    }

    private async Task StartPickupAsync()
    {
        if (_order is null)
        {
            return;
        }

        OrderDto order = _order;
        if (order.Status == OrderStatus.Cancelled)
        {
            AppDialogs.Info(this, "Commande annulée", $"La commande {order.TicketNumber} a été annulée : aucun retrait possible.", MessageKind.Warning);
            return;
        }

        if (!order.Items.Any(i => i.Status == ItemStatus.Ready && i.RemainingQuantity > 0))
        {
            string reason = order.Status == OrderStatus.Completed
                ? "Tous les articles de cette commande ont déjà été retirés."
                : "Aucun article n'est encore prêt. Passez les articles au statut « Prêt » depuis le détail de la commande.";
            AppDialogs.Info(this, "Rien à retirer", reason, MessageKind.Warning);
            return;
        }

        using PickupDialog dialog = new(Api, order);
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Host.Toast($"Retrait enregistré pour {order.TicketNumber}.");
            _ticket.Text = string.Empty;
            _summary.Visible = false;
            _order = null;
            await LoadAsync();
        }
    }
}
