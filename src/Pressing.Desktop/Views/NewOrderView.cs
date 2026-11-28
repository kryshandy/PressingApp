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

        /// <summary>Ligne proposée par l'assistant vocal et jugée incertaine : surlignée en orange.</summary>
        public bool Uncertain { get; init; }
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
            "article" when Line.Uncertain => Theme.Amber,
            _ => null
        };
    }

    // ---- État ----------------------------------------------------------------------------
    private IReadOnlyList<CategoryDto> _catalog = Array.Empty<CategoryDto>();
    private ClientDto? _client;
    private readonly List<LineVm> _lines = new();

    // Assistant vocal : brouillon reçu avant le chargement du catalogue, et bandeau « à vérifier » (vide = masqué).
    private OrderDraftDto? _pendingDraft;
    private readonly TextLabel _assistantNote = new() { Wrap = true, Font = Theme.F(9.5f), ForeColor = Theme.Amber };
    private OrderQuoteDto? _quote;
    private int _quoteVersion;
    private bool _dueTouched;
    private bool _settingDue;
    private bool _loaded;
    private LineVm? _editing;

    // ---- Client --------------------------------------------------------------------------
    private readonly InputBox _clientSearch = new() { IconGlyph = Icons.Search, PlaceholderText = "Rechercher un client : nom, prénom ou téléphone…" };
    private readonly GridCard _clientResults = new("Aucun client trouvé.") { Visible = false };
    private readonly RoundedPanel _clientChip = new() { Visible = false, BackColor = Theme.SurfaceAlt, DrawBorder = false };
    private readonly FlatButton _newClient = new() { Text = "Nouveau client", Kind = ButtonKind.Secondary, IconGlyph = Icons.Add, AutoSize = false };
    private readonly Label _clientName = new() { AutoSize = true, Font = Theme.Semi(11f), ForeColor = Theme.Text };
    private readonly Label _clientMeta = new() { AutoSize = true, Font = Theme.F(9.5f), ForeColor = Theme.TextMuted };
    private readonly System.Windows.Forms.Timer _clientDebounce = new() { Interval = 300 };
    private GridRow _clientSearchRow = null!;
    private int _clientSearchVersion;

    // ---- Ajout de ligne ------------------------------------------------------------------
    private readonly SelectBox _category = new();
    private readonly SelectBox _service = new();
    private readonly StepperBox _quantity = new() { Minimum = 1, Maximum = 500 };
    private readonly InputBox _price = new() { Suffix = "FCFA", TextAlign = HorizontalAlignment.Right, SelectAllOnFocus = true };
    private readonly ToggleBox _lineExpress = new() { Text = "Express" };
    private readonly InputBox _description = new() { PlaceholderText = "Description / remarque (taches, couleur…)" };
    private readonly FlatButton _addLine = new() { Text = "Ajouter", Kind = ButtonKind.Secondary, IconGlyph = Icons.Add, AutoSize = false };
    private readonly TextLabel _priceHint = new() { ForeColor = Theme.TextFaint, Font = Theme.F(8.5f) };
    private readonly GridCard _linesGrid = new("Aucun article : choisissez une catégorie puis cliquez sur « Ajouter ».");

    // ---- Options -------------------------------------------------------------------------
    private readonly ToggleBox _globalExpress = new() { Text = "Dépôt express (tous les articles)" };
    private readonly DateBox _due = new();
    private readonly SelectBox _delivery = new();
    private readonly InputBox _address = new() { PlaceholderText = "Adresse de livraison" };
    private readonly InputBox _fee = new() { Suffix = "FCFA", TextAlign = HorizontalAlignment.Right, SelectAllOnFocus = true, Text = "0" };
    private readonly InputBox _discount = new() { Suffix = "FCFA", TextAlign = HorizontalAlignment.Right, SelectAllOnFocus = true, Text = "0" };
    private readonly InputBox _promo = new() { PlaceholderText = "BIENVENUE10", CharacterCasingUpper = true };
    private readonly FlatButton _promoCheck = new()
    {
        Text = string.Empty, IconGlyph = Icons.Check, Kind = ButtonKind.Secondary,
        AccessibleName = "Valider le code promo", AccessibleDescription = "Vérifier le code promotionnel saisi"
    };
    private readonly TextLabel _promoMessage = new() { Font = Theme.F(9f), Wrap = true };
    private readonly InputBox _points = new() { TextAlign = HorizontalAlignment.Right, SelectAllOnFocus = true, Text = "0", Suffix = "pts" };
    private readonly InputBox _notes = new() { PlaceholderText = "Remarques sur le dépôt (visibles par le personnel uniquement)" };
    private readonly InputBox _deposit = new() { Suffix = "FCFA", TextAlign = HorizontalAlignment.Right, SelectAllOnFocus = true, Text = "0" };
    private readonly SelectBox _depositMethod = new();
    private readonly InputBox _depositRef = new() { PlaceholderText = "Réf. transaction (facultatif)" };
    private readonly ToggleBox _payAll = new() { Text = "Payer la totalité maintenant" };
    private InlineField _addressField = null!;
    private InlineField _feeField = null!;
    private InlineField _pointsField = null!;
    private readonly System.Windows.Forms.Timer _quoteTimer = new() { Interval = 300 };

    // ---- Récapitulatif -------------------------------------------------------------------
    private Label _sumSubtotal = null!;
    private Label _sumExpress = null!;
    private Label _sumDelivery = null!;
    private Label _sumDiscounts = null!;
    private Label _sumTotal = null!;
    private Label _sumDeposit = null!;
    private Label _sumBalance = null!;
    private readonly TextLabel _sumError = new() { ForeColor = Theme.Red, Font = Theme.F(9f) };
    private readonly FlatButton _submit = new()
    {
        Text = "Valider le dépôt",
        Kind = ButtonKind.Primary,
        IconGlyph = Icons.Check,
        AutoSize = false,
        Height = Theme.S(48),
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

        // 8 + 4 : saisie à gauche, carte « Récapitulatif » collante à droite ; un seul défilement (la page).
        GridRow body = new GridRow { StackBelow = 860 }.Add(left, 8).Add(right, 4).Sticky(right);
        _formRoot = new PageLayout(header, body, scroll: true);
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
        if (parameter is OrderDraftDto draft)
        {
            // Avant le premier chargement le catalogue n'est pas là : le brouillon est appliqué à la fin de LoadAsync.
            if (_loaded)
            {
                _ = RunDraftAsync(draft);
            }
            else
            {
                _pendingDraft = draft;
            }

            return;
        }

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
        // -- Client : une seule rangée (recherche + nouveau client), résultats dessous si besoin --
        _clientSearchRow = new GridRow().Add(_clientSearch, 8).Add(_newClient, 4);
        _newClient.Click += (_, _) => CreateClient();

        ThemedGrid results = _clientResults.Grid;
        results.AddText("name", "CLIENT", nameof(ClientRow.Name), 55, minWidth: 140);
        results.AddText("phone", "TÉLÉPHONE", nameof(ClientRow.Phone), 45, minWidth: 130);
        results.AddText("balance", "SOLDE DÛ", nameof(ClientRow.Balance), right: true, fixedWidth: 112);
        _clientResults.AutoHeightRows = 1;
        _clientResults.MaxRows = 4;
        _clientResults.BackColor = Theme.SurfaceAlt;

        _clientChip.Padding = new Padding(Theme.S(16), 0, Theme.S(12), 0);
        TableLayoutPanel chip = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        chip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        chip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        TableLayoutPanel chipText = new() { AutoSize = true, ColumnCount = 1, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
        _clientName.Margin = Padding.Empty;
        _clientMeta.Margin = new Padding(0, Theme.S(2), 0, 0);
        chipText.Controls.Add(_clientName, 0, 0);
        chipText.Controls.Add(_clientMeta, 0, 1);
        FlatButton change = new() { Text = "Changer", Kind = ButtonKind.Secondary, Anchor = AnchorStyles.Right, Margin = Padding.Empty, Height = Theme.S(32) };
        change.Click += (_, _) => ClearClient();
        chip.Controls.Add(chipText, 0, 0);
        chip.Controls.Add(change, 1, 0);
        _clientChip.Controls.Add(chip);

        VStack clientBody = new VStack(12)
            .Add(_clientSearchRow)
            .Add(_clientResults)
            .Add(_clientChip, StackSize.Fixed, 68);
        TitledCard clientCard = new("Client", clientBody, null, false, true);

        // -- Articles : saisie alignée sur une grille de 4 colonnes, puis la grille des lignes --
        _category.Format = o => ((CategoryDto)o).Name;
        _category.Placeholder = "Catégorie…";
        _service.Format = o => ((CategoryPriceDto)o).ServiceLabel;
        _service.Placeholder = "Service…";

        FormGrid entry = new(4) { ColumnGap = Theme.S(16) };
        entry.AddField("Catégorie", _category);
        entry.AddField("Service", _service);
        entry.AddField("Quantité", _quantity);
        entry.AddField("Prix unitaire", _price);
        entry.AddField("Description", _description, 2);
        entry.Add(_lineExpress);
        entry.Add(_addLine);

        ThemedGrid grid = _linesGrid.Grid;
        grid.AddText("article", "ARTICLE", nameof(LineRow.Article), 100, minWidth: 160);
        grid.AddText("qty", "QTÉ", nameof(LineRow.Quantity), right: true, fixedWidth: 64);
        grid.AddText("unit", "PRIX UNIT.", nameof(LineRow.Unit), right: true, fixedWidth: 108);
        grid.AddText("total", "TOTAL", nameof(LineRow.Total), right: true, fixedWidth: 112);
        grid.AddPill("express", "", fixedWidth: 108);
        DataGridViewTextBoxColumn remove = grid.AddText("remove", "", nameof(LineRow.Remove), fixedWidth: 48);
        remove.DefaultCellStyle.Font = Theme.Icon(10f);
        remove.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        remove.ToolTipText = "Retirer la ligne";
        _linesGrid.MinimumSize = new Size(0, Theme.S(40 + 44 * 3 + 2));

        VStack linesBody = new VStack(16)
            .Add(entry)
            .Add(_priceHint)
            .Add(_linesGrid, StackSize.Fill)
            .Add(new FieldBox("Notes internes", _notes));
        linesBody.Gap = Theme.S(16);
        TitledCard linesCard = new("Articles", linesBody, "Double-clic : modifier · Suppr : retirer · + / − : quantité");

        return new VStack().Add(_assistantNote).Add(clientCard).Add(linesCard, StackSize.Fill);
    }

    private Control BuildRight()
    {
        _delivery.SetChoices(new[]
        {
            new Choice<DeliveryType>(DeliveryType.StorePickup, "Retrait en boutique"),
            new Choice<DeliveryType>(DeliveryType.HomeDelivery, "Livraison à domicile")
        }, DeliveryType.StorePickup);
        _depositMethod.SetChoices(Choices.PaymentMethods(), PaymentMethod.Cash);
        _due.Value = DateTime.Today.AddDays(2);
        _promoMessage.Text = string.Empty;
        new ToolTip().SetToolTip(_promoCheck, "Vérifier le code promo");

        _addressField = new InlineField("Adresse", _address);
        _feeField = new InlineField("Frais de livraison", _fee);
        _pointsField = new InlineField("Points fidélité", _points, "Disponibles : —");

        // Détail du montant puis totaux
        InfoList details = new(false);
        _sumSubtotal = details.Add("Sous-total", "0 FCFA");
        _sumExpress = details.Add("Supplément express", "0 FCFA");
        _sumDelivery = details.Add("Livraison", "0 FCFA");
        _sumDiscounts = details.Add("Remises", "0 FCFA", Theme.Green);
        InfoList totals = new(false);
        _sumTotal = totals.Add("Total", "0 FCFA", Theme.Accent, true, 16f);
        _sumBalance = totals.Add("Reste à payer", "0 FCFA", Theme.Amber, true, 11f);
        _sumDeposit = new Label();
        _sumError.Wrap = true;

        _globalExpress.Text = "Dépôt express (tous les articles)";
        VStack recap = new VStack(12)
            .Add(_globalExpress)
            .Add(new InlineField("Retrait prévu le", _due))
            .Add(new InlineField("Livraison", _delivery))
            .Add(_addressField)
            .Add(_feeField)
            .Add(new InlineField("Remise", _discount))
            .Add(new InlineField("Code promo", new PromoBox(_promo, _promoCheck)))
            .Add(_promoMessage)
            .Add(_pointsField)
            .Add(new SectionRule("Acompte"))
            .Add(new InlineField("Montant versé", _deposit))
            .Add(new InlineField("Mode de paiement", _depositMethod))
            .Add(new InlineField("Référence", _depositRef))
            .Add(_payAll)
            .Add(new SectionRule())
            .Add(details)
            .Add(new SectionRule())
            .Add(totals)
            .Add(_sumError)
            .Add(_submit, StackSize.Fixed, 48);
        RecapCard = new TitledCard("Récapitulatif", recap, null, false, true)
        {
            BorderColor = Theme.Mix(Theme.Border, Theme.Accent, 0.45f)
        };
        return RecapCard;
    }

    private TitledCard RecapCard { get; set; } = null!;

    /// <summary>Champ code promo + bouton « Vérifier » sur une seule rangée.</summary>
    private sealed class PromoBox : LayoutControl
    {
        private readonly Control _input;
        private readonly Control _button;

        public PromoBox(Control input, Control button)
        {
            _input = input;
            _button = button;
            LayoutUtil.Prepare(input);
            LayoutUtil.Prepare(button);
            Controls.Add(input);
            Controls.Add(button);
            Height = Theme.ControlHeight;
        }

        protected override void Arrange()
        {
            int bw = _button.GetPreferredSize(Size.Empty).Width;
            _button.SetBounds(Width - bw, 0, bw, Height);
            _input.SetBounds(0, 0, Math.Max(10, Width - bw - Theme.S(8)), Height);
        }
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
        if (_pendingDraft is { } pending)
        {
            _pendingDraft = null;
            await RunDraftAsync(pending);
        }
        else if (_client is null)
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

    // ==== Assistant vocal ==================================================================
    private async Task RunDraftAsync(OrderDraftDto draft)
    {
        try
        {
            await ApplyDraftAsync(draft);
        }
        catch (ApiException ex)
        {
            Host.Toast(ex.Message, MessageKind.Warning);
        }
    }

    /// <summary>
    /// Pré-remplit le dépôt avec le brouillon validé par le backend (client, articles du catalogue, express, acompte,
    /// date de retrait). Rien n'est enregistré : le caissier vérifie, corrige puis clique sur « Valider le dépôt ».
    /// Les éléments incertains sont surlignés en orange et expliqués dans le bandeau avec la transcription.
    /// </summary>
    public async Task ApplyDraftAsync(OrderDraftDto draft)
    {
        if (draft.Target != AssistantTarget.NewOrder)
        {
            return;
        }

        if (!_loaded)
        {
            _pendingDraft = draft;
            return;
        }

        bool hasWork = !_confirmPanel.Visible && (_client is not null || _lines.Count > 0);
        if (hasWork && !AppDialogs.Confirm(
                this, "Remplacer la saisie ?",
                "L'assistant a préparé un nouveau dépôt. La saisie en cours sera remplacée.", "Remplacer"))
        {
            return;
        }

        ResetAll();

        // -- Client : retrouvé par son identifiant ; sinon la recherche affiche homonymes ou correspondances.
        AssistantClientMatchDto? match = draft.Client;
        if (match?.ClientId is int clientId)
        {
            ClientDetailDto? detail = await Api.GetClientAsync(clientId);
            if (!Alive)
            {
                return;
            }

            if (detail is not null)
            {
                SelectClient(detail.Client);
                if (_client is not null && draft.UncertainFields.Contains("client"))
                {
                    _clientMeta.ForeColor = Theme.Amber;
                    _clientMeta.Text += " · à vérifier";
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(match?.FullName))
        {
            _clientSearch.Text = match!.FullName!;
        }

        // -- Articles : uniquement ceux reconnus par le catalogue, au prix du catalogue.
        int notAdded = 0;
        foreach (OrderDraftItemDto item in draft.Items)
        {
            CategoryDto? category = item.CategoryId is int categoryId
                ? _catalog.FirstOrDefault(c => c.Id == categoryId)
                : null;
            CategoryPriceDto? price = category is null || item.ServiceType is null
                ? null
                : category.Prices.FirstOrDefault(p => p.IsActive && p.ServiceType == item.ServiceType);
            if (category is null || price is null)
            {
                notAdded++;
                continue;
            }

            _lines.Add(new LineVm
            {
                Category = category,
                Service = price.ServiceType,
                Quantity = Math.Clamp(item.Quantity, 1, 500),
                UnitPrice = price.Price,
                Express = item.IsExpress,
                Grid = price,
                Uncertain = item.IsUncertain
            });
        }

        LinesChanged();

        // -- Acompte, mode de paiement, date de retrait demandée.
        if (draft.DepositAmount.GetValueOrDefault() > 0)
        {
            _deposit.AmountValue = draft.DepositAmount!.Value;
            if (draft.PaymentMethod is { } method)
            {
                _depositMethod.SetChoices(Choices.PaymentMethods(), method);
            }
        }

        if (draft.DueDate is { } due && due.Date >= DateTime.Today)
        {
            _settingDue = true;
            _due.Value = due.Date;
            _settingDue = false;
            _dueTouched = true;
        }

        // -- Bandeau : transcription et points à vérifier.
        List<string> note = new() { "Assistant vocal : vérifiez avant de valider." };
        if (!string.IsNullOrWhiteSpace(draft.Transcript))
        {
            note.Add("J'ai compris : « " + draft.Transcript.Trim() + " »");
        }

        note.AddRange(draft.Warnings.Select(w => "• " + w));
        if (notAdded > 0)
        {
            note.Add($"• {notAdded} article(s) non ajouté(s) : à saisir à la main.");
        }

        _assistantNote.Text = string.Join(Environment.NewLine, note);

        UpdateSubmitState();
        ScheduleQuote();
        Host.Toast("Brouillon de l'assistant prêt : vérifiez puis validez.");
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
        _assistantNote.Text = string.Empty;
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
