using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Desktop.Dialogs;
using Pressing.Desktop.Services;
using Pressing.Desktop.Ui;

namespace Pressing.Desktop.Views;

/// <summary>Détail d'une commande : articles, paiements, retraits, totaux et actions.</summary>
public sealed class OrderDetailView : BaseView
{
    private sealed class PickupRow
    {
        public PickupRow(PickupDto p)
        {
            When = Fmt.DateTime(p.PickedUpAt);
            Items = string.Join(", ", p.Items.Select(i => $"{i.Quantity} × {i.CategoryName}"));
            Staff = p.StaffName ?? string.Empty;
            Note = p.Note ?? string.Empty;
        }

        public string When { get; }
        public string Items { get; }
        public string Staff { get; }
        public string Note { get; }
    }

    private readonly int _orderId;
    private OrderDto? _order;
    private readonly TwoColumnPanel _body;
    private FlowLayoutPanel _itemTools = null!;

    private readonly Label _ticket = new() { AutoSize = true, Font = Theme.F(22f, FontStyle.Bold), ForeColor = Theme.Accent, Margin = Padding.Empty };
    private readonly Label _subtitle = new() { AutoSize = true, Font = Theme.F(10f), ForeColor = Theme.TextMuted, Margin = new Padding(0, Theme.S(2), 0, 0) };
    private readonly PillLabel _statusPill = new() { Anchor = AnchorStyles.Left };
    private readonly PillLabel _expressPill = new() { Anchor = AnchorStyles.Left, Text = "EXPRESS", PillColor = Theme.Amber, Visible = false };

    private readonly FlatButton _pay = new() { Text = "Encaisser", Kind = ButtonKind.Primary, IconGlyph = Icons.Money };
    private readonly FlatButton _pickup = new() { Text = "Retrait", Kind = ButtonKind.Info, IconGlyph = Icons.Pickup };
    private readonly FlatButton _receipt = new() { Text = "Reçu PDF", Kind = ButtonKind.Secondary, IconGlyph = Icons.Pdf };
    private readonly FlatButton _email = new() { Text = "Envoyer par e-mail", Kind = ButtonKind.Secondary, IconGlyph = Icons.Mail };
    private readonly FlatButton _refund = new() { Text = "Rembourser", Kind = ButtonKind.Secondary, IconGlyph = Icons.Refund };
    private readonly FlatButton _cancel = new() { Text = "Annuler la commande", Kind = ButtonKind.Danger, IconGlyph = Icons.Cancel };

    private readonly GridCard _itemsGrid = new("Aucun article.");
    private readonly TitledCard _itemsCard;
    private readonly FlatButton _setProcessing = new() { Text = "En traitement", Kind = ButtonKind.Secondary, Height = Theme.S(36) };
    private readonly FlatButton _setReady = new() { Text = "Prêt", Kind = ButtonKind.Secondary, IconGlyph = Icons.Check, Height = Theme.S(36) };
    private readonly SelectBox _bulkStatus = new() { Width = Theme.S(170), Height = Theme.S(36) };
    private readonly FlatButton _bulkApply = new() { Text = "Tout passer à", Kind = ButtonKind.Secondary, Height = Theme.S(36) };
    private readonly GridCard _paymentsGrid = new("Aucun paiement enregistré.");
    private readonly TitledCard _paymentsCard;
    private readonly GridCard _pickupsGrid = new("Aucun retrait pour le moment.");
    private readonly TitledCard _pickupsCard;
    private readonly InfoList _clientInfo = new();
    private readonly InfoList _dates = new();
    private readonly InfoList _amounts = new();
    private readonly LinkLabel _clientLink = new();
    private readonly TitledCard _notesCard;
    private readonly Label _notes = new() { AutoSize = true, MaximumSize = new Size(Theme.S(300), 0), ForeColor = Theme.Text, Font = Theme.F(10f) };

