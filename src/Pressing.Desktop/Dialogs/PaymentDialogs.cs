using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Desktop.Ui;

namespace Pressing.Desktop.Dialogs;

/// <summary>Encaissement d'un paiement sur une commande.</summary>
public sealed class PaymentDialog : DialogForm
{
    private readonly PressingApiClient _api;
    private readonly OrderDto _order;
    private readonly InputBox _amount;
    private readonly SelectBox _method;
    private readonly InputBox _reference;
    private readonly InputBox _note;

    public PaymentDialog(PressingApiClient api, OrderDto order)
        : base("Encaisser un paiement", $"{order.TicketNumber} · {order.ClientName}", 540, 560)
    {
        _api = api;
        _order = order;
        Primary.Text = "Encaisser";
        Primary.IconGlyph = Icons.Check;

        _amount = new InputBox { Suffix = "FCFA", TextAlign = HorizontalAlignment.Right, SelectAllOnFocus = true, AmountValue = Math.Max(0, order.Balance) };
        _method = new SelectBox();
        _method.SetChoices(Choices.PaymentMethods(), PaymentMethod.Cash);
        _reference = new InputBox { PlaceholderText = "N° de transaction, chèque…" };
        _note = new InputBox { PlaceholderText = "Facultatif" };

        FormGrid grid = new();
        grid.AddField("Montant encaissé", _amount, 2);
        grid.AddField("Mode de paiement", _method);
        grid.AddField("Référence", _reference);
        grid.AddField("Note", _note, 2);
        Body.Controls.Add(grid);
        Body.Controls.Add(Summary.Strip(
            ("Total", Fmt.Money(order.Total), null),
            ("Déjà payé", Fmt.Money(order.PaidAmount), Theme.Green),
            ("Reste à payer", Fmt.Money(order.Balance), order.Balance > 0 ? Theme.Amber : Theme.Green)));

        Primary.Click += async (_, _) => await SubmitAsync();
        Shown += (_, _) => _amount.Focus();
    }

    public PaymentDto? Result { get; private set; }

    private async Task SubmitAsync()
    {
        decimal amount = _amount.AmountValue;
        if (amount <= 0)
        {
            ShowError("Saisissez un montant supérieur à 0.");
            _amount.Focus();
            return;
        }

        ShowError(null);
        bool ok = await UiRun.RunAsync(this, async () =>
        {
            Result = await _api.AddPaymentAsync(_order.Id, new AddPaymentRequest(
                amount, _method.SelectedValue<PaymentMethod>(), Clean(_reference.Text), Clean(_note.Text), null));
        }, "Encaissement impossible");
        if (ok)
        {
            DialogResult = DialogResult.OK;
        }
    }

