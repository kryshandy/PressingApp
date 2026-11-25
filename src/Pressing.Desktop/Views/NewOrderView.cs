using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Desktop.Dialogs;
using Pressing.Desktop.Services;
using Pressing.Desktop.Ui;

namespace Pressing.Desktop.Views;

/// <summary>
/// Nouveau dépôt (écran de caisse) : client, lignes, options, acompte, récapitulatif en direct puis confirmation.
/// La vue est conservée en mémoire : un dépôt en cours n'est pas perdu quand on change d'écran.
/// </summary>
public sealed class NewOrderView : BaseView
{
    private sealed class LineVm
    {
        public required CategoryDto Category { get; init; }
        public required ServiceType Service { get; init; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public bool Express { get; set; }
        public string? Description { get; set; }
        public CategoryPriceDto? Grid { get; init; }
    }

    private sealed class LineRow : IPillRow, ITintRow
    {
        public LineRow(LineVm line, bool globalExpress)
        {
            Line = line;
            GlobalExpress = globalExpress;
            Article = line.Category.Name + " — " + line.Service.Label() +
                      (string.IsNullOrWhiteSpace(line.Description) ? string.Empty : " (" + line.Description + ")");
            Quantity = line.Quantity.ToString();
            Unit = Fmt.Money(line.UnitPrice);
            decimal supplement = (line.Express || globalExpress) ? line.Grid?.ExpressSupplement ?? 0 : 0;
            Total = Fmt.Money((line.UnitPrice + supplement) * line.Quantity);
            Remove = Icons.Cancel;
        }

        public LineVm Line { get; }
        public bool GlobalExpress { get; }
        public string Article { get; }
        public string Quantity { get; }
        public string Unit { get; }
        public string Total { get; }
        public string Remove { get; }

        public (string Text, Color Color)? GetPill(string columnName) =>
            columnName == "express" && (Line.Express || GlobalExpress) ? ("EXPRESS", Theme.Amber) : null;

        public Color? GetTint(string columnName) => columnName switch
        {
            "remove" => Theme.TextFaint,
            "total" => Theme.Accent,
            _ => null
        };
    }

    // ---- État ----------------------------------------------------------------------------
    private IReadOnlyList<CategoryDto> _catalog = Array.Empty<CategoryDto>();
    private ClientDto? _client;
    private readonly List<LineVm> _lines = new();
    private OrderQuoteDto? _quote;
    private int _quoteVersion;
    private bool _dueTouched;
    private bool _settingDue;
    private bool _loaded;
    private LineVm? _editing;

    // ---- Client --------------------------------------------------------------------------
    private readonly InputBox _clientSearch = new() { IconGlyph = Icons.Search, PlaceholderText = "Rechercher un client : nom, prénom ou téléphone…" };
    private readonly GridCard _clientResults = new("Aucun client trouvé.") { Visible = false };
    private readonly RoundedPanel _clientChip = new() { Visible = false, BackColor = Theme.SurfaceAlt };
    private readonly Label _clientName = new() { AutoSize = true, Font = Theme.F(12f, FontStyle.Bold), ForeColor = Theme.Text };
    private readonly Label _clientMeta = new() { AutoSize = true, Font = Theme.F(9.5f), ForeColor = Theme.TextMuted };
    private readonly System.Windows.Forms.Timer _clientDebounce = new() { Interval = 300 };
    private TableLayoutPanel _clientSearchRow = null!;
    private int _clientSearchVersion;

    // ---- Ajout de ligne ------------------------------------------------------------------
    private readonly SelectBox _category = new();
    private readonly SelectBox _service = new();
    private readonly StepperBox _quantity = new() { Minimum = 1, Maximum = 500 };
    private readonly InputBox _price = new() { Suffix = "FCFA", TextAlign = HorizontalAlignment.Right, SelectAllOnFocus = true };
    private readonly ToggleBox _lineExpress = new() { Text = "Express" };
    private readonly InputBox _description = new() { PlaceholderText = "Description / remarque (taches, couleur…)" };
    private readonly FlatButton _addLine = new() { Text = "Ajouter", Kind = ButtonKind.Primary, IconGlyph = Icons.Add };
    private readonly Label _priceHint = new() { AutoSize = true, ForeColor = Theme.TextFaint, Font = Theme.F(8.5f), Margin = new Padding(0, Theme.S(4), 0, 0) };
    private readonly GridCard _linesGrid = new("Aucun article : choisissez une catégorie puis cliquez sur « Ajouter ».");

    // ---- Options -------------------------------------------------------------------------
    private readonly ToggleBox _globalExpress = new() { Text = "Dépôt express (tous les articles)" };
    private readonly DateBox _due = new();
    private readonly SelectBox _delivery = new();
    private readonly InputBox _address = new() { PlaceholderText = "Adresse de livraison" };
    private readonly InputBox _fee = new() { Suffix = "FCFA", TextAlign = HorizontalAlignment.Right, SelectAllOnFocus = true, Text = "0" };
    private readonly InputBox _discount = new() { Suffix = "FCFA", TextAlign = HorizontalAlignment.Right, SelectAllOnFocus = true, Text = "0" };
    private readonly InputBox _promo = new() { PlaceholderText = "Ex. BIENVENUE10", CharacterCasingUpper = true };
    private readonly FlatButton _promoCheck = new() { Text = "Vérifier", Kind = ButtonKind.Secondary, Height = Theme.S(40) };
    private readonly Label _promoMessage = new() { AutoSize = true, Font = Theme.F(9f), Margin = new Padding(0, Theme.S(4), 0, 0) };
    private readonly InputBox _points = new() { TextAlign = HorizontalAlignment.Right, SelectAllOnFocus = true, Text = "0", Suffix = "pts" };
    private readonly InputBox _notes = new() { Multiline = true, PlaceholderText = "Notes internes sur le dépôt" };
    private readonly InputBox _deposit = new() { Suffix = "FCFA", TextAlign = HorizontalAlignment.Right, SelectAllOnFocus = true, Text = "0" };
    private readonly SelectBox _depositMethod = new();
    private readonly InputBox _depositRef = new() { PlaceholderText = "Réf. transaction (facultatif)" };
    private readonly ToggleBox _payAll = new() { Text = "Payer la totalité maintenant" };
    private FieldBox _addressField = null!;
    private FieldBox _feeField = null!;
    private FieldBox _pointsField = null!;
    private readonly System.Windows.Forms.Timer _quoteTimer = new() { Interval = 300 };