    public OrderDetailView(IAppHost host, int orderId) : base(host)
    {
        _orderId = orderId;

        // ----- En-tête -----
        FlatButton back = new() { Text = "Retour", Kind = ButtonKind.Ghost, IconGlyph = Icons.Back, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, Theme.S(16), 0) };
        back.Click += (_, _) => Host.GoBack();
        TableLayoutPanel titleBox = new() { AutoSize = true, ColumnCount = 1, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
        titleBox.Controls.Add(_ticket, 0, 0);
        titleBox.Controls.Add(_subtitle, 0, 1);
        FlowLayoutPanel pills = new() { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = new Padding(Theme.S(18), 0, 0, 0) };
        pills.Controls.Add(_statusPill);
        pills.Controls.Add(_expressPill);

        TableLayoutPanel top = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4,
            RowCount = 1,
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

        FlowLayoutPanel actions = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Margin = Padding.Empty,
            Padding = new Padding(0, 0, 0, Theme.S(14)),
            Dock = DockStyle.Fill
        };
        foreach (FlatButton b in new[] { _pay, _pickup, _receipt, _email, _refund, _cancel })
        {
            b.Margin = new Padding(0, 0, Theme.S(10), Theme.S(8));
            actions.Controls.Add(b);
        }

        _refund.Visible = Session.IsManagerOrAdmin;
        _cancel.Visible = Session.IsManagerOrAdmin;

        // ----- Articles -----
        ThemedGrid items = _itemsGrid.Grid;
        items.AddText("article", "ARTICLE", nameof(ItemRow.Article), 28, minWidth: 140);
        items.AddText("service", "SERVICE", nameof(ItemRow.Service), 20, minWidth: 130);
        items.AddText("qty", "QTÉ", nameof(ItemRow.Quantity), 7, true, 56);
        items.AddText("unit", "PRIX UNIT.", nameof(ItemRow.UnitPrice), 13, true, 96);
        items.AddText("total", "TOTAL", nameof(ItemRow.Total), 13, true, 96);
        items.AddPill("status", "STATUT", 16, 130);
        items.AddText("picked", "RETIRÉ", nameof(ItemRow.PickedUp), 9, true, 72);
        items.SelectionChanged += (_, _) => UpdateItemButtons();