    private static string? Clean(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// <summary>Remboursement (Manager / Administrateur).</summary>
public sealed class RefundDialog : DialogForm
{
    private readonly PressingApiClient _api;
    private readonly OrderDto _order;
    private readonly InputBox _amount;
    private readonly SelectBox _method;
    private readonly InputBox _note;

    public RefundDialog(PressingApiClient api, OrderDto order)
        : base("Rembourser", $"{order.TicketNumber} · {order.ClientName}", 540, 520)
    {
        _api = api;
        _order = order;
        Primary.Text = "Rembourser";
        Primary.Kind = ButtonKind.Danger;

        _amount = new InputBox { Suffix = "FCFA", TextAlign = HorizontalAlignment.Right, SelectAllOnFocus = true, AmountValue = Math.Max(0, order.PaidAmount) };
        _method = new SelectBox();
        _method.SetChoices(Choices.PaymentMethods(), PaymentMethod.Cash);
        _note = new InputBox { PlaceholderText = "Motif du remboursement (obligatoire)" };

        FormGrid grid = new();
        grid.AddField("Montant à rembourser", _amount, 2, $"Maximum : {Fmt.Money(order.PaidAmount)}");
        grid.AddField("Mode de remboursement", _method, 2);
        grid.AddField("Motif", _note, 2);
        Body.Controls.Add(grid);
        Body.Controls.Add(Summary.Strip(
            ("Total", Fmt.Money(order.Total), null),
            ("Encaissé", Fmt.Money(order.PaidAmount), Theme.Green)));

        Primary.Click += async (_, _) => await SubmitAsync();
        Shown += (_, _) => _amount.Focus();
    }

    public PaymentDto? Result { get; private set; }

    private async Task SubmitAsync()
    {
        decimal amount = _amount.AmountValue;
        if (amount <= 0 || amount > _order.PaidAmount)
        {
            ShowError(amount <= 0 ? "Saisissez un montant supérieur à 0." : "Le montant dépasse les sommes encaissées.");
            _amount.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(_note.Text))
        {
            ShowError("Le motif est obligatoire.");
            _note.Focus();
            return;
        }

        ShowError(null);
        bool ok = await UiRun.RunAsync(this, async () =>
        {
            Result = await _api.RefundAsync(_order.Id, new RefundRequest(amount, _method.SelectedValue<PaymentMethod>(), _note.Text.Trim()));
        }, "Remboursement impossible");
        if (ok)
        {
            DialogResult = DialogResult.OK;
        }
    }
}

/// <summary>Retrait d'articles prêts, avec encaissement éventuel du solde.</summary>
public sealed class PickupDialog : DialogForm
{
    private sealed class Line
    {
        public required OrderItemDto Item { get; init; }
        public required ToggleBox Check { get; init; }
        public required StepperBox Stepper { get; init; }
    }

    private readonly PressingApiClient _api;
    private readonly OrderDto _order;
    private readonly List<Line> _lines = new();
    private readonly InputBox _amount;
    private readonly SelectBox _method;
    private readonly InputBox _reference;
    private readonly InputBox _note;
    private readonly Label _remaining;

    public PickupDialog(PressingApiClient api, OrderDto order)
        : base("Retrait de commande", $"{order.TicketNumber} · {order.ClientName}", 700, 720)
    {
        _api = api;
        _order = order;
        Primary.Text = "Valider le retrait";
        Primary.IconGlyph = Icons.Check;

        TableLayoutPanel items = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            Margin = Padding.Empty
        };
        items.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        items.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        items.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(132)));

        int row = 0;
        foreach (OrderItemDto item in order.Items.Where(i => i.Status != ItemStatus.Cancelled))
        {
            bool ready = item.Status == ItemStatus.Ready && item.RemainingQuantity > 0;
            bool done = item.RemainingQuantity <= 0 || item.Status == ItemStatus.PickedUp;
            ToggleBox check = new()
            {
                Text = item.CategoryName + " — " + item.ServiceLabel,
                Enabled = ready,
                Checked = ready,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(0, Theme.S(8), Theme.S(12), Theme.S(8))
            };
            string state = done ? "Déjà retiré" : ready ? $"{item.RemainingQuantity} à retirer" : item.StatusLabel;
            Label status = new()
            {
                Text = state,
                AutoSize = true,
                ForeColor = ready ? Theme.Green : Theme.TextMuted,
                Font = Theme.F(9.5f),
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, Theme.S(12), 0)
            };
            StepperBox stepper = new()
            {
                Minimum = 1,
                Maximum = Math.Max(1, item.RemainingQuantity),
                Number = Math.Max(1, item.RemainingQuantity),
                Enabled = ready,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Height = Theme.S(36),
                Margin = new Padding(0, Theme.S(4), 0, Theme.S(4))
            };
            items.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            items.Controls.Add(check, 0, row);
            items.Controls.Add(status, 1, row);
            items.Controls.Add(stepper, 2, row);
            if (ready)
            {
                check.CheckedChanged += (_, _) => stepper.Enabled = check.Checked;
                _lines.Add(new Line { Item = item, Check = check, Stepper = stepper });
            }

