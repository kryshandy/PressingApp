using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Desktop.Dialogs;
using Pressing.Desktop.Services;
using Pressing.Desktop.Ui;

namespace Pressing.Desktop.Views;

/// <summary>Fiche client : coordonnées, indicateurs, commandes, paiements, fidélité et accès web.</summary>
public sealed class ClientDetailView : BaseView
{
    private sealed class LoyaltyRow : ITintRow
    {
        public LoyaltyRow(LoyaltyTransactionDto t)
        {
            Source = t;
            Date = Fmt.DateTime(t.CreatedAt);
            Type = t.TypeLabel;
            Points = (t.Points > 0 ? "+" : string.Empty) + t.Points;
            Note = string.IsNullOrWhiteSpace(t.TicketNumber) ? t.Note : $"{t.Note} ({t.TicketNumber})";
        }

        public LoyaltyTransactionDto Source { get; }
        public string Date { get; }
        public string Type { get; }
        public string Points { get; }
        public string Note { get; }

        public Color? GetTint(string columnName) => columnName == "points" ? (Source.Points >= 0 ? Theme.Green : Theme.Amber) : null;
    }

    private readonly int _clientId;
    private ClientDetailDto? _detail;
    private readonly TwoColumnPanel _body;

    private readonly Label _name = new() { AutoSize = true, Font = Theme.F(22f, FontStyle.Bold), ForeColor = Theme.Text, Margin = Padding.Empty };
    private readonly Label _subtitle = new() { AutoSize = true, Font = Theme.F(10f), ForeColor = Theme.TextMuted, Margin = new Padding(0, Theme.S(2), 0, 0) };
    private readonly PillLabel _webPill = new() { Text = "Accès web actif", PillColor = Theme.Blue, Visible = false, Anchor = AnchorStyles.Left };
    private readonly PillLabel _inactivePill = new() { Text = "Inactif", PillColor = Theme.Slate, Visible = false, Anchor = AnchorStyles.Left };
    private readonly FlatButton _newOrder = new() { Text = "Nouveau dépôt", Kind = ButtonKind.Primary, IconGlyph = Icons.Add };
    private readonly FlatButton _edit = new() { Text = "Modifier", Kind = ButtonKind.Secondary, IconGlyph = Icons.Edit };
    private readonly FlatButton _access = new() { Text = "Accès web", Kind = ButtonKind.Secondary, IconGlyph = Icons.Key };
    private readonly FlatButton _revoke = new() { Text = "Désactiver l'accès web", Kind = ButtonKind.Secondary, IconGlyph = Icons.Cancel };
    private readonly FlatButton _loyalty = new() { Text = "Ajuster les points", Kind = ButtonKind.Secondary, IconGlyph = Icons.Gift };
    private readonly Panel _statsHost = new() { Dock = DockStyle.Top, Height = Theme.S(108), Margin = new Padding(0, 0, 0, Theme.S(6)) };
    private readonly StatCard _statOrders = new("Commandes", "—", Theme.Blue);
    private readonly StatCard _statSpent = new("Total dépensé", "—", Theme.Accent);
    private readonly StatCard _statBalance = new("Solde dû", "—", Theme.Amber);
    private readonly StatCard _statPoints = new("Points fidélité", "—", Theme.Violet);
    private readonly GridCard _orders = new("Aucune commande pour ce client.");
    private readonly GridCard _payments = new("Aucun paiement.");
    private readonly GridCard _loyaltyGrid = new("Aucun mouvement de points.");
    private readonly TitledCard _ordersCard;
    private readonly TitledCard _paymentsCard;
    private readonly TitledCard _loyaltyCard;
    private readonly InfoList _contact = new();

    public ClientDetailView(IAppHost host, int clientId) : base(host)
    {
        _clientId = clientId;

        FlatButton back = new() { Text = "Retour", Kind = ButtonKind.Ghost, IconGlyph = Icons.Back, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, Theme.S(16), 0) };
        back.Click += (_, _) => Host.GoBack();
        TableLayoutPanel titleBox = new() { AutoSize = true, ColumnCount = 1, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
        titleBox.Controls.Add(_name, 0, 0);
        titleBox.Controls.Add(_subtitle, 0, 1);
        FlowLayoutPanel pills = new() { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = new Padding(Theme.S(18), 0, 0, 0) };
        pills.Controls.Add(_webPill);
        pills.Controls.Add(_inactivePill);
        TableLayoutPanel top = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4,
            Margin = Padding.Empty,
            Padding = new Padding(0, 0, 0, Theme.S(14))
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.Controls.Add(back, 0, 0);
        top.Controls.Add(titleBox, 1, 0);
        top.Controls.Add(pills, 2, 0);

        FlowLayoutPanel actions = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = Padding.Empty, Padding = new Padding(0, 0, 0, Theme.S(14)) };
        foreach (FlatButton b in new[] { _newOrder, _edit, _access, _revoke, _loyalty })
        {
            b.Margin = new Padding(0, 0, Theme.S(10), Theme.S(8));
            actions.Controls.Add(b);
        }