        _bulkStatus.SetChoices(new[]
        {
            new Choice<ItemStatus>(ItemStatus.Deposited, "Déposé"),
            new Choice<ItemStatus>(ItemStatus.InProgress, "En traitement"),
            new Choice<ItemStatus>(ItemStatus.Ready, "Prêt")
        }, ItemStatus.Ready);
        FlowLayoutPanel itemTools = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(Theme.S(19), 0, Theme.S(19), Theme.S(10))
        };
        Label selectedLabel = UiKit.Label("Sélection :", Theme.F(9.5f), Theme.TextMuted);
        Label allLabel = UiKit.Label("Tous :", Theme.F(9.5f), Theme.TextMuted);
        selectedLabel.Margin = new Padding(0, Theme.S(8), Theme.S(10), 0);
        allLabel.Margin = new Padding(Theme.S(16), Theme.S(8), Theme.S(10), 0);
        _bulkApply.Text = "Appliquer";
        foreach (Control c in new Control[] { selectedLabel, _setProcessing, _setReady, allLabel, _bulkStatus, _bulkApply })
        {
            if (c is not Label)
            {
                c.Margin = new Padding(0, 0, Theme.S(8), 0);
            }

            itemTools.Controls.Add(c);
        }

        _itemTools = itemTools;
        TableLayoutPanel itemsContent = (TableLayoutPanel)UiKit.Rows((itemTools, false), (_itemsGrid, true));
        _itemsCard = new TitledCard("Articles", itemsContent, null, flushContent: true) { Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, Theme.S(12)) };

        // ----- Paiements / retraits -----
        ThemedGrid pay = _paymentsGrid.Grid;
        pay.AddText("date", "DATE", nameof(PaymentRow.Date), 20, minWidth: 130);
        pay.AddText("type", "TYPE", nameof(PaymentRow.Type), 16, minWidth: 110);
        pay.AddText("method", "MODE", nameof(PaymentRow.Method), 16, minWidth: 100);
        pay.AddText("reference", "RÉFÉRENCE", nameof(PaymentRow.Reference), 18, minWidth: 100);
        pay.AddText("staff", "PAR", nameof(PaymentRow.Staff), 14, minWidth: 90);
        pay.AddText("amount", "MONTANT", nameof(PaymentRow.Amount), 16, true, 110);
        _paymentsCard = new TitledCard("Paiements", _paymentsGrid, null, flushContent: true) { Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, Theme.S(12)) };

        ThemedGrid pick = _pickupsGrid.Grid;
        pick.AddText("when", "DATE", nameof(PickupRow.When), 22, minWidth: 130);
        pick.AddText("items", "ARTICLES REMIS", nameof(PickupRow.Items), 44, minWidth: 160);
        pick.AddText("staff", "PAR", nameof(PickupRow.Staff), 16, minWidth: 90);
        pick.AddText("note", "NOTE", nameof(PickupRow.Note), 18, minWidth: 80);
        _pickupsCard = new TitledCard("Retraits", _pickupsGrid, null, flushContent: true) { Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, Theme.S(12)) };

        TableLayoutPanel left = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Margin = Padding.Empty };
        left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        left.Controls.Add(_itemsCard, 0, 0);
        left.Controls.Add(_paymentsCard, 0, 1);
        left.Controls.Add(_pickupsCard, 0, 2);

        // ----- Colonne de droite -----
        _clientLink.AutoSize = true;
        _clientLink.Font = Theme.F(12f, FontStyle.Bold);
        _clientLink.LinkColor = Theme.Text;
        _clientLink.ActiveLinkColor = Theme.Accent;
        _clientLink.VisitedLinkColor = Theme.Text;
        _clientLink.LinkBehavior = LinkBehavior.HoverUnderline;
        _clientLink.BackColor = Color.Transparent;
        _clientLink.Margin = new Padding(0, 0, 0, Theme.S(8));
        _clientLink.LinkClicked += (_, _) =>
        {
            if (_order is not null)
            {
                Host.OpenClient(_order.ClientId);
            }
        };
        TableLayoutPanel clientBody = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, BackColor = Color.Transparent };
        clientBody.Controls.Add(_clientLink, 0, 0);
        clientBody.Controls.Add(_clientInfo, 0, 1);
        TitledCard clientCard = new("Client et livraison", clientBody, null, false, true) { Margin = new Padding(0, 0, 0, Theme.S(12)) };
        TitledCard datesCard = new("Chronologie", _dates, null, false, true) { Margin = new Padding(0, 0, 0, Theme.S(12)) };
        TitledCard amountsCard = new("Montants", _amounts, null, false, true) { Margin = new Padding(0, 0, 0, Theme.S(12)) };
        _notesCard = new TitledCard("Notes", _notes, null, false, true) { Margin = new Padding(0, 0, 0, Theme.S(12)), Visible = false };

        TableLayoutPanel right = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Margin = Padding.Empty };
        right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        right.Controls.Add(amountsCard, 0, 0);
        right.Controls.Add(clientCard, 0, 1);
        right.Controls.Add(datesCard, 0, 2);
        right.Controls.Add(_notesCard, 0, 3);

        _body = new TwoColumnPanel(left, right, 372);
        ScrollPanel scroll = new();
        scroll.Controls.Add(_body);

        Controls.Add(UiKit.Rows((top, false), (actions, false), (scroll, true)));

        _pay.Click += async (_, _) => await PayAsync();
        _pickup.Click += async (_, _) => await PickupAsync();
        _receipt.Click += async (_, _) => await RunAsync(OpenReceiptAsync, "Reçu PDF");
        _email.Click += async (_, _) => await EmailAsync();
        _refund.Click += async (_, _) => await RefundAsync();
        _cancel.Click += async (_, _) => await CancelAsync();
        _setProcessing.Click += async (_, _) => await SetSelectedStatusAsync(ItemStatus.InProgress);
        _setReady.Click += async (_, _) => await SetSelectedStatusAsync(ItemStatus.Ready);
        _bulkApply.Click += async (_, _) => await BulkStatusAsync();
        UpdateButtons();
        SizeChanged += (_, _) =>
        {
            if (_order is not null)
            {
                FitHeights(_order);
                _body.PerformLayout();
            }
        };
    }

    public override string Title => _order is null ? "Commande" : "Commande " + _order.TicketNumber;

    public override async Task LoadAsync()
    {
        OrderDto? order = await Api.GetOrderAsync(_orderId);
        if (!Alive)
        {
            return;
        }

        if (order is null)
        {
            Host.Toast("Commande introuvable.", MessageKind.Warning);
            Host.GoBack();
            return;
        }

        Bind(order);
    }

    // ---- Affichage -----------------------------------------------------------------------
    private void Bind(OrderDto order)
    {
        _order = order;
        if (FindForm() is { } form)
        {
            form.Text = Title + " — Pressing Éclat";
        }

        _ticket.Text = order.TicketNumber;
        _subtitle.Text = $"{order.ClientName} · déposée {Fmt.Relative(order.CreatedAt)}";
        _statusPill.Text = order.StatusLabel;
        _statusPill.PillColor = Theme.StatusColor(order.Status);
        _expressPill.Visible = order.IsExpress;

        _itemsGrid.Grid.SetRows(order.Items.Select(i => new ItemRow(i)).ToList());
        _itemsGrid.UpdateEmpty();
        _paymentsGrid.Grid.SetRows(order.Payments.OrderByDescending(p => p.PaidAt).Select(p => new PaymentRow(p)).ToList());
        _paymentsGrid.UpdateEmpty();
        _pickupsGrid.Grid.SetRows(order.Pickups.OrderByDescending(p => p.PickedUpAt).Select(p => new PickupRow(p)).ToList());
        _pickupsGrid.UpdateEmpty();
        FitHeights(order);
        _pickupsCard.Visible = order.Pickups.Count > 0 || order.Status is OrderStatus.Ready or OrderStatus.PartiallyPickedUp or OrderStatus.Completed;

        _clientLink.Text = order.ClientName;
        _clientLink.Links.Clear();
        _clientLink.Links.Add(0, order.ClientName.Length);
        _clientInfo.Clear();
        _clientInfo.Add("Téléphone", order.ClientPhone);
        _clientInfo.Add("E-mail", string.IsNullOrWhiteSpace(order.ClientEmail) ? "—" : order.ClientEmail);
        _clientInfo.AddSeparator();
        _clientInfo.Add("Remise", order.DeliveryTypeLabel);
        if (order.DeliveryType == DeliveryType.HomeDelivery)
        {
            _clientInfo.Add("Adresse", string.IsNullOrWhiteSpace(order.DeliveryAddress) ? "—" : order.DeliveryAddress);
        }

        bool late = order.DueDate.Date < DateTime.Today && order.Status is not (OrderStatus.Completed or OrderStatus.Cancelled);
        _dates.Clear();
        _dates.Add("Déposée le", Fmt.DateTime(order.CreatedAt));
        _dates.Add("Retrait prévu", Fmt.Date(order.DueDate) + (late ? " (en retard)" : string.Empty), late ? Theme.Red : null, late);
        if (order.ReadyNotifiedAt.HasValue)
        {
            _dates.Add("Client prévenu", Fmt.DateTime(order.ReadyNotifiedAt.Value));
        }

        if (order.CompletedAt.HasValue)
        {
            _dates.Add("Terminée le", Fmt.DateTime(order.CompletedAt.Value), Theme.Green);
        }

        if (order.CancelledAt.HasValue)
        {
            _dates.Add("Annulée le", Fmt.DateTime(order.CancelledAt.Value), Theme.Red);
            if (!string.IsNullOrWhiteSpace(order.CancelReason))
            {
                _dates.Add("Motif", order.CancelReason, Theme.Red);
            }
        }

        _dates.Add("Enregistrée par", order.CreatedByStaffName ?? "—");

        _amounts.Clear();
        _amounts.Add("Sous-total", Fmt.Money(order.Subtotal));
        if (order.ExpressTotal > 0)
        {
            _amounts.Add("Supplément express", Fmt.Money(order.ExpressTotal));
        }

        if (order.DeliveryFee > 0)
        {
            _amounts.Add("Livraison", Fmt.Money(order.DeliveryFee));
        }

        if (order.DiscountAmount > 0)
        {
            _amounts.Add("Remise", "− " + Fmt.Money(order.DiscountAmount), Theme.Green);
        }

        if (order.PromotionDiscount > 0)
        {
            _amounts.Add($"Promotion {order.PromotionCode}", "− " + Fmt.Money(order.PromotionDiscount), Theme.Green);
        }

        if (order.LoyaltyDiscount > 0)
        {
            _amounts.Add($"Fidélité ({order.LoyaltyPointsRedeemed} pts)", "− " + Fmt.Money(order.LoyaltyDiscount), Theme.Green);
        }

        _amounts.AddSeparator();
        _amounts.Add("Total", Fmt.Money(order.Total), null, true, 11f);
        _amounts.Add("Déjà payé", Fmt.Money(order.PaidAmount), Theme.Green);
        _amounts.Add("Reste à payer", order.Balance > 0 ? Fmt.Money(order.Balance) : "Soldé",
            order.Balance > 0 ? Theme.Amber : Theme.Green, true, 13f);

        _notes.Text = order.Notes ?? string.Empty;
        _notesCard.Visible = !string.IsNullOrWhiteSpace(order.Notes);
        UpdateButtons();
        _body.PerformLayout();
    }

    private void FitHeights(OrderDto order)
    {
        int toolbar = _itemTools.GetPreferredSize(new Size(Math.Max(Theme.S(300), _itemsCard.Width - Theme.S(40)), 0)).Height;
        FitHeight(_itemsCard, _itemsGrid, order.Items.Count, toolbar + Theme.S(4));
        FitHeight(_paymentsCard, _paymentsGrid, order.Payments.Count);
        FitHeight(_pickupsCard, _pickupsGrid, order.Pickups.Count);
    }

    private static void FitHeight(TitledCard card, GridCard grid, int rows, int extra = 0)
    {
        int shown = Math.Max(rows, 1);
        card.Height = Theme.S(18) + Theme.S(34) + Theme.S(12) + grid.Grid.ColumnHeadersHeight + shown * grid.Grid.RowTemplate.Height + Theme.S(20) + extra;
    }

    private void UpdateButtons()
    {
        OrderDto? o = _order;
        bool cancelled = o?.Status == OrderStatus.Cancelled;
        _pay.Enabled = o is not null && !cancelled && o.Balance > 0;
        _pickup.Enabled = o is not null && !cancelled && o.Items.Any(i => i.Status == ItemStatus.Ready && i.RemainingQuantity > 0);
        _receipt.Enabled = o is not null;
        _email.Enabled = o is not null && !string.IsNullOrWhiteSpace(o.ClientEmail);
        _refund.Enabled = o is not null && o.PaidAmount > 0;
        _cancel.Enabled = o is not null && o.Status is not (OrderStatus.Cancelled or OrderStatus.Completed);
        _bulkApply.Enabled = o is not null && !cancelled && o.Status != OrderStatus.Completed && o.Items.Any(i => i.Status is not (ItemStatus.PickedUp or ItemStatus.Cancelled));
        _bulkStatus.Enabled = _bulkApply.Enabled;
        UpdateItemButtons();
    }

    private void UpdateItemButtons()
    {
        ItemRow? row = _itemsGrid.Grid.SelectedItem<ItemRow>();
        bool editable = row is not null && _order is { Status: not (OrderStatus.Cancelled or OrderStatus.Completed) } &&
                        row.Source.Status is not (ItemStatus.PickedUp or ItemStatus.Cancelled);
        _setProcessing.Enabled = editable && row!.Source.Status != ItemStatus.InProgress;
        _setReady.Enabled = editable && row!.Source.Status != ItemStatus.Ready;
    }

    // ---- Actions -------------------------------------------------------------------------
    private async Task ReloadAsync()
    {
        OrderDto? order = await Api.GetOrderAsync(_orderId);
        if (Alive && order is not null)
        {
            Bind(order);
        }
    }

    private async Task PayAsync()
    {
        if (_order is null)
        {
            return;
        }

        using PaymentDialog dialog = new(Api, _order);
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Host.Toast($"Paiement de {Fmt.Money(dialog.Result?.Amount ?? 0)} encaissé.");
            await RunAsync(ReloadAsync);
        }
    }

    private async Task PickupAsync()
    {
        if (_order is null)
        {
            return;
        }

        using PickupDialog dialog = new(Api, _order);
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Host.Toast("Retrait enregistré.");
            if (dialog.Result is not null && Alive)
            {
                Bind(dialog.Result);
            }
            else
            {
                await RunAsync(ReloadAsync);
            }
        }
    }

    private async Task RefundAsync()
    {
        if (_order is null)
        {
            return;
        }

        using RefundDialog dialog = new(Api, _order);
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            Host.Toast("Remboursement enregistré.");
            await RunAsync(ReloadAsync);
        }
    }

    private async Task CancelAsync()
    {
        if (_order is null)
        {
            return;
        }

        string? reason = AppDialogs.Prompt(this, "Annuler la commande",
            "Motif de l'annulation", "Annuler la commande",
            $"{_order.TicketNumber} sera définitivement annulée. Cette action ne peut pas être défaite.",
            required: true, multiline: true, danger: true);
        if (reason is null)
        {
            return;
        }

        bool ok = await RunAsync(async () =>
        {
            OrderDto? updated = await Api.CancelOrderAsync(_orderId, reason);
            if (updated is not null && Alive)
            {
                Bind(updated);
            }
        }, "Annulation impossible");
        if (ok)
        {
            Host.Toast("Commande annulée.", MessageKind.Warning);
        }
    }

    private async Task OpenReceiptAsync()
    {
        if (_order is not null)
        {
            await ReceiptService.OpenAsync(Api, _order.Id, _order.TicketNumber);
        }
    }

    private async Task EmailAsync()
    {
        if (_order is null)
        {
            return;
        }

        string? message = null;
        bool ok = await RunAsync(async () => message = (await Api.EmailReceiptAsync(_order.Id))?.Message, "Envoi du reçu impossible");
        if (ok)
        {
            Host.Toast(message ?? "Reçu envoyé par e-mail.");
        }
    }

    private async Task SetSelectedStatusAsync(ItemStatus status)
    {
        ItemRow? row = _itemsGrid.Grid.SelectedItem<ItemRow>();
        if (row is null || _order is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            OrderDto? updated = await Api.UpdateItemStatusAsync(_order.Id, row.Id, status);
            if (updated is not null && Alive)
            {
                Bind(updated);
            }
        }, "Changement de statut impossible");
    }

    private async Task BulkStatusAsync()
    {
        if (_order is null)
        {
            return;
        }

        ItemStatus status = _bulkStatus.SelectedValue<ItemStatus>();
        await RunAsync(async () =>
        {
            OrderDto? updated = await Api.UpdateOrderStatusAsync(_order.Id, status);
            if (updated is not null && Alive)
            {
                Bind(updated);
            }
        }, "Changement de statut impossible");
    }

    public override bool HandleShortcut(Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Host.GoBack();
            return true;
        }

        return base.HandleShortcut(keyData);
    }
}
