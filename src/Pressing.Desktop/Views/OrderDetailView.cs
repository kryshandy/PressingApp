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
    private readonly PageHeader _header = new("Commande", "Chargement…");
    private readonly PillLabel _statusPill = new();
    private readonly PillLabel _expressPill = new() { Text = "EXPRESS", PillColor = Theme.Amber, Visible = false };
    private readonly FigureBlock _balance = new("Reste à payer", 24f);

    private readonly FlatButton _pay = new() { Text = "Encaisser", Kind = ButtonKind.Primary, IconGlyph = Icons.Money };
    private readonly FlatButton _pickup = new() { Text = "Retrait", Kind = ButtonKind.Secondary, IconGlyph = Icons.Pickup };
    private readonly FlatButton _receipt = new() { Text = "Reçu PDF", Kind = ButtonKind.Secondary, IconGlyph = Icons.Pdf };
    private readonly FlatButton _labels = new() { Text = "Étiquettes", Kind = ButtonKind.Secondary, IconGlyph = Icons.Print };
    private readonly FlatButton _thermal = new() { Text = "Ticket 80 mm", Kind = ButtonKind.Secondary, IconGlyph = Icons.Print };
    private readonly FlatButton _email = new() { Text = "Envoyer par e-mail", Kind = ButtonKind.Secondary, IconGlyph = Icons.Mail };
    private readonly FlatButton _refund = new() { Text = "Rembourser", Kind = ButtonKind.Secondary, IconGlyph = Icons.Refund };
    private readonly FlatButton _cancel = new() { Text = "Annuler la commande", Kind = ButtonKind.Danger, IconGlyph = Icons.Cancel };

    private readonly GridCard _itemsGrid = new("Aucun article.");
    private readonly TitledCard _itemsCard;
    private readonly FlatButton _setProcessing = new() { Text = "En traitement", Kind = ButtonKind.Secondary };
    private readonly FlatButton _setReady = new() { Text = "Prêt", Kind = ButtonKind.Secondary, IconGlyph = Icons.Check };
    private readonly SelectBox _bulkStatus = new() { Width = Theme.S(160), Height = Theme.S(32) };
    private readonly FlatButton _bulkApply = new() { Text = "Appliquer", Kind = ButtonKind.Secondary };
    private readonly GridCard _paymentsGrid = new("Aucun paiement enregistré.");
    private readonly TitledCard _paymentsCard;
    private readonly GridCard _pickupsGrid = new("Aucun retrait pour le moment.");
    private readonly TitledCard _pickupsCard;
    private readonly InfoList _clientInfo = new();
    private readonly InfoList _dates = new();
    private readonly InfoList _amounts = new();
    private readonly TextLabel _clientLink = new();
    private readonly TitledCard _notesCard;
    private readonly TextLabel _notes = new() { Wrap = true, ForeColor = Theme.Text, Font = Theme.F(10f) };

    public OrderDetailView(IAppHost host, int orderId) : base(host)
    {
        _orderId = orderId;

        // ----- En-tête : Retour sur la gouttière, ticket + pastilles, client et dates, actions sur une ligne -----
        FlatButton back = new() { Text = "Retour", Kind = ButtonKind.Secondary, IconGlyph = Icons.Back };
        back.Click += (_, _) => Host.GoBack();
        _header.SetLeading(back);
        _header.AddTitleAdornment(_statusPill);
        _header.AddTitleAdornment(_expressPill);
        List<FlatButton> actions = new() { _email, _labels, _thermal, _receipt };
        if (Session.IsManagerOrAdmin)
        {
            actions.Insert(0, _cancel);
            actions.Insert(1, _refund);
        }

        actions.Add(_pickup);
        actions.Add(_pay);
        foreach (FlatButton b in actions)
        {
            _header.Actions.Add(b);
        }

        // ----- Articles -----
        ThemedGrid items = _itemsGrid.Grid;
        items.AddText("article", "ARTICLE", nameof(ItemRow.Article), 55, minWidth: 150);
        items.AddText("service", "SERVICE", nameof(ItemRow.Service), 45, minWidth: 120);
        items.AddText("qty", "QTÉ", nameof(ItemRow.Quantity), right: true, fixedWidth: 64);
        items.AddText("unit", "PRIX UNIT.", nameof(ItemRow.UnitPrice), right: true, fixedWidth: 108);
        items.AddText("total", "TOTAL", nameof(ItemRow.Total), right: true, fixedWidth: 108);
        items.AddPill("status", "STATUT", fixedWidth: 136);
        items.AddText("picked", "RETIRÉ", nameof(ItemRow.PickedUp), right: true, fixedWidth: 84);
        items.SetOptional("picked", "unit", "service");
        items.SelectionChanged += (_, _) => UpdateItemButtons();

        _bulkStatus.SetChoices(new[]
        {
            new Choice<ItemStatus>(ItemStatus.Deposited, "Déposé"),
            new Choice<ItemStatus>(ItemStatus.InProgress, "En traitement"),
            new Choice<ItemStatus>(ItemStatus.Ready, "Prêt")
        }, ItemStatus.Ready);
        ActionBar itemTools = new(alignRight: false) { ButtonHeight = Theme.S(32), MaxVisible = 10 };
        TextLabel allLabel = new() { Text = "Passer tous les articles à :", Font = Theme.F(9.5f), ForeColor = Theme.TextMuted, AutoSize = true, Height = Theme.S(20) };
        foreach (Control c in new Control[] { allLabel, _bulkStatus, _bulkApply })
        {
            itemTools.Add(c);
        }

        _itemsGrid.AutoHeightRows = 2;
        _itemsGrid.MaxRows = 10;
        VStack itemsContent = new VStack(16)
            .Add(new Padded(itemTools, new Padding(Theme.CardPadding - 1, 0, Theme.CardPadding - 1, 0)))
            .Add(_itemsGrid);
        _itemsCard = new TitledCard("Articles", itemsContent, "Sélectionnez un article pour changer son statut", flushContent: true);
        _itemsCard.AddAction(_setProcessing);
        _itemsCard.AddAction(_setReady);

        // ----- Paiements / retraits -----
        ThemedGrid pay = _paymentsGrid.Grid;
        pay.AddText("date", "DATE", nameof(PaymentRow.Date), fixedWidth: 148);
        pay.AddText("type", "TYPE", nameof(PaymentRow.Type), 25, minWidth: 100);
        pay.AddText("method", "MODE", nameof(PaymentRow.Method), 25, minWidth: 110);
        pay.AddText("reference", "RÉFÉRENCE", nameof(PaymentRow.Reference), 30, minWidth: 90);
        pay.AddText("staff", "PAR", nameof(PaymentRow.Staff), 20, minWidth: 90);
        pay.AddText("amount", "MONTANT", nameof(PaymentRow.Amount), right: true, fixedWidth: 120);
        pay.SetOptional("reference", "staff");
        _paymentsGrid.AutoHeightRows = 1;
        _paymentsGrid.MaxRows = 8;
        _paymentsCard = new TitledCard("Paiements", _paymentsGrid, null, flushContent: true);

        ThemedGrid pick = _pickupsGrid.Grid;
        pick.AddText("when", "DATE", nameof(PickupRow.When), fixedWidth: 148);
        pick.AddText("items", "ARTICLES REMIS", nameof(PickupRow.Items), 50, minWidth: 160);
        pick.AddText("staff", "PAR", nameof(PickupRow.Staff), 20, minWidth: 90);
        pick.AddText("note", "NOTE", nameof(PickupRow.Note), 30, minWidth: 90);
        pick.SetOptional("note", "staff");
        _pickupsGrid.AutoHeightRows = 1;
        _pickupsGrid.MaxRows = 8;
        _pickupsCard = new TitledCard("Retraits", _pickupsGrid, null, flushContent: true);

        VStack left = new VStack().Add(_itemsCard).Add(_paymentsCard).Add(_pickupsCard);

        // ----- Colonne de droite : carte Résumé -----
        _clientLink.Font = Theme.Semi(12f);
        _clientLink.ForeColor = Theme.Text;
        _clientLink.Cursor = Cursors.Hand;
        _clientLink.MouseEnter += (_, _) => _clientLink.ForeColor = Theme.Accent;
        _clientLink.MouseLeave += (_, _) => _clientLink.ForeColor = Theme.Text;
        _clientLink.Click += (_, _) =>
        {
            if (_order is not null)
            {
                Host.OpenClient(_order.ClientId);
            }
        };
        VStack summary = new VStack(16)
            .Add(_clientLink)
            .Add(_clientInfo)
            .Add(new SectionRule())
            .Add(_amounts)
            .Add(_balance);
        TitledCard summaryCard = new("Résumé", summary, null, false, true);
        TitledCard datesCard = new("Chronologie", _dates, null, false, true);
        _notesCard = new TitledCard("Notes", _notes, null, false, true) { Visible = false };
        VStack right = new VStack().Add(summaryCard).Add(datesCard).Add(_notesCard);

        // 8 + 4 ; la colonne Résumé reste visible pendant le défilement.
        GridRow body = new GridRow { StackBelow = 900 }.Add(left, 8).Add(right, 4).Sticky(right);
        SetPage(_header, body, scroll: true);

        _pay.Click += async (_, _) => await PayAsync();
        _pickup.Click += async (_, _) => await PickupAsync();
        _receipt.Click += async (_, _) => await RunAsync(OpenReceiptAsync, "Reçu PDF");
        _thermal.Click += async (_, _) => await PrintThermalAsync();
        _labels.Click += async (_, _) => await RunAsync(OpenLabelsAsync, "Étiquettes");
        _email.Click += async (_, _) => await EmailAsync();
        _refund.Click += async (_, _) => await RefundAsync();
        _cancel.Click += async (_, _) => await CancelAsync();
        _setProcessing.Click += async (_, _) => await SetSelectedStatusAsync(ItemStatus.InProgress);
        _setReady.Click += async (_, _) => await SetSelectedStatusAsync(ItemStatus.Ready);
        _bulkApply.Click += async (_, _) => await BulkStatusAsync();
        UpdateButtons();
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

        _header.TitleText = order.TicketNumber;
        _header.Subtitle = $"{order.ClientName} · déposée le {Fmt.Date(order.CreatedAt)} · retrait le {Fmt.Date(order.DueDate)}";
        _statusPill.Text = order.StatusLabel;
        _statusPill.PillColor = Theme.StatusColor(order.Status);
        _expressPill.Visible = order.IsExpress;
        _header.PerformLayout();

        _itemsGrid.Grid.SetRows(order.Items.Select(i => new ItemRow(i)).ToList());
        _itemsGrid.UpdateEmpty();
        _paymentsGrid.Grid.SetRows(order.Payments.OrderByDescending(p => p.PaidAt).Select(p => new PaymentRow(p)).ToList());
        _paymentsGrid.UpdateEmpty();
        _pickupsGrid.Grid.SetRows(order.Pickups.OrderByDescending(p => p.PickedUpAt).Select(p => new PickupRow(p)).ToList());
        _pickupsGrid.UpdateEmpty();
        _pickupsCard.Visible = order.Pickups.Count > 0 || order.Status is OrderStatus.Ready or OrderStatus.PartiallyPickedUp or OrderStatus.Completed;

        _clientLink.Text = order.ClientName;
        _clientInfo.Clear();
        _clientInfo.Add("Téléphone", order.ClientPhone);
        _clientInfo.Add("E-mail", string.IsNullOrWhiteSpace(order.ClientEmail) ? "—" : order.ClientEmail);
        _clientInfo.AddSeparator();
        _clientInfo.Add("Échéance", Fmt.Date(order.DueDate) + (order.DueDate.Date < DateTime.Today && order.Status is not (OrderStatus.Completed or OrderStatus.Cancelled) ? " (en retard)" : string.Empty));
        _clientInfo.Add("Express", order.IsExpress ? "Oui" : "Non", order.IsExpress ? Theme.Amber : null);
        _clientInfo.Add("Livraison", order.DeliveryTypeLabel);
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
        _balance.Value.Text = order.Balance > 0 ? Fmt.Money(order.Balance) : "Soldé";
        _balance.Value.ForeColor = order.Balance > 0 ? Theme.Accent : Theme.Green;

        _notes.Text = order.Notes ?? string.Empty;
        _notesCard.Visible = !string.IsNullOrWhiteSpace(order.Notes);
        UpdateButtons();
        _notes.RequestPageLayout();
    }

    private void UpdateButtons()
    {
        OrderDto? o = _order;
        bool cancelled = o?.Status == OrderStatus.Cancelled;
        _pay.Enabled = o is not null && !cancelled && o.Balance > 0;
        _pickup.Enabled = o is not null && !cancelled && o.Items.Any(i => i.Status == ItemStatus.Ready && i.RemainingQuantity > 0);
        _receipt.Enabled = o is not null;
        _thermal.Enabled = o is not null;
        _labels.Enabled = o is not null && o.Status != OrderStatus.Cancelled;
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

    private async Task PrintThermalAsync()
    {
        if (_order is null)
        {
            return;
        }

        TicketPrintOutcome outcome = TicketPrintOutcome.NotConfigured;
        string printer = string.Empty;
        bool ok = await RunAsync(async () => (outcome, printer) = await TicketPrintService.PrintAsync(Api, _order.Id, _order.TicketNumber),
            "Ticket 80 mm");
        if (!ok || !Alive)
        {
            return;
        }

        switch (outcome)
        {
            case TicketPrintOutcome.Printed:
                Host.Toast($"Ticket envoyé à l'imprimante « {printer} ».");
                break;
            case TicketPrintOutcome.Skipped:
                Host.Toast("Mode autotest : ticket généré, impression ignorée.", MessageKind.Info);
                break;
            default:
                AppDialogs.Info(this, "Imprimante de tickets non configurée", TicketPrintService.HowToConfigure, MessageKind.Info);
                break;
        }
    }

    private async Task OpenLabelsAsync()
    {
        if (_order is not null)
        {
            await LabelFileService.OpenAsync(Api, _order.Id, _order.TicketNumber);
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