        _loyalty.Visible = Session.IsManagerOrAdmin;

        TableLayoutPanel stats = new() { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
        for (int i = 0; i < 4; i++)
        {
            stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        }

        stats.Controls.Add(_statOrders, 0, 0);
        stats.Controls.Add(_statSpent, 1, 0);
        stats.Controls.Add(_statBalance, 2, 0);
        stats.Controls.Add(_statPoints, 3, 0);
        _statsHost.Controls.Add(stats);

        ThemedGrid orders = _orders.Grid;
        orders.AddText("ticket", "TICKET", nameof(OrderRow.Ticket), 20, minWidth: 130);
        orders.AddText("created", "DÉPOSÉE LE", nameof(OrderRow.Created), 16, minWidth: 100);
        orders.AddText("due", "PRÉVUE LE", nameof(OrderRow.Due), 16, minWidth: 100);
        orders.AddText("total", "TOTAL", nameof(OrderRow.Total), 16, true, 100);
        orders.AddText("balance", "RESTE", nameof(OrderRow.Balance), 14, true, 90);
        orders.AddPill("status", "STATUT", 18, 130);
        orders.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && orders.Rows[e.RowIndex].DataBoundItem is OrderRow row)
            {
                Host.OpenOrder(row.Id);
            }
        };
        _ordersCard = new TitledCard("Commandes", _orders, "Double-clic pour ouvrir", flushContent: true) { Dock = DockStyle.Top, Margin = new Padding(6, 6, 6, 6) };

        ThemedGrid pay = _payments.Grid;
        pay.AddText("date", "DATE", nameof(PaymentRow.Date), 24, minWidth: 130);
        pay.AddText("ticket", "TICKET", nameof(PaymentRow.Ticket), 22, minWidth: 120);
        pay.AddText("type", "TYPE", nameof(PaymentRow.Type), 18, minWidth: 100);
        pay.AddText("method", "MODE", nameof(PaymentRow.Method), 16, minWidth: 90);
        pay.AddText("amount", "MONTANT", nameof(PaymentRow.Amount), 20, true, 110);
        _paymentsCard = new TitledCard("Paiements", _payments, null, flushContent: true) { Dock = DockStyle.Top, Margin = new Padding(6, 6, 6, 6) };

        ThemedGrid loy = _loyaltyGrid.Grid;
        loy.AddText("date", "DATE", nameof(LoyaltyRow.Date), 28, minWidth: 130);
        loy.AddText("type", "TYPE", nameof(LoyaltyRow.Type), 22, minWidth: 110);
        loy.AddText("points", "POINTS", nameof(LoyaltyRow.Points), 14, true, 80);
        loy.AddText("note", "NOTE", nameof(LoyaltyRow.Note), 36, minWidth: 120);
        _loyaltyCard = new TitledCard("Fidélité", _loyaltyGrid, null, flushContent: true) { Dock = DockStyle.Top, Margin = new Padding(6, 6, 6, 6) };

        TableLayoutPanel left = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Margin = Padding.Empty };
        left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        left.Controls.Add(_ordersCard, 0, 0);
        left.Controls.Add(_paymentsCard, 0, 1);
        left.Controls.Add(_loyaltyCard, 0, 2);

        TitledCard contactCard = new("Coordonnées", _contact, null, false, true) { Margin = new Padding(6, 6, 6, 6) };
        TableLayoutPanel right = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Margin = Padding.Empty };
        right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        right.Controls.Add(contactCard, 0, 0);

        _body = new TwoColumnPanel(left, right, 372);

        ScrollPanel scroll = new();
        scroll.Controls.Add(_body);
        scroll.Controls.Add(_statsHost);
        _statsHost.SendToBack();
        _body.BringToFront();

        Controls.Add(UiKit.Rows((top, false), (actions, false), (scroll, true)));

        _newOrder.Click += (_, _) => { if (_detail is not null) Host.StartNewOrder(_detail.Client); };
        _edit.Click += async (_, _) => await EditAsync();
        _access.Click += async (_, _) => await AccessAsync();
        _revoke.Click += async (_, _) => await RevokeAsync();
        _loyalty.Click += async (_, _) => await LoyaltyAsync();
    }

    public override string Title => _detail is null ? "Client" : _detail.Client.FullName;

    public override async Task LoadAsync()
    {
        ClientDetailDto? detail = await Api.GetClientAsync(_clientId);
        if (!Alive)
        {
            return;
        }

        if (detail is null)
        {
            Host.Toast("Client introuvable.", MessageKind.Warning);
            Host.GoBack();
            return;
        }

        Bind(detail);
    }

    private void Bind(ClientDetailDto detail)
    {
        _detail = detail;
        ClientDto c = detail.Client;
        if (FindForm() is { } form)
        {
            form.Text = c.FullName + " — Pressing Éclat";
        }

        _name.Text = c.FullName;
        _subtitle.Text = $"Cliente depuis le {Fmt.Date(c.CreatedAt)}" + (c.LastOrderAt.HasValue ? $" · dernière commande {Fmt.Relative(c.LastOrderAt.Value)}" : string.Empty);
        _webPill.Visible = c.WebAccessEnabled;
        _inactivePill.Visible = !c.IsActive;
        _statOrders.Set(c.OrderCount.ToString(), "commande(s) au total");
        _statSpent.Set(Fmt.Money(c.TotalSpent), "montant cumulé");
        _statBalance.Set(c.Balance > 0 ? Fmt.Money(c.Balance) : "Aucun", c.Balance > 0 ? "à encaisser" : "compte à jour");
        _statPoints.Set(c.LoyaltyPoints.ToString(), "points disponibles");

        _contact.Clear();
        _contact.Add("Téléphone", c.Phone);
        _contact.Add("WhatsApp", c.WhatsApp);
        _contact.Add("E-mail", string.IsNullOrWhiteSpace(c.Email) ? "—" : c.Email);
        _contact.Add("Adresse", string.IsNullOrWhiteSpace(c.Address) ? "—" : c.Address);
        _contact.AddSeparator();
        _contact.Add("Accès web", c.WebAccessEnabled ? "Activé" : "Non activé", c.WebAccessEnabled ? Theme.Blue : Theme.TextMuted);
        if (!string.IsNullOrWhiteSpace(c.Notes))
        {
            _contact.AddSeparator();
            _contact.Add("Notes", c.Notes);
        }

        _orders.Grid.SetRows(detail.Orders.Select(o => new OrderRow(o)).ToList());
        _orders.UpdateEmpty();
        _payments.Grid.SetRows(detail.Payments.Select(p => new PaymentRow(p)).ToList());
        _payments.UpdateEmpty();
        _loyaltyGrid.Grid.SetRows(detail.LoyaltyTransactions.Select(t => new LoyaltyRow(t)).ToList());
        _loyaltyGrid.UpdateEmpty();
        Fit(_ordersCard, _orders, detail.Orders.Count);
        Fit(_paymentsCard, _payments, detail.Payments.Count);
        Fit(_loyaltyCard, _loyaltyGrid, detail.LoyaltyTransactions.Count);

        _access.Text = c.WebAccessEnabled ? "Réinitialiser l'accès web" : "Activer l'accès web";
        _revoke.Visible = c.WebAccessEnabled;
        _newOrder.Enabled = c.IsActive;
        _body.PerformLayout();
    }

    private static void Fit(TitledCard card, GridCard grid, int rows)
    {
        int shown = Math.Min(Math.Max(rows, 1), 10);
        card.Height = Theme.S(18) + Theme.S(34) + Theme.S(12) + grid.Grid.ColumnHeadersHeight + shown * grid.Grid.RowTemplate.Height + Theme.S(24);
    }

    private async Task EditAsync()
    {
        if (_detail is null)
        {
            return;
        }

        using ClientEditDialog dialog = new(Api, _detail.Client);
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Host.Toast("Fiche client mise à jour.");
            await RunAsync(LoadAsync);
        }
    }

    private async Task AccessAsync()
    {
        if (_detail is null)
        {
            return;
        }

        using ClientAccessDialog dialog = new(Api, _detail.Client);
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Host.Toast("Accès web mis à jour.");
            await RunAsync(LoadAsync);
        }
    }

    private async Task RevokeAsync()
    {
        if (_detail is null ||
            !AppDialogs.Confirm(this, "Désactiver l'accès web",
                $"{_detail.Client.FullName} ne pourra plus se connecter au site web. Ses commandes restent conservées.",
                "Désactiver", danger: true))
        {
            return;
        }

        bool ok = await RunAsync(async () => await Api.SetClientPasswordAsync(_clientId, new SetClientPasswordRequest(null, false, false)),
            "Désactivation impossible");
        if (ok)
        {
            Host.Toast("Accès web désactivé.", MessageKind.Warning);
            await RunAsync(LoadAsync);
        }
    }

    private async Task LoyaltyAsync()
    {
        if (_detail is null)
        {
            return;
        }

        using LoyaltyAdjustDialog dialog = new(Api, _detail.Client);
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Host.Toast("Points ajustés.");
            await RunAsync(LoadAsync);
        }
    }
}
