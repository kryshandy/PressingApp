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
    private readonly PageHeader _header = new("Client", "Chargement…");
    private readonly InitialsAvatar _avatar = new();
    private readonly TextLabel _name = new() { Font = Theme.Semi(14f), ForeColor = Theme.TitleText };
    private readonly TextLabel _subtitle = new() { Font = Theme.F(9.5f), ForeColor = Theme.TextMuted, Wrap = true };
    private readonly PillLabel _webPill = new() { Text = "Accès web actif", PillColor = Theme.Blue, Visible = false };
    private readonly PillLabel _inactivePill = new() { Text = "Inactif", PillColor = Theme.Slate, Visible = false };
    private readonly FlatButton _newOrder = new() { Text = "Nouveau dépôt", Kind = ButtonKind.Primary, IconGlyph = Icons.Add };
    private readonly FlatButton _edit = new() { Text = "Modifier", Kind = ButtonKind.Secondary, IconGlyph = Icons.Edit };
    private readonly FlatButton _access = new() { Text = "Accès web", Kind = ButtonKind.Secondary, IconGlyph = Icons.Key };
    private readonly FlatButton _revoke = new() { Text = "Désactiver l'accès web", Kind = ButtonKind.Secondary, IconGlyph = Icons.Cancel };
    private readonly FlatButton _loyalty = new() { Text = "Ajuster les points", Kind = ButtonKind.Secondary, IconGlyph = Icons.Gift };
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

        FlatButton back = new() { Text = "Retour", Kind = ButtonKind.Secondary, IconGlyph = Icons.Back };
        back.Click += (_, _) => Host.GoBack();
        _header.SetLeading(back);
        _header.AddTitleAdornment(_webPill);
        _header.AddTitleAdornment(_inactivePill);
        if (Session.IsManagerOrAdmin)
        {
            _header.Actions.Add(_loyalty);
        }

        foreach (FlatButton b in new[] { _revoke, _access, _edit, _newOrder })
        {
            _header.Actions.Add(b);
        }

        _statPoints.BackColor = Theme.SurfaceAlt;
        _statPoints.DrawBorder = false;

        ThemedGrid orders = _orders.Grid;
        orders.AddText("ticket", "TICKET", nameof(OrderRow.Ticket), 100, minWidth: 150);
        orders.AddText("created", "DÉPOSÉE LE", nameof(OrderRow.Created), fixedWidth: 112);
        orders.AddText("due", "PRÉVUE LE", nameof(OrderRow.Due), fixedWidth: 112);
        orders.AddText("total", "TOTAL", nameof(OrderRow.Total), right: true, fixedWidth: 112);
        orders.AddText("balance", "RESTE", nameof(OrderRow.Balance), right: true, fixedWidth: 104);
        orders.AddPill("status", "STATUT", fixedWidth: 140);
        orders.SetOptional("created", "due");
        orders.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && orders.Rows[e.RowIndex].DataBoundItem is OrderRow row)
            {
                Host.OpenOrder(row.Id);
            }
        };
        _ordersCard = new TitledCard("Commandes", _orders, "Double-clic pour ouvrir", flushContent: true) ;

        ThemedGrid pay = _payments.Grid;
        pay.AddText("date", "DATE", nameof(PaymentRow.Date), fixedWidth: 148);
        pay.AddText("ticket", "TICKET", nameof(PaymentRow.Ticket), 50, minWidth: 140);
        pay.AddText("type", "TYPE", nameof(PaymentRow.Type), 25, minWidth: 100);
        pay.AddText("method", "MODE", nameof(PaymentRow.Method), 25, minWidth: 110);
        pay.AddText("amount", "MONTANT", nameof(PaymentRow.Amount), right: true, fixedWidth: 120);
        pay.SetOptional("method", "type");
        _paymentsCard = new TitledCard("Paiements", _payments, null, flushContent: true) ;

        ThemedGrid loy = _loyaltyGrid.Grid;
        loy.AddText("date", "DATE", nameof(LoyaltyRow.Date), fixedWidth: 148);
        loy.AddText("type", "TYPE", nameof(LoyaltyRow.Type), 30, minWidth: 110);
        loy.AddText("points", "POINTS", nameof(LoyaltyRow.Points), right: true, fixedWidth: 92);
        loy.AddText("note", "NOTE", nameof(LoyaltyRow.Note), 70, minWidth: 140);
        loy.SetOptional("type");
        _loyaltyCard = new TitledCard("Fidélité", _loyaltyGrid, null, flushContent: true) ;

        foreach (GridCard g in new[] { _orders, _payments, _loyaltyGrid })
        {
            g.AutoHeightRows = 3;
            g.MaxRows = 10;
        }

        // 4 + 8 : identité à gauche ; indicateurs puis historique à droite.
        VStack identity = new VStack(16)
            .Add(new AvatarRow(_avatar, _name, _subtitle))
            .Add(_contact)
            .Add(_statPoints);
        TitledCard identityCard = new("Identité", identity, null, false, true);
        VStack right = new VStack()
            .Add(new GridRow().Add(_statOrders, 4).Add(_statSpent, 4).Add(_statBalance, 4))
            .Add(_ordersCard)
            .Add(_paymentsCard)
            .Add(_loyaltyCard);
        GridRow body = new GridRow { StackBelow = 900 }.Add(identityCard, 4).Add(right, 8).Sticky(identityCard);
        SetPage(_header, body, scroll: true);

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
        _avatar.Initials = string.Concat(c.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpperInvariant(p[0])));
        _header.TitleText = c.FullName;
        _header.Subtitle = "Fiche client · " + c.Phone;
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

        _access.Text = c.WebAccessEnabled ? "Réinitialiser l'accès web" : "Activer l'accès web";
        _revoke.Visible = c.WebAccessEnabled;
        _newOrder.Enabled = c.IsActive;
        _header.PerformLayout();
    }

    /// <summary>Avatar + nom + ancienneté (haut de la carte Identité).</summary>
    private sealed class AvatarRow : LayoutControl
    {
        private readonly Control _avatar;
        private readonly TextLabel _name;
        private readonly TextLabel _since;

        public AvatarRow(Control avatar, TextLabel name, TextLabel since)
        {
            _avatar = avatar;
            _name = name;
            _since = since;
            Controls.Add(avatar);
            Controls.Add(name);
            Controls.Add(since);
        }

        private int TextLeft => _avatar.Width + Theme.S(16);

        public override Size GetPreferredSize(Size proposedSize)
        {
            int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
            int text = _name.Font.Height + Theme.S(4) + _since.GetPreferredSize(new Size(Math.Max(10, width - TextLeft), 0)).Height;
            return new Size(width, Math.Max(_avatar.Height, text));
        }

        protected override void Arrange()
        {
            _avatar.Location = new Point(0, (Height - _avatar.Height) / 2);
            int w = Math.Max(10, Width - TextLeft);
            int since = _since.GetPreferredSize(new Size(w, 0)).Height;
            int top = (Height - _name.Font.Height - Theme.S(4) - since) / 2;
            _name.SetBounds(TextLeft, top, w, _name.Font.Height);
            _since.SetBounds(TextLeft, top + _name.Font.Height + Theme.S(4), w, since);
        }
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