    // ---- Récapitulatif -------------------------------------------------------------------
    private Label _sumSubtotal = null!;
    private Label _sumExpress = null!;
    private Label _sumDelivery = null!;
    private Label _sumDiscounts = null!;
    private Label _sumTotal = null!;
    private Label _sumDeposit = null!;
    private Label _sumBalance = null!;
    private readonly Label _sumError = new() { AutoSize = true, MaximumSize = new Size(Theme.S(340), 0), ForeColor = Theme.Red, Font = Theme.F(9f), Margin = new Padding(0, Theme.S(4), 0, Theme.S(6)) };
    private readonly FlatButton _submit = new()
    {
        Text = "Valider le dépôt",
        Kind = ButtonKind.Primary,
        IconGlyph = Icons.Check,
        AutoSize = false,
        Height = Theme.S(48),
        Dock = DockStyle.Fill,
        Font = Theme.F(11.5f, FontStyle.Bold),
        Margin = Padding.Empty,
        Enabled = false
    };

    // ---- Confirmation --------------------------------------------------------------------
    private readonly Panel _confirmPanel = new() { Dock = DockStyle.Fill, Visible = false, BackColor = Theme.Background };
    private readonly Label _confirmTicket = new() { AutoSize = true, Font = Theme.F(40f, FontStyle.Bold), ForeColor = Theme.Accent, Anchor = AnchorStyles.None };
    private readonly Label _confirmInfo = new() { AutoSize = true, Font = Theme.F(11f), ForeColor = Theme.Text, Anchor = AnchorStyles.None, TextAlign = ContentAlignment.MiddleCenter };
    private readonly Label _confirmDetail = new() { AutoSize = true, Font = Theme.F(10f), ForeColor = Theme.TextMuted, Anchor = AnchorStyles.None, TextAlign = ContentAlignment.MiddleCenter };
    private readonly FlatButton _confirmReceipt = new() { Text = "Imprimer / ouvrir le reçu PDF", Kind = ButtonKind.Primary, IconGlyph = Icons.Print };
    private readonly FlatButton _confirmEmail = new() { Text = "Envoyer le reçu par e-mail", Kind = ButtonKind.Secondary, IconGlyph = Icons.Mail };
    private readonly FlatButton _confirmOpen = new() { Text = "Voir la commande", Kind = ButtonKind.Secondary, IconGlyph = Icons.Orders };
    private readonly FlatButton _confirmNew = new() { Text = "Nouveau dépôt", Kind = ButtonKind.Info, IconGlyph = Icons.Add };
    private OrderDto? _createdOrder;
    private Control _formRoot = null!;

    public NewOrderView(IAppHost host) : base(host)
    {
        PageHeader header = new("Nouveau dépôt", "Saisie rapide : client, articles, acompte — Ctrl+Entrée pour valider");
        header.AddAction("Réinitialiser", ButtonKind.Secondary, Icons.Refresh, (_, _) => ConfirmReset());

        Control left = BuildLeft();
        Control right = BuildRight();

        TableLayoutPanel body = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(372)));
        body.Controls.Add(left, 0, 0);
        body.Controls.Add(right, 1, 0);

        _formRoot = UiKit.Rows((header, false), (body, true));
        BuildConfirmation();
        Controls.Add(_formRoot);
        Controls.Add(_confirmPanel);
        _confirmPanel.BringToFront();