            row++;
        }

        RoundedPanel itemsCard = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(Theme.S(16), Theme.S(6), Theme.S(16), Theme.S(6)),
            Margin = new Padding(0, 0, 0, Theme.S(16))
        };
        itemsCard.Controls.Add(items);
        Panel itemsWrap = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0, 0, 0, Theme.S(12)) };
        itemsCard.Dock = DockStyle.Top;
        itemsWrap.Controls.Add(itemsCard);

        _amount = new InputBox { Suffix = "FCFA", TextAlign = HorizontalAlignment.Right, SelectAllOnFocus = true, AmountValue = Math.Max(0, order.Balance) };
        _method = new SelectBox();
        _method.SetChoices(Choices.PaymentMethods(), PaymentMethod.Cash);
        _reference = new InputBox { PlaceholderText = "Facultatif" };
        _note = new InputBox { PlaceholderText = "Facultatif" };
        _remaining = new Label { Dock = DockStyle.Top, AutoSize = true, Font = Theme.F(9.5f), ForeColor = Theme.TextMuted, Padding = new Padding(0, 0, 0, Theme.S(10)) };
        _amount.TextChanged += (_, _) => UpdateRemaining();
        UpdateRemaining();

        FormGrid grid = new();
        grid.AddField("Montant encaissé à ce retrait", _amount, 1, null);
        grid.AddField("Mode de paiement", _method);
        grid.AddField("Référence", _reference);
        grid.AddField("Note", _note);

        Body.Controls.Add(grid);
        Body.Controls.Add(_remaining);
        Body.Controls.Add(itemsWrap);
        Body.Controls.Add(new Label
        {
            Text = "ARTICLES À REMETTRE AU CLIENT",
            Dock = DockStyle.Top,
            Height = Theme.S(30),
            Font = Theme.F(8.5f, FontStyle.Bold),
            ForeColor = Theme.TextMuted,
            TextAlign = ContentAlignment.BottomLeft
        });
        Body.Controls.Add(Summary.Strip(
            ("Total", Fmt.Money(order.Total), null),
            ("Payé", Fmt.Money(order.PaidAmount), Theme.Green),
            ("Reste à payer", Fmt.Money(order.Balance), order.Balance > 0 ? Theme.Amber : Theme.Green)));

        Primary.Enabled = _lines.Count > 0;
        Primary.Click += async (_, _) => await SubmitAsync();
    }

    public OrderDto? Result { get; private set; }

    private void UpdateRemaining()
    {
        decimal left = _order.Balance - _amount.AmountValue;
        _remaining.Text = left > 0
            ? $"Il restera {Fmt.Money(left)} à payer après ce retrait."
            : "La commande sera entièrement réglée.";
        _remaining.ForeColor = left > 0 ? Theme.Amber : Theme.Green;
    }

    private async Task SubmitAsync()
    {
        List<PickupItemRequest> selected = _lines
            .Where(l => l.Check.Checked)
            .Select(l => new PickupItemRequest(l.Item.Id, l.Stepper.Number))
            .ToList();
        if (selected.Count == 0)
        {
            ShowError("Sélectionnez au moins un article prêt.");
            return;
        }

        ShowError(null);
        decimal amount = _amount.AmountValue;
        bool ok = await UiRun.RunAsync(this, async () =>
        {
            Result = await _api.PickupAsync(_order.Id, new PickupRequest(
                selected, Math.Max(0, amount), _method.SelectedValue<PaymentMethod>(),
                string.IsNullOrWhiteSpace(_reference.Text) ? null : _reference.Text.Trim(),
                string.IsNullOrWhiteSpace(_note.Text) ? null : _note.Text.Trim()));
        }, "Retrait impossible");
        if (ok)
        {
            DialogResult = DialogResult.OK;
        }
    }
}