        WireEvents();
        SetDeliveryUi();
        UpdateSubmitState();
    }

    public override string Title => "Nouveau dépôt";

    public override bool KeepAlive => true;

    public override void ApplyParameter(object? parameter)
    {
        if (parameter is ClientDto client)
        {
            if (_confirmPanel.Visible)
            {
                ResetAll();
            }

            SelectClient(client);
        }
    }

    // ==== Construction =====================================================================
    private Control BuildLeft()
    {
        // -- Client --
        _clientSearchRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        _clientSearchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _clientSearchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _clientSearch.Dock = DockStyle.Fill;
        _clientSearch.Margin = new Padding(0, 0, Theme.S(10), 0);
        FlatButton newClient = new() { Text = "Nouveau client", Kind = ButtonKind.Secondary, IconGlyph = Icons.Add, Margin = Padding.Empty };
        newClient.Click += (_, _) => CreateClient();
        _clientSearchRow.Controls.Add(_clientSearch, 0, 0);
        _clientSearchRow.Controls.Add(newClient, 1, 0);

        ThemedGrid results = _clientResults.Grid;
        results.AddText("name", "CLIENT", nameof(ClientRow.Name), 40, minWidth: 140);
        results.AddText("phone", "TÉLÉPHONE", nameof(ClientRow.Phone), 30, minWidth: 120);
        results.AddText("balance", "SOLDE DÛ", nameof(ClientRow.Balance), 30, true, 100);
        _clientResults.Height = Theme.S(38 + 36 * 4 + 4);
        _clientResults.Dock = DockStyle.Top;
        _clientResults.Margin = new Padding(0, Theme.S(10), 0, 0);
        _clientResults.BackColor = Theme.SurfaceAlt;

        _clientChip.Dock = DockStyle.Top;
        _clientChip.Height = Theme.S(72);
        _clientChip.Padding = new Padding(Theme.S(18), Theme.S(10), Theme.S(12), Theme.S(10));
        TableLayoutPanel chip = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        chip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        chip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        TableLayoutPanel chipText = new() { AutoSize = true, ColumnCount = 1, Anchor = AnchorStyles.Left };
        chipText.Controls.Add(_clientName, 0, 0);
        chipText.Controls.Add(_clientMeta, 0, 1);
        FlatButton change = new() { Text = "Changer", Kind = ButtonKind.Secondary, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
        change.Click += (_, _) => ClearClient();
        chip.Controls.Add(chipText, 0, 0);
        chip.Controls.Add(change, 1, 0);
        _clientChip.Controls.Add(chip);

        TableLayoutPanel clientBody = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = Padding.Empty };
        clientBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        clientBody.Controls.Add(_clientSearchRow, 0, 0);
        clientBody.Controls.Add(_clientResults, 0, 1);
        clientBody.Controls.Add(_clientChip, 0, 2);
        TitledCard clientCard = new("1 · Client", clientBody, null, false, true) { Margin = new Padding(0, 0, Theme.S(12), Theme.S(12)) };

        // -- Lignes --
        _category.Format = o => ((CategoryDto)o).Name;
        _category.Placeholder = "Catégorie…";
        _service.Format = o => ((CategoryPriceDto)o).ServiceLabel;
        _service.Placeholder = "Service…";
        _addLine.AutoSize = false;
        _addLine.Height = Theme.S(36);
        _addLine.Width = Theme.S(124);
        _addLine.Margin = Padding.Empty;

        TableLayoutPanel add = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 2, Margin = Padding.Empty };
        add.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        add.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        add.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(150)));
        add.Controls.Add(new FieldBox("Catégorie", _category), 0, 0);
        add.Controls.Add(new FieldBox("Service", _service), 1, 0);
        add.Controls.Add(new FieldBox("Quantité", _quantity), 2, 0);

        TableLayoutPanel add2 = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, Margin = Padding.Empty };
        add2.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(160)));
        add2.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        add2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        FieldBox priceField = new("Prix unitaire", _price);
        _lineExpress.Anchor = AnchorStyles.Left;
        _lineExpress.Margin = new Padding(0, Theme.S(8), Theme.S(18), 0);
        FieldBox descField = new("Description", _description);
        add2.Controls.Add(priceField, 0, 0);
        add2.Controls.Add(_lineExpress, 1, 0);
        add2.Controls.Add(descField, 2, 0);
        _lineExpress.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        _lineExpress.Margin = new Padding(Theme.S(4), 0, Theme.S(18), Theme.S(18));

        TableLayoutPanel addBlock = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = Padding.Empty };
        addBlock.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        addBlock.Controls.Add(add, 0, 0);
        addBlock.Controls.Add(add2, 0, 1);
        addBlock.Controls.Add(_priceHint, 0, 2);

        ThemedGrid grid = _linesGrid.Grid;
        grid.AddText("article", "ARTICLE", nameof(LineRow.Article), 44, minWidth: 160);
        grid.AddText("qty", "QTÉ", nameof(LineRow.Quantity), 9, true, 56);
        grid.AddText("unit", "PRIX UNIT.", nameof(LineRow.Unit), 18, true, 96);
        grid.AddText("total", "TOTAL", nameof(LineRow.Total), 18, true, 96);
        grid.AddPill("express", "", 14, 96);
        DataGridViewTextBoxColumn remove = grid.AddText("remove", "", nameof(LineRow.Remove), 6, true, 44);
        remove.DefaultCellStyle.Font = Theme.Icon(10f);
        remove.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        remove.ToolTipText = "Retirer la ligne";
        _linesGrid.Dock = DockStyle.Fill;
        _linesGrid.Margin = new Padding(0, Theme.S(8), 0, 0);
        Label hint = new()
        {
            Text = "Double-clic : modifier · Suppr : retirer · + / − : quantité",
            AutoSize = true,
            Font = Theme.F(8.5f),
            ForeColor = Theme.TextFaint,
            Margin = new Padding(0, Theme.S(8), 0, 0)
        };
        TableLayoutPanel linesBody = (TableLayoutPanel)UiKit.Rows((addBlock, false), (_linesGrid, true), (hint, false));
        TitledCard linesCard = new("2 · Articles", linesBody) { Margin = new Padding(0, 0, Theme.S(12), 0) };
        linesCard.AddAction(_addLine);

        return UiKit.Rows((clientCard, false), (linesCard, true));
    }

    private Control BuildRight()
    {
        _delivery.SetChoices(Enum.GetValues<DeliveryType>().Select(d => new Choice<DeliveryType>(d, d.Label())), DeliveryType.StorePickup);
        _depositMethod.SetChoices(Choices.PaymentMethods(), PaymentMethod.Cash);
        _due.Value = DateTime.Today.AddDays(2);

        _promoMessage.Text = string.Empty;
        TableLayoutPanel promoRow = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        promoRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        promoRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _promo.Dock = DockStyle.Fill;
        _promo.Margin = new Padding(0, 0, Theme.S(8), 0);
        _promoCheck.Margin = Padding.Empty;
        promoRow.Controls.Add(_promo, 0, 0);
        promoRow.Controls.Add(_promoCheck, 1, 0);
        TableLayoutPanel promoBlock = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, Margin = Padding.Empty };
        promoBlock.Controls.Add(promoRow, 0, 0);
        promoBlock.Controls.Add(_promoMessage, 0, 1);

        _notes.Height = Theme.S(76);
        FormGrid form = new();
        form.Add(_globalExpress, 2);
        _globalExpress.Margin = new Padding(0, 0, 0, Theme.S(12));
        form.AddField("Retrait prévu le", _due);
        form.AddField("Remise manuelle", _discount);
        form.AddField("Mode de remise", _delivery, 2);
        _addressField = form.AddField("Adresse de livraison", _address, 2);
        _feeField = form.AddField("Frais de livraison", _fee, 2);
        form.Add(new FieldBox("Code promo", promoBlock), 2);
        _pointsField = form.AddField("Points fidélité à utiliser", _points, 1, "Disponibles : —");
        form.AddField("Notes", _notes, 2);
        Label depositTitle = UiKit.Label("ACOMPTE", Theme.F(8.5f, FontStyle.Bold), Theme.Accent);
        depositTitle.Margin = new Padding(0, Theme.S(6), 0, Theme.S(8));
        form.Add(depositTitle, 2);
        form.AddField("Montant versé", _deposit);
        form.AddField("Mode de paiement", _depositMethod);
        form.AddField("Référence", _depositRef, 2);
        form.Add(_payAll, 2);

        TitledCard options = new("3 · Options et acompte", form, null, false, true) { Margin = new Padding(0, 0, 0, Theme.S(12)) };

        // Détail du montant (dans la zone défilante)
        InfoList details = new();
        _sumSubtotal = details.Add("Sous-total", "0 FCFA");
        _sumExpress = details.Add("Supplément express", "0 FCFA");
        _sumDelivery = details.Add("Livraison", "0 FCFA");
        _sumDiscounts = details.Add("Remises", "0 FCFA", Theme.Green);
        TitledCard detailsCard = new("Détail du montant", details, null, false, true) { Margin = new Padding(0, 0, 0, Theme.S(12)) };

        TableLayoutPanel stack = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Margin = Padding.Empty };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        stack.Controls.Add(options, 0, 0);
        stack.Controls.Add(detailsCard, 0, 1);
        ScrollPanel scroll = new();
        scroll.Controls.Add(stack);

        // Total, acompte et validation : toujours visibles en bas
        TableLayoutPanel totals = new() { ColumnCount = 2, RowCount = 2, Dock = DockStyle.Fill, Margin = Padding.Empty };
        totals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        totals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        totals.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        totals.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        Label MakeKey(string t, float size) => new() { Text = t, Dock = DockStyle.Fill, Font = Theme.F(size), ForeColor = Theme.TextMuted, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        Label MakeValue(Color c, float size) => new() { Dock = DockStyle.Fill, Font = Theme.F(size, FontStyle.Bold), ForeColor = c, TextAlign = ContentAlignment.MiddleRight, Text = "0 FCFA" };
        _sumTotal = MakeValue(Theme.Accent, 15f);
        _sumBalance = MakeValue(Theme.Amber, 11f);
        _sumDeposit = new Label();
        totals.Controls.Add(MakeKey("TOTAL", 12f), 0, 0);
        totals.Controls.Add(_sumTotal, 1, 0);
        totals.Controls.Add(MakeKey("Reste à payer", 10f), 0, 1);
        totals.Controls.Add(_sumBalance, 1, 1);
        TableLayoutPanel stickyBody = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        stickyBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        stickyBody.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(76)));
        stickyBody.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(22)));
        stickyBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        totals.Dock = DockStyle.Fill;
        stickyBody.Controls.Add(totals, 0, 0);
        _sumError.AutoSize = false;
        _sumError.MaximumSize = Size.Empty;
        _sumError.Dock = DockStyle.Fill;
        _sumError.AutoEllipsis = true;
        _sumError.Margin = Padding.Empty;
        _sumError.TextAlign = ContentAlignment.MiddleLeft;
        stickyBody.Controls.Add(_sumError, 0, 1);
        _submit.Dock = DockStyle.Fill;
        stickyBody.Controls.Add(_submit, 0, 2);
        RoundedPanel sticky = new()
        {
            Dock = DockStyle.Fill,
            Height = Theme.S(190),
            Padding = new Padding(Theme.S(20), Theme.S(12), Theme.S(20), Theme.S(14)),
            Margin = Padding.Empty,
            BorderColor = Theme.Mix(Theme.Border, Theme.Accent, 0.5f)
        };
        sticky.Controls.Add(stickyBody);

        return UiKit.Rows((scroll, true), (sticky, false));
    }

    private void BuildConfirmation()
    {
        TableLayoutPanel center = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Theme.Background
        };
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        center.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        TableLayoutPanel box = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Anchor = AnchorStyles.None,
            BackColor = Theme.Background
        };
        IconBadge badge = new(Icons.Check, Theme.Green, 76) { Anchor = AnchorStyles.None, Margin = new Padding(0, 0, 0, Theme.S(14)) };
        Label ok = UiKit.Label("Dépôt enregistré", Theme.F(16f, FontStyle.Bold), Theme.Text);
        ok.Anchor = AnchorStyles.None;
        Label caption = UiKit.Muted("NUMÉRO DE TICKET", 9f);
        caption.Anchor = AnchorStyles.None;
        caption.Margin = new Padding(0, Theme.S(18), 0, 0);
        _confirmTicket.Margin = new Padding(0, 0, 0, Theme.S(6));
        _confirmInfo.Margin = new Padding(0, Theme.S(6), 0, 0);
        _confirmDetail.Margin = new Padding(0, Theme.S(4), 0, Theme.S(22));

        FlowLayoutPanel buttons = new() { AutoSize = true, WrapContents = true, Anchor = AnchorStyles.None, Margin = Padding.Empty, MaximumSize = new Size(Theme.S(1100), 0) };
        foreach (FlatButton b in new[] { _confirmReceipt, _confirmEmail, _confirmOpen, _confirmNew })
        {
            b.Margin = new Padding(Theme.S(5), 0, Theme.S(5), Theme.S(10));
            buttons.Controls.Add(b);
        }

        box.Controls.Add(badge, 0, 0);
        box.Controls.Add(ok, 0, 1);
        box.Controls.Add(caption, 0, 2);
        box.Controls.Add(_confirmTicket, 0, 3);
        box.Controls.Add(_confirmInfo, 0, 4);
        box.Controls.Add(_confirmDetail, 0, 5);
        box.Controls.Add(buttons, 0, 6);
        center.Controls.Add(box, 0, 1);
        _confirmPanel.Controls.Add(center);

        _confirmReceipt.Click += async (_, _) =>
        {
            if (_createdOrder is { } o)
            {
                await RunAsync(async () => await ReceiptService.OpenAsync(Api, o.Id, o.TicketNumber), "Reçu PDF");
            }
        };
        _confirmEmail.Click += async (_, _) =>
        {
            if (_createdOrder is not { } o)
            {
                return;
            }

            string? message = null;
            bool done = await RunAsync(async () => message = (await Api.EmailReceiptAsync(o.Id))?.Message, "Envoi du reçu impossible");
            if (done)
            {
                Host.Toast(message ?? "Reçu envoyé par e-mail.");
            }
        };
        _confirmOpen.Click += (_, _) => { if (_createdOrder is { } o) Host.OpenOrder(o.Id); };
        _confirmNew.Click += (_, _) => ResetAll();
    }

    // ==== Événements =======================================================================
    private void WireEvents()
    {
        // Client
        _clientDebounce.Tick += async (_, _) =>
        {
            _clientDebounce.Stop();
            await SearchClientsAsync();
        };
        _clientSearch.TextChanged += (_, _) =>
        {
            _clientDebounce.Stop();
            if (_clientSearch.Text.Trim().Length >= 2)
            {
                _clientDebounce.Start();
            }
            else
            {
                _clientResults.Visible = false;
            }
        };
        _clientSearch.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Down && _clientResults.Visible && _clientResults.Grid.Rows.Count > 0)
            {
                _clientResults.Grid.Focus();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                _clientDebounce.Stop();
                _ = SearchClientsAsync();
            }
        };
        _clientResults.Grid.CellClick += (_, e) => PickClient(e.RowIndex);
        _clientResults.Grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                PickClient(_clientResults.Grid.CurrentRow?.Index ?? -1);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };

        // Ligne
        _category.SelectedIndexChanged += (_, _) => OnCategoryChanged();
        _service.SelectedIndexChanged += (_, _) => ApplyGridPrice();
        _lineExpress.CheckedChanged += (_, _) => UpdatePriceHint();
        _addLine.Click += (_, _) => AddOrUpdateLine();
        _price.KeyDown += (_, e) => EnterToAdd(e);
        _description.KeyDown += (_, e) => EnterToAdd(e);
        ThemedGrid grid = _linesGrid.Grid;
        grid.CellClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && grid.Columns[e.ColumnIndex].Name == "remove" && grid.Rows[e.RowIndex].DataBoundItem is LineRow row)
            {
                RemoveLine(row.Line);
            }
        };
        grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && grid.Columns[e.ColumnIndex].Name != "remove" && grid.Rows[e.RowIndex].DataBoundItem is LineRow row)
            {
                EditLine(row.Line);
            }
        };
        grid.KeyDown += (_, e) =>
        {
            if (grid.SelectedItem<LineRow>() is not { } row)
            {
                return;
            }

            switch (e.KeyCode)
            {
                case Keys.Delete:
                    RemoveLine(row.Line);
                    e.Handled = true;
                    break;
                case Keys.Add or Keys.Oemplus:
                    ChangeQuantity(row.Line, 1);
                    e.Handled = true;
                    break;
                case Keys.Subtract or Keys.OemMinus:
                    ChangeQuantity(row.Line, -1);
                    e.Handled = true;
                    break;
                case Keys.Enter:
                    EditLine(row.Line);
                    e.Handled = true;
                    break;
            }
        };

        // Options -> nouveau devis
        _globalExpress.CheckedChanged += (_, _) => { RefreshLinesGrid(); UpdateDueEstimate(); ScheduleQuote(); };
        _due.ValueChanged += (_, _) => { if (!_settingDue) _dueTouched = true; };
        _delivery.SelectedIndexChanged += (_, _) => { SetDeliveryUi(); ScheduleQuote(); };
        _fee.TextChanged += (_, _) => ScheduleQuote();
        _discount.TextChanged += (_, _) => ScheduleQuote();
        _promo.TextChanged += (_, _) => { _promoMessage.Text = string.Empty; ScheduleQuote(); };
        _points.TextChanged += (_, _) => ScheduleQuote();
        _deposit.TextChanged += (_, _) => UpdateBalance();
        _payAll.CheckedChanged += (_, _) => { _deposit.Enabled = !_payAll.Checked; SyncPayAll(); };
        _promoCheck.Click += async (_, _) => await CheckPromoAsync();
        _quoteTimer.Tick += async (_, _) =>
        {
            _quoteTimer.Stop();
            await RequestQuoteAsync();
        };
        _submit.Click += async (_, _) => await SubmitAsync();
    }

    private void EnterToAdd(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && !e.Control)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            AddOrUpdateLine();
        }
    }

    public override bool HandleShortcut(Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Enter) && _submit.Enabled && !_confirmPanel.Visible)
        {
            _ = SubmitAsync();
            return true;
        }

        return base.HandleShortcut(keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _clientDebounce.Dispose();
            _quoteTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    // ==== Chargement =======================================================================
    public override async Task LoadAsync()
    {
        _catalog = await Session.GetCatalogAsync(true);
        if (!Alive)
        {
            return;
        }

        _loaded = true;
        _category.SetItems(_catalog.Cast<object>(), _catalog.Count > 0 ? 0 : -1);
        OnCategoryChanged();
        if (_client is null)
        {
            _clientSearch.Focus();
        }
    }

    public override async Task ReactivateAsync()
    {
        if (!_confirmPanel.Visible)
        {
            if (_client is null)
            {
                _clientSearch.Focus();
            }
        }

        await Task.CompletedTask;
    }

    public override async Task RefreshAsync()
    {
        _catalog = await Session.GetCatalogAsync(true);
        if (!Alive)
        {
            return;
        }

        CategoryDto? current = _category.SelectedItem as CategoryDto;
        _category.SetItems(_catalog.Cast<object>(), Math.Max(0, _catalog.ToList().FindIndex(c => c.Id == current?.Id)));
        OnCategoryChanged();
        await RequestQuoteAsync();
    }

    // ==== Client ===========================================================================
    private async Task SearchClientsAsync()
    {
        string text = _clientSearch.Text.Trim();
        if (text.Length < 1)
        {
            _clientResults.Visible = false;
            return;
        }

        int version = ++_clientSearchVersion;
        try
        {
            List<ClientDto>? list = await Api.LookupClientsAsync(text, 8);
            if (!Alive || version != _clientSearchVersion)
            {
                return;
            }

            _clientResults.Grid.SetRows((list ?? new List<ClientDto>()).Select(c => new ClientRow(c)).ToList());
            _clientResults.UpdateEmpty();
            _clientResults.Visible = true;
            int rows = Math.Clamp(list?.Count ?? 0, 1, 5);
            _clientResults.Height = _clientResults.Grid.ColumnHeadersHeight + rows * _clientResults.Grid.RowTemplate.Height + Theme.S(4);
            if (list is { Count: > 0 })
            {
                _clientResults.Grid.ClearSelection();
                _clientResults.Grid.CurrentCell = _clientResults.Grid.Rows[0].Cells[0];
            }
        }
        catch (ApiException ex) when (!ex.IsUnauthorized)
        {
            _clientResults.Visible = false;
            Host.Toast(ex.Message, MessageKind.Error);
        }
    }

    private void PickClient(int rowIndex)
    {
        ThemedGrid grid = _clientResults.Grid;
        if (rowIndex >= 0 && rowIndex < grid.Rows.Count && grid.Rows[rowIndex].DataBoundItem is ClientRow row)
        {
            SelectClient(row.Source);
        }
    }

    private void SelectClient(ClientDto client)
    {
        if (!client.IsActive)
        {
            AppDialogs.Info(this, "Client inactif", $"{client.FullName} est désactivé : impossible de créer un dépôt à son nom.", MessageKind.Warning);
            return;
        }

        _client = client;
        _clientName.Text = client.FullName;
        _clientMeta.Text = $"{client.Phone} · {client.LoyaltyPoints} point(s) de fidélité" +
                           (client.Balance > 0 ? $" · solde dû {Fmt.Money(client.Balance)}" : string.Empty);
        _clientMeta.ForeColor = client.Balance > 0 ? Theme.Amber : Theme.TextMuted;
        _clientChip.Visible = true;
        _clientSearchRow.Visible = false;
        _clientResults.Visible = false;
        _clientSearch.Text = string.Empty;
        _pointsField.Control.Tag = client.LoyaltyPoints;
        SetPointsHint();
        UpdateSubmitState();
        ScheduleQuote();
        if (_lines.Count == 0)
        {
            _category.Focus();
        }
    }

    private void ClearClient()
    {
        _client = null;
        _clientChip.Visible = false;
        _clientSearchRow.Visible = true;
        _points.Text = "0";
        SetPointsHint();
        UpdateSubmitState();
        _clientSearch.Focus();
        ScheduleQuote();
    }

    private void SetPointsHint()
    {
        Label? hint = _pointsField.Controls.OfType<Label>().LastOrDefault();
        if (hint is not null && hint != _pointsField.Caption)
        {
            hint.Text = _client is null ? "Sélectionnez d'abord un client." : $"Disponibles : {_client.LoyaltyPoints} point(s)";
        }
    }

    private void CreateClient()
    {
        using ClientEditDialog dialog = new(Api);
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK && dialog.Result is { } client)
        {
            Host.Toast($"Client « {client.FullName} » créé.");
            SelectClient(client);
        }
    }

    // ==== Lignes ===========================================================================
    private void OnCategoryChanged()
    {
        if (_category.SelectedItem is not CategoryDto category)
        {
            _service.SetItems(Array.Empty<object>(), -1);
            return;
        }

        List<CategoryPriceDto> prices = category.Prices.Where(p => p.IsActive).OrderBy(p => p.ServiceType).ToList();
        int keep = _service.SelectedItem is CategoryPriceDto current ? prices.FindIndex(p => p.ServiceType == current.ServiceType) : -1;
        _service.SetItems(prices.Cast<object>(), keep >= 0 ? keep : 0);
        ApplyGridPrice();
    }

    private void ApplyGridPrice()
    {
        if (_service.SelectedItem is CategoryPriceDto price)
        {
            _price.AmountValue = price.Price;
        }

        UpdatePriceHint();
    }

    private void UpdatePriceHint()
    {
        if (_service.SelectedItem is CategoryPriceDto price)
        {
            _priceHint.Text = $"Tarif {Fmt.Money(price.Price)} · express +{Fmt.Money(price.ExpressSupplement)} · délai {price.EstimatedDays} j" +
                              (_lineExpress.Checked ? " · express activé" : string.Empty);
        }
        else
        {
            _priceHint.Text = string.Empty;
        }
    }

    private void AddOrUpdateLine()
    {
        if (_category.SelectedItem is not CategoryDto category || _service.SelectedItem is not CategoryPriceDto price)
        {
            Host.Toast("Choisissez une catégorie et un service.", MessageKind.Warning);
            return;
        }

        decimal unit = _price.AmountValue;
        if (unit < 0)
        {
            Host.Toast("Le prix ne peut pas être négatif.", MessageKind.Warning);
            return;
        }

        LineVm line = new()
        {
            Category = category,
            Service = price.ServiceType,
            Quantity = _quantity.Number,
            UnitPrice = unit,
            Express = _lineExpress.Checked,
            Description = string.IsNullOrWhiteSpace(_description.Text) ? null : _description.Text.Trim(),
            Grid = price
        };
        if (_editing is not null)
        {
            int index = _lines.IndexOf(_editing);
            _lines.Remove(_editing);
            _lines.Insert(Math.Max(0, index), line);
            _editing = null;
            _addLine.Text = "Ajouter";
            _addLine.IconGlyph = Icons.Add;
        }
        else
        {
            _lines.Add(line);
        }

        _quantity.Number = 1;
        _description.Text = string.Empty;
        _lineExpress.Checked = false;
        ApplyGridPrice();
        LinesChanged();
        _category.Focus();
    }

    private void EditLine(LineVm line)
    {
        _editing = line;
        int categoryIndex = _catalog.ToList().FindIndex(c => c.Id == line.Category.Id);
        _category.SelectedIndex = categoryIndex;
        OnCategoryChanged();
        int serviceIndex = _service.Items.ToList().FindIndex(s => ((CategoryPriceDto)s).ServiceType == line.Service);
        _service.SelectedIndex = serviceIndex;
        _quantity.Number = line.Quantity;
        _price.AmountValue = line.UnitPrice;
        _lineExpress.Checked = line.Express;
        _description.Text = line.Description ?? string.Empty;
        _addLine.Text = "Mettre à jour";
        _addLine.IconGlyph = Icons.Save;
        _price.Focus();
    }

    private void RemoveLine(LineVm line)
    {
        _lines.Remove(line);
        if (ReferenceEquals(_editing, line))
        {
            _editing = null;
            _addLine.Text = "Ajouter";
            _addLine.IconGlyph = Icons.Add;
        }

        LinesChanged();
    }

    private void ChangeQuantity(LineVm line, int delta)
    {
        line.Quantity = Math.Clamp(line.Quantity + delta, 1, 500);
        LinesChanged();
    }

    private void LinesChanged()
    {
        RefreshLinesGrid();
        UpdateDueEstimate();
        UpdateSubmitState();
        ScheduleQuote();
    }

    private void RefreshLinesGrid()
    {
        ThemedGrid grid = _linesGrid.Grid;
        int selected = grid.CurrentRow?.Index ?? -1;
        grid.SetRows(_lines.Select(l => new LineRow(l, _globalExpress.Checked)).ToList());
        _linesGrid.UpdateEmpty();
        if (selected >= 0 && selected < grid.Rows.Count)
        {
            grid.CurrentCell = grid.Rows[selected].Cells[0];
        }
    }

    private void UpdateDueEstimate()
    {
        if (_dueTouched || _lines.Count == 0)
        {
            return;
        }

        int days = _lines.Max(l => (l.Express || _globalExpress.Checked) ? 1 : (l.Grid?.EstimatedDays > 0 ? l.Grid.EstimatedDays : 3));
        _settingDue = true;
        _due.Value = DateTime.Today.AddDays(days);
        _settingDue = false;
    }

    // ==== Options ==========================================================================
    private void SetDeliveryUi()
    {
        bool home = _delivery.SelectedValue<DeliveryType>() == DeliveryType.HomeDelivery;
        _addressField.Visible = home;
        _feeField.Visible = home;
        if (!home)
        {
            _fee.Text = "0";
        }
        else if (string.IsNullOrWhiteSpace(_address.Text) && _client?.Address is { Length: > 0 } address)
        {
            _address.Text = address;
        }
    }

    private async Task CheckPromoAsync()
    {
        string code = _promo.Text.Trim();
        if (code.Length == 0)
        {
            _promoMessage.Text = "Saisissez un code promo.";
            _promoMessage.ForeColor = Theme.Amber;
            return;
        }

        decimal basis = (_quote?.Subtotal ?? 0) + (_quote?.ExpressTotal ?? 0) + (_quote?.DeliveryFee ?? 0);
        if (_lines.Count == 0)
        {
            _promoMessage.Text = "Ajoutez d'abord des articles pour tester le code.";
            _promoMessage.ForeColor = Theme.Amber;
            return;
        }

        PromotionValidationDto? result = null;
        bool ok = await RunAsync(async () => result = await Api.ValidatePromotionAsync(code, basis), "Vérification du code promo");
        if (ok && result is not null)
        {
            _promoMessage.Text = result.Valid ? $"{result.Message} (−{Fmt.Money(result.Discount)})" : result.Message;
            _promoMessage.ForeColor = result.Valid ? Theme.Green : Theme.Red;
        }
    }

    // ==== Devis en direct ==================================================================
    private void ScheduleQuote()
    {
        if (!Alive || _confirmPanel.Visible)
        {
            return;
        }

        _quoteTimer.Stop();
        _quoteTimer.Start();
        UpdateSubmitState();
    }

    private OrderQuoteRequest BuildQuoteRequest() => new(
        BuildItems(),
        _globalExpress.Checked,
        _delivery.SelectedValue<DeliveryType>(),
        _fee.AmountValue,
        _discount.AmountValue,
        string.IsNullOrWhiteSpace(_promo.Text) ? null : _promo.Text.Trim(),
        PointsValue(),
        _client?.Id);

    private List<CreateOrderItemRequest> BuildItems() => _lines
        .Select(l => new CreateOrderItemRequest(l.Category.Id, l.Service, l.Quantity, l.UnitPrice, l.Express, null, l.Description, null))
        .ToList();

    private int PointsValue() => int.TryParse(_points.Text.Trim(), out int p) && p > 0 ? p : 0;

    private async Task RequestQuoteAsync()
    {
        int version = ++_quoteVersion;
        if (_lines.Count == 0)
        {
            _quote = null;
            _sumError.Text = string.Empty;
            ShowQuote();
            return;
        }

        try
        {
            OrderQuoteDto? quote = await Api.QuoteOrderAsync(BuildQuoteRequest());
            if (!Alive || version != _quoteVersion)
            {
                return;
            }

            _quote = quote;
            _sumError.Text = string.Empty;
            if (quote is { PromotionValid: false, PromotionMessage: { Length: > 0 } message } && !string.IsNullOrWhiteSpace(_promo.Text))
            {
                _promoMessage.Text = message;
                _promoMessage.ForeColor = Theme.Red;
            }
            else if (quote is { PromotionValid: true } && !string.IsNullOrWhiteSpace(_promo.Text))
            {
                _promoMessage.Text = $"{quote.PromotionMessage} (−{Fmt.Money(quote.PromotionDiscount)})";
                _promoMessage.ForeColor = Theme.Green;
            }
        }
        catch (ApiException ex) when (!ex.IsUnauthorized)
        {
            if (!Alive || version != _quoteVersion)
            {
                return;
            }

            _quote = null;
            _sumError.Text = ex.Message;
        }

        ShowQuote();
    }

    private void ShowQuote()
    {
        OrderQuoteDto? q = _quote;
        _sumSubtotal.Text = Fmt.Money(q?.Subtotal ?? 0);
        _sumExpress.Text = Fmt.Money(q?.ExpressTotal ?? 0);
        _sumDelivery.Text = Fmt.Money(q?.DeliveryFee ?? 0);
        decimal discounts = (q?.DiscountAmount ?? 0) + (q?.PromotionDiscount ?? 0) + (q?.LoyaltyDiscount ?? 0);
        _sumDiscounts.Text = discounts > 0 ? "− " + Fmt.Money(discounts) : Fmt.Money(0);
        _sumTotal.Text = Fmt.Money(q?.Total ?? 0);
        SyncPayAll();
        UpdateBalance();
        UpdateSubmitState();
    }

    private void SyncPayAll()
    {
        if (_payAll.Checked)
        {
            _deposit.AmountValue = _quote?.Total ?? 0;
        }
    }

    private void UpdateBalance()
    {
        decimal total = _quote?.Total ?? 0;
        decimal deposit = Math.Max(0, _deposit.AmountValue);
        _sumDeposit.Text = Fmt.Money(deposit);
        decimal balance = total - deposit;
        _sumBalance.Text = Fmt.Money(Math.Max(0, balance));
        _sumBalance.ForeColor = balance > 0 ? Theme.Amber : Theme.Green;
        UpdateSubmitState();
    }

    private void UpdateSubmitState()
    {
        _submit.Enabled = _client is not null && _lines.Count > 0 && _quote is not null && !_confirmPanel.Visible;
    }

    // ==== Validation du dépôt ==============================================================
    private async Task SubmitAsync()
    {
        if (_client is null)
        {
            Host.Toast("Sélectionnez un client.", MessageKind.Warning);
            return;
        }

        if (_lines.Count == 0 || _quote is null)
        {
            Host.Toast("Ajoutez au moins un article.", MessageKind.Warning);
            return;
        }

        DeliveryType delivery = _delivery.SelectedValue<DeliveryType>();
        if (delivery == DeliveryType.HomeDelivery && string.IsNullOrWhiteSpace(_address.Text))
        {
            AppDialogs.Info(this, "Adresse manquante", "Renseignez l'adresse de livraison ou choisissez « Retrait en boutique ».", MessageKind.Warning);
            _address.Focus();
            return;
        }

        if (!string.IsNullOrWhiteSpace(_promo.Text) && _quote.PromotionValid == false)
        {
            AppDialogs.Info(this, "Code promo invalide", _quote.PromotionMessage ?? "Le code promo n'est pas valable. Corrigez-le ou videz le champ.", MessageKind.Warning);
            _promo.Focus();
            return;
        }

        decimal deposit = Math.Max(0, _deposit.AmountValue);
        if (deposit > _quote.Total + 0.005m)
        {
            AppDialogs.Info(this, "Acompte trop élevé", $"L'acompte ({Fmt.Money(deposit)}) dépasse le total ({Fmt.Money(_quote.Total)}).", MessageKind.Warning);
            _deposit.Focus();
            return;
        }

        CreateOrderRequest request = new(
            _client.Id,
            BuildItems(),
            _globalExpress.Checked,
            _dueTouched ? _due.Value : null,
            delivery,
            delivery == DeliveryType.HomeDelivery ? _address.Text.Trim() : null,
            delivery == DeliveryType.HomeDelivery ? _fee.AmountValue : 0,
            _discount.AmountValue,
            string.IsNullOrWhiteSpace(_promo.Text) ? null : _promo.Text.Trim(),
            PointsValue(),
            string.IsNullOrWhiteSpace(_notes.Text) ? null : _notes.Text.Trim(),
            deposit,
            _depositMethod.SelectedValue<PaymentMethod>(),
            string.IsNullOrWhiteSpace(_depositRef.Text) ? null : _depositRef.Text.Trim());

        OrderDto? order = null;
        bool ok = await RunAsync(async () => order = await Api.CreateOrderAsync(request), "Le dépôt n'a pas pu être enregistré");
        if (!ok || order is null || !Alive)
        {
            return;
        }

        _createdOrder = order;
        ShowConfirmation(order);
        _ = Host.RefreshBadgesAsync();
    }

    private void ShowConfirmation(OrderDto order)
    {
        _confirmTicket.Text = order.TicketNumber;
        _confirmInfo.Text = $"{order.ClientName} · {order.Items.Sum(i => i.Quantity)} article(s) · total {Fmt.Money(order.Total)}";
        _confirmDetail.Text = $"Retrait prévu le {Fmt.Date(order.DueDate)}   ·   " +
                              (order.PaidAmount > 0 ? $"acompte {Fmt.Money(order.PaidAmount)}   ·   " : string.Empty) +
                              (order.Balance > 0 ? $"reste à payer {Fmt.Money(order.Balance)}" : "entièrement réglé");
        _confirmEmail.Enabled = !string.IsNullOrWhiteSpace(order.ClientEmail);
        _confirmPanel.Visible = true;
        _confirmPanel.BringToFront();
        _formRoot.Visible = false;
        UpdateSubmitState();
        _confirmReceipt.Focus();
    }

    // ==== Réinitialisation =================================================================
    private void ConfirmReset()
    {
        bool dirty = _client is not null || _lines.Count > 0;
        if (!dirty || AppDialogs.Confirm(this, "Réinitialiser le dépôt", "Toutes les informations saisies seront effacées.", "Réinitialiser", danger: true))
        {
            ResetAll();
        }
    }

    private void ResetAll()
    {
        _quoteTimer.Stop();
        _quoteVersion++;
        _createdOrder = null;
        _client = null;
        _lines.Clear();
        _editing = null;
        _quote = null;
        _dueTouched = false;
        _clientChip.Visible = false;
        _clientSearchRow.Visible = true;
        _clientResults.Visible = false;
        _clientSearch.Text = string.Empty;
        _quantity.Number = 1;
        _description.Text = string.Empty;
        _lineExpress.Checked = false;
        _addLine.Text = "Ajouter";
        _addLine.IconGlyph = Icons.Add;
        _globalExpress.Checked = false;
        _delivery.SelectedIndex = 0;
        _address.Text = string.Empty;
        _fee.Text = "0";
        _discount.Text = "0";
        _promo.Text = string.Empty;
        _promoMessage.Text = string.Empty;
        _points.Text = "0";
        _notes.Text = string.Empty;
        _payAll.Checked = false;
        _deposit.Text = "0";
        _depositRef.Text = string.Empty;
        _depositMethod.SelectedIndex = 0;
        _settingDue = true;
        _due.Value = DateTime.Today.AddDays(2);
        _settingDue = false;
        _sumError.Text = string.Empty;
        SetPointsHint();
        RefreshLinesGrid();
        ShowQuote();
        _confirmPanel.Visible = false;
        _formRoot.Visible = true;
        UpdateSubmitState();
        if (_loaded)
        {
            ApplyGridPrice();
        }

        _clientSearch.Focus();
    }
}
