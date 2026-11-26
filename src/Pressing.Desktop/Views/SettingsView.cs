using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Globalization;
using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Desktop.Dialogs;
using Pressing.Desktop.Services;
using Pressing.Desktop.Ui;

namespace Pressing.Desktop.Views;

/// <summary>
/// Paramètres : boutique, commandes, fidélité, e-mails, reçus (Manager en lecture, Administrateur en écriture),
/// puis sauvegardes et journal d'audit (Administrateur).
/// </summary>
public sealed class SettingsView : BaseView
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    // ---- Onglets dessinés --------------------------------------------------------------
    private sealed class SectionTabs : Control
    {
        private readonly List<(string Key, string Text)> _tabs = new();
        private int _selected;
        private int _hover = -1;

        public SectionTabs()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Height = Theme.S(46);
            Font = Theme.F(10f, FontStyle.Bold);
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        public event EventHandler? SelectedChanged;

        public string SelectedKey => _tabs.Count == 0 ? string.Empty : _tabs[_selected].Key;

        public void Add(string key, string text)
        {
            _tabs.Add((key, text));
            Invalidate();
        }

        public void Select(string key)
        {
            int index = _tabs.FindIndex(t => t.Key == key);
            if (index >= 0 && index != _selected)
            {
                _selected = index;
                Invalidate();
                SelectedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private List<Rectangle> Rects()
        {
            List<Rectangle> rects = new();
            int x = 0;
            foreach ((string _, string text) in _tabs)
            {
                int width = TextRenderer.MeasureText(text, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width + Theme.S(36);
                rects.Add(new Rectangle(x, 0, width, Height - 1));
                x += width;
            }

            return rects;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.BackOf(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen line = new(Theme.Border))
            {
                g.DrawLine(line, 0, Height - 1, Width, Height - 1);
            }

            List<Rectangle> rects = Rects();
            for (int i = 0; i < rects.Count; i++)
            {
                bool active = i == _selected;
                Color color = active ? Theme.Accent : i == _hover ? Theme.Text : Theme.TextMuted;
                TextRenderer.DrawText(g, _tabs[i].Text, Font, rects[i], color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                if (active)
                {
                    using SolidBrush brush = new(Theme.Accent);
                    g.FillRectangle(brush, rects[i].X + Theme.S(12), Height - Theme.S(3), rects[i].Width - Theme.S(24), Theme.S(3));
                }
            }
        }

        private int HitTest(Point p) => Rects().FindIndex(r => r.Contains(p));

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int hit = HitTest(e.Location);
            if (hit != _hover)
            {
                _hover = hit;
                Invalidate();
            }

            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int hit = HitTest(e.Location);
            if (hit >= 0 && e.Button == MouseButtons.Left)
            {
                Select(_tabs[hit].Key);
            }

            base.OnMouseClick(e);
        }
    }

    // ---- Lignes des tableaux ---------------------------------------------------------------
    private sealed class EmailRow : IPillRow, ITintRow
    {
        public EmailRow(EmailMessageDto m)
        {
            Source = m;
            Created = Fmt.DateTime(m.CreatedAt);
            To = m.ToEmail;
            Subject = m.Subject;
            Template = m.Template;
            Attempts = m.Attempts.ToString();
            Error = string.IsNullOrWhiteSpace(m.Error) ? "—" : m.Error.Replace('\r', ' ').Replace('\n', ' ');
        }

        public EmailMessageDto Source { get; }
        public string Created { get; }
        public string To { get; }
        public string Subject { get; }
        public string Template { get; }
        public string Attempts { get; }
        public string Error { get; }

        public (string Text, Color Color)? GetPill(string columnName) => columnName == "status"
            ? (Source.StatusLabel, Source.Status switch
            {
                EmailStatus.Sent => Theme.Green,
                EmailStatus.Failed => Theme.Red,
                EmailStatus.Pending => Theme.Amber,
                _ => Theme.Blue
            })
            : null;

        public Color? GetTint(string columnName) => columnName == "error" && Source.Status == EmailStatus.Failed ? Theme.Red : null;
    }

    private sealed class BackupRow : IPillRow
    {
        public BackupRow(BackupResultDto b)
        {
            Source = b;
            FileName = b.FileName;
            Created = Fmt.DateTime(b.CreatedAt);
            Size = FormatSize(b.Size);
            Folder = System.IO.Path.GetDirectoryName(b.Path) ?? b.Path;
        }

        public BackupResultDto Source { get; }
        public string FileName { get; }
        public string Created { get; }
        public string Size { get; }
        public string Folder { get; }

        public (string Text, Color Color)? GetPill(string columnName) =>
            columnName == "integrity" ? (Source.IntegrityOk ? "Intègre" : "Corrompue", Source.IntegrityOk ? Theme.Green : Theme.Red) : null;
    }

    private sealed class AuditRow
    {
        public AuditRow(AuditLogDto a)
        {
            When = Fmt.DateTime(a.CreatedAt);
            Actor = a.Actor;
            Action = ActionLabel(a.Action);
            Entity = string.IsNullOrWhiteSpace(a.EntityId) ? a.EntityType : $"{a.EntityType} #{a.EntityId}";
            Details = string.IsNullOrWhiteSpace(a.Details) ? "—" : a.Details.Replace('\r', ' ').Replace('\n', ' ');
            Ip = string.IsNullOrWhiteSpace(a.IpAddress) ? "—" : a.IpAddress;
        }

        public string When { get; }
        public string Actor { get; }
        public string Action { get; }
        public string Entity { get; }
        public string Details { get; }
        public string Ip { get; }

        private static string ActionLabel(string action) => action.ToLowerInvariant() switch
        {
            "create" => "Création",
            "update" => "Modification",
            "delete" => "Suppression",
            "login" => "Connexion",
            "backup" => "Sauvegarde",
            "cancel" => "Annulation",
            "refund" => "Remboursement",
            "payment" => "Paiement",
            "pickup" => "Retrait",
            _ => action
        };
    }

    private sealed class Field
    {
        public required string Key { get; init; }
        public required Control Control { get; init; }
        public required Func<string> Read { get; init; }
        public required Action<string> Write { get; init; }
        public required string Label { get; init; }
    }

    private const string TabShop = "shop";
    private const string TabOrders = "orders";
    private const string TabLoyalty = "loyalty";
    private const string TabEmails = "emails";
    private const string TabReceipts = "receipts";
    private const string TabBackups = "backups";
    private const string TabAudit = "audit";
    private const int LogPageSize = 12;
    private const int AuditPageSize = 30;

    private readonly List<Field> _fields = new();
    private readonly Dictionary<string, Control> _pages = new();
    private readonly HashSet<string> _loadedTabs = new();
    private readonly SectionTabs _tabs = new();
    private readonly FlatButton _save = new() { Text = "Enregistrer", Kind = ButtonKind.Primary, IconGlyph = Icons.Save };
    private readonly Label _loyaltyExample = new() { AutoSize = true, ForeColor = Theme.TextMuted, Font = Theme.F(9.5f), MaximumSize = new Size(Theme.S(760), 0), Margin = new Padding(0, Theme.S(4), 0, 0) };
    private readonly InfoList _emailInfo = new();
    private readonly Label _emailWarning = new() { AutoSize = true, ForeColor = Theme.Amber, Font = Theme.F(9.5f), MaximumSize = new Size(Theme.S(760), 0), Margin = new Padding(0, Theme.S(8), 0, 0), Visible = false };
    private readonly FlatButton _testEmail = new() { Text = "Envoyer un e-mail de test", Kind = ButtonKind.Secondary, IconGlyph = Icons.Mail };
    private readonly SelectBox _emailFilter = new() { Width = Theme.S(200) };
    private readonly GridCard _emailLog = new("Aucun e-mail dans le journal.");
    private readonly PagerBar _emailPager = new();
    private readonly FlatButton _retry = new() { Text = "Réessayer", Kind = ButtonKind.Secondary, IconGlyph = Icons.Refresh, Enabled = false };
    private readonly FlatButton _backupNow = new() { Text = "Sauvegarder maintenant", Kind = ButtonKind.Primary, IconGlyph = Icons.Save };
    private readonly FlatButton _openFolder = new() { Text = "Ouvrir le dossier", Kind = ButtonKind.Secondary, IconGlyph = Icons.Pdf };
    private readonly InfoList _backupInfo = new();
    private readonly TitledCard _backupResultCard;
    private readonly GridCard _backupList = new("Aucune sauvegarde pour le moment.");
    private readonly InputBox _auditSearch = new() { IconGlyph = Icons.Search, PlaceholderText = "Rechercher : auteur, action, entité, détails…" };
    private readonly GridCard _auditList = new("Aucune entrée dans le journal.");
    private readonly PagerBar _auditPager = new();
    private readonly System.Windows.Forms.Timer _auditDebounce = new() { Interval = 450 };
    private Dictionary<string, string> _loaded = new();
    private int _emailPageNumber = 1;
    private int _auditPageNumber = 1;
    private int _auditVersion;
    private string? _lastBackupFolder;

    public SettingsView(IAppHost host) : base(host)
    {
        bool admin = Session.IsAdmin;
        PageHeader header = new("Paramètres", admin
            ? "Configuration de la boutique, des e-mails et de la sécurité des données"
            : "Configuration de la boutique en consultation seule");
        header.AddAction("Actualiser", ButtonKind.Secondary, Icons.Refresh, async (_, _) => await RunAsync(RefreshAsync));
        if (admin)
        {
            header.AddControl(_save);
        }

        _tabs.Add(TabShop, "Boutique");
        _tabs.Add(TabOrders, "Commandes");
        _tabs.Add(TabLoyalty, "Fidélité");
        _tabs.Add(TabEmails, "E-mails");
        _tabs.Add(TabReceipts, "Reçus");
        if (admin)
        {
            _tabs.Add(TabBackups, "Sauvegardes");
            _tabs.Add(TabAudit, "Journal d'audit");
        }

        _backupResultCard = new TitledCard("Dernière sauvegarde", _backupInfo, "Vérifiée automatiquement après création", false, true) { Visible = false };

        Panel host2 = new() { Dock = DockStyle.Fill, BackColor = Theme.Background };
        _pages[TabShop] = BuildShopPage();
        _pages[TabOrders] = BuildOrdersPage();
        _pages[TabLoyalty] = BuildLoyaltyPage();
        _pages[TabEmails] = BuildEmailsPage();
        _pages[TabReceipts] = BuildReceiptsPage();
        if (admin)
        {
            _pages[TabBackups] = BuildBackupsPage();
            _pages[TabAudit] = BuildAuditPage();
        }

        foreach (Control page in _pages.Values)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            host2.Controls.Add(page);
        }

        _pages[TabShop].Visible = true;
        _tabs.Dock = DockStyle.Fill;
        Panel tabsWrap = new() { Height = Theme.S(46 + 12), Padding = new Padding(0, 0, 0, Theme.S(12)), BackColor = Theme.Background };
        tabsWrap.Controls.Add(_tabs);

        List<(Control Control, bool Fill)> rows = new() { (header, false) };
        if (!admin)
        {
            Label notice = UiKit.Notice("Lecture seule : seuls les administrateurs peuvent modifier les paramètres.", Theme.Amber);
            notice.Margin = new Padding(0, 0, 0, Theme.S(10));
            rows.Add((notice, false));
        }

        rows.Add((tabsWrap, false));
        rows.Add((host2, true));
        Controls.Add(UiKit.Rows(rows.ToArray()));

        if (!admin)
        {
            foreach (Field f in _fields)
            {
                if (f.Control is InputBox input)
                {
                    input.ReadOnly = true;
                }
                else
                {
                    f.Control.Enabled = false;
                }
            }
        }

        _tabs.SelectedChanged += async (_, _) => await OnTabChangedAsync();
        _save.Click += async (_, _) => await SaveChangesAsync();
        _testEmail.Click += async (_, _) => await SendTestEmailAsync();
        _retry.Click += async (_, _) => await RetryEmailAsync();
        _backupNow.Click += async (_, _) => await BackupAsync();
        _openFolder.Click += async (_, _) => await OpenFolderAsync();
        _emailFilter.SelectedIndexChanged += async (_, _) =>
        {
            _emailPageNumber = 1;
            if (_loadedTabs.Contains(TabEmails) && Alive)
            {
                await RunAsync(LoadEmailsAsync);
            }
        };
        _emailPager.PageRequested += async (_, page) =>
        {
            _emailPageNumber = page;
            await RunAsync(LoadEmailsAsync);
        };
        _auditPager.PageRequested += async (_, page) =>
        {
            _auditPageNumber = page;
            await RunAsync(LoadAuditAsync);
        };
        _auditDebounce.Tick += async (_, _) =>
        {
            _auditDebounce.Stop();
            _auditPageNumber = 1;
            await RunAsync(LoadAuditAsync);
        };
        _auditSearch.TextChanged += (_, _) =>
        {
            _auditDebounce.Stop();
            _auditDebounce.Start();
        };
    }

    public override string Title => "Paramètres";

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _auditDebounce.Dispose();
        }

        base.Dispose(disposing);
    }

    // ---- Construction des pages -------------------------------------------------------------
    private static ScrollPanel ScrollPage(params Control[] cards)
    {
        TableLayoutPanel page = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Margin = Padding.Empty
        };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < cards.Length; i++)
        {
            page.Controls.Add(cards[i], 0, i);
        }

        ScrollPanel scroll = new();
        scroll.Controls.Add(page);
        return scroll;
    }

    private static TitledCard Card(string title, string subtitle, Control content) =>
        new(title, content, subtitle, false, true) { Margin = new Padding(0, 0, 0, Theme.S(14)) };

    private InputBox TextField(string key, string label, string placeholder = "", string suffix = "", bool right = false)
    {
        InputBox box = new() { PlaceholderText = placeholder, Suffix = suffix, TextAlign = right ? HorizontalAlignment.Right : HorizontalAlignment.Left };
        _fields.Add(new Field { Key = key, Label = label, Control = box, Read = () => box.Text.Trim(), Write = v => box.Text = v });
        return box;
    }

    private InputBox Area(string key, string label, int height)
    {
        InputBox box = new() { Multiline = true, Height = Theme.S(height) };
        _fields.Add(new Field { Key = key, Label = label, Control = box, Read = () => box.Text.Trim(), Write = v => box.Text = v });
        return box;
    }

    private ToggleBox Toggle(string key, string label, string text)
    {
        ToggleBox box = new() { Text = text, Margin = new Padding(0, Theme.S(2), 0, Theme.S(6)) };
        _fields.Add(new Field
        {
            Key = key,
            Label = label,
            Control = box,
            Read = () => box.Checked ? "true" : "false",
            Write = v => box.Checked = bool.TryParse(v, out bool b) && b
        });
        return box;
    }

    private StepperBox Stepper(string key, string label, int min, int max)
    {
        StepperBox box = new() { Minimum = min, Maximum = max, Width = Theme.S(180), Anchor = AnchorStyles.Left };
        _fields.Add(new Field
        {
            Key = key,
            Label = label,
            Control = box,
            Read = () => box.Number.ToString(CultureInfo.InvariantCulture),
            Write = v => box.Number = int.TryParse(v, out int n) ? n : min
        });
        return box;
    }

    private static FlowLayoutPanel Stack(params Control[] controls)
    {
        FlowLayoutPanel flow = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = Padding.Empty
        };
        flow.Controls.AddRange(controls);
        return flow;
    }

    private Control BuildShopPage()
    {
        FormGrid identity = new(2);
        identity.AddField("Nom commercial *", TextField(SettingsKeys.ShopName, "Nom commercial", "ex. Pressing Éclat"));
        identity.AddField("Raison sociale", TextField(SettingsKeys.ShopLegalName, "Raison sociale"));
        identity.AddField("Adresse", TextField(SettingsKeys.ShopAddress, "Adresse"));
        identity.AddField("Ville", TextField(SettingsKeys.ShopCity, "Ville"));
        identity.AddField("Téléphone", TextField(SettingsKeys.ShopPhone, "Téléphone"));
        identity.AddField("WhatsApp", TextField(SettingsKeys.ShopWhatsApp, "WhatsApp"));
        identity.AddField("E-mail de la boutique", TextField(SettingsKeys.ShopEmail, "E-mail de la boutique"));
        identity.AddField("Devise affichée", TextField(SettingsKeys.Currency, "Devise"), 1, "Exemple : FCFA");
        identity.AddField("Slogan", TextField(SettingsKeys.ShopSlogan, "Slogan"), 2);

        FormGrid receipt = new(2);
        receipt.AddField("Horaires d'ouverture", TextField(SettingsKeys.OpeningHours, "Horaires d'ouverture", "ex. Lun–Sam : 7h30 – 19h30"), 2);
        receipt.AddField("Pied de page des reçus", Area(SettingsKeys.ReceiptFooter, "Pied de page des reçus", 90), 2, "Message imprimé en bas de chaque ticket.");

        return ScrollPage(
            Card("Identité de la boutique", "Ces informations apparaissent sur les reçus et dans les e-mails.", identity),
            Card("Horaires et reçus", "Horaires d'ouverture et message de fin de ticket.", receipt));
    }

    private Control BuildOrdersPage()
    {
        FormGrid delays = new(2);
        delays.AddField("Délai de traitement par défaut", Stepper(SettingsKeys.DefaultProcessingDays, "Délai par défaut", 0, 90), 1, "Nombre de jours avant la date de retrait prévue.");
        delays.AddField("Délai pour une commande express", Stepper(SettingsKeys.ExpressProcessingDays, "Délai express", 0, 90), 1, "Nombre de jours en mode express.");
        delays.AddField("Remise maximum autorisée", TextField(SettingsKeys.MaxDiscountPercent, "Remise maximum", suffix: "%", right: true), 1, "Plafond de la remise manuelle sur une commande.");

        FormGrid pickup = new(1);
        pickup.Add(Toggle(SettingsKeys.AllowPickupWithBalance, "Retrait avec solde", "Autoriser le retrait des articles même s'il reste un solde à payer"));

        return ScrollPage(
            Card("Délais et remises", "Valeurs proposées lors d'un nouveau dépôt.", delays),
            Card("Retrait des articles", "Règle appliquée lors de la remise des vêtements.", pickup));
    }

    private Control BuildLoyaltyPage()
    {
        ToggleBox enabled = Toggle(SettingsKeys.LoyaltyEnabled, "Fidélité activée", "Activer le programme de fidélité");
        FormGrid grid = new(2);
        grid.Add(enabled, 2);
        grid.AddField("Montant dépensé pour gagner 1 point", TextField(SettingsKeys.LoyaltyPointsPerAmount, "Montant pour 1 point", suffix: "FCFA", right: true));
        grid.AddField("Valeur d'un point à l'utilisation", TextField(SettingsKeys.LoyaltyPointValue, "Valeur d'un point", suffix: "FCFA", right: true));
        grid.Add(_loyaltyExample, 2);

        foreach (Field f in _fields.Where(f => f.Key is SettingsKeys.LoyaltyPointsPerAmount or SettingsKeys.LoyaltyPointValue or SettingsKeys.LoyaltyEnabled))
        {
            switch (f.Control)
            {
                case InputBox input:
                    input.TextChanged += (_, _) => UpdateLoyaltyExample();
                    break;
                case ToggleBox toggle:
                    toggle.CheckedChanged += (_, _) => UpdateLoyaltyExample();
                    break;
            }
        }

        return ScrollPage(Card("Programme de fidélité", "Les points sont cumulés à chaque paiement et convertis en remise.", grid));
    }

    private void UpdateLoyaltyExample()
    {
        Field? perAmount = _fields.FirstOrDefault(f => f.Key == SettingsKeys.LoyaltyPointsPerAmount);
        Field? pointValue = _fields.FirstOrDefault(f => f.Key == SettingsKeys.LoyaltyPointValue);
        Field? enabled = _fields.FirstOrDefault(f => f.Key == SettingsKeys.LoyaltyEnabled);
        if (perAmount is null || pointValue is null || enabled is null)
        {
            return;
        }

        if (enabled.Read() != "true")
        {
            _loyaltyExample.Text = "Le programme de fidélité est désactivé : aucun point n'est cumulé.";
            return;
        }

        if (int.TryParse(perAmount.Read(), out int amount) && amount > 0 && TryDecimal(pointValue.Read(), out decimal value) && value >= 0)
        {
            int points = 10_000 / amount;
            _loyaltyExample.Text = $"Exemple : une commande de {Fmt.Money(10_000)} rapporte {points} point(s), soit {Fmt.Money(points * value)} de remise à utiliser.";
        }
        else
        {
            _loyaltyExample.Text = "Saisissez un montant supérieur à 0 et une valeur de point positive pour voir un exemple.";
        }
    }

    private Control BuildEmailsPage()
    {
        FormGrid toggles = new(1);
        toggles.Add(Toggle(SettingsKeys.EmailNotifications, "Notifications", "Activer l'envoi des e-mails aux clients"));
        toggles.Add(Toggle(SettingsKeys.EmailOrderConfirmation, "Confirmation de dépôt", "Envoyer une confirmation lors du dépôt d'une commande"));
        toggles.Add(Toggle(SettingsKeys.EmailOrderReady, "Commande prête", "Prévenir le client lorsque sa commande est prête"));
        toggles.Add(Toggle(SettingsKeys.EmailReceipt, "Reçu", "Envoyer le reçu par e-mail après paiement"));

        Panel config = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent };
        _testEmail.Margin = new Padding(0, Theme.S(12), 0, 0);
        config.Controls.Add(Stack(_emailInfo, _emailWarning, _testEmail));

        // Journal
        ThemedGrid grid = _emailLog.Grid;
        grid.AddText("created", "DATE", nameof(EmailRow.Created), 14, minWidth: 130);
        grid.AddText("to", "DESTINATAIRE", nameof(EmailRow.To), 20, minWidth: 140);
        grid.AddText("subject", "OBJET", nameof(EmailRow.Subject), 24, minWidth: 140);
        grid.AddText("template", "MODÈLE", nameof(EmailRow.Template), 12, minWidth: 100);
        grid.AddPill("status", "STATUT", 11, 100);
        grid.AddText("attempts", "ESSAIS", nameof(EmailRow.Attempts), 6, true, 60);
        grid.AddText("error", "ERREUR", nameof(EmailRow.Error), 20, minWidth: 100);
        grid.SelectionChanged += (_, _) => _retry.Enabled = grid.SelectedItem<EmailRow>() is { Source.Status: EmailStatus.Failed or EmailStatus.Pending };
        _emailPager.Dock = DockStyle.Bottom;
        Panel logHost = new() { Height = Theme.S(12 * 36 + 40 + 56), BackColor = Color.Transparent };
        _emailLog.Dock = DockStyle.Fill;
        logHost.Controls.Add(_emailLog);
        logHost.Controls.Add(_emailPager);
        _emailLog.BringToFront();

        _emailFilter.SetChoices(new[]
        {
            new Choice<EmailStatus?>(null, "Tous les statuts"),
            new Choice<EmailStatus?>(EmailStatus.Pending, "En attente"),
            new Choice<EmailStatus?>(EmailStatus.Sent, "Envoyés"),
            new Choice<EmailStatus?>(EmailStatus.Failed, "En échec"),
            new Choice<EmailStatus?>(EmailStatus.Simulated, "Simulés")
        }, null);

        TitledCard logCard = new("Journal des e-mails", logHost, "Les e-mails en échec peuvent être renvoyés.", false, true) { Margin = new Padding(0, 0, 0, Theme.S(14)) };
        _retry.Margin = new Padding(Theme.S(10), 0, 0, 0);
        _emailFilter.Margin = Padding.Empty;
        logCard.AddAction(_emailFilter);
        logCard.AddAction(_retry);

        return ScrollPage(
            Card("Notifications par e-mail", "Choisissez les messages envoyés automatiquement aux clients.", toggles),
            Card("État de la configuration", "Fournisseur d'envoi utilisé par le serveur.", config),
            logCard);
    }

    private Control BuildReceiptsPage()
    {
        SelectBox paper = new();
        paper.SetChoices(new[]
        {
            new Choice<string>("A4", "A4 (210 × 297 mm)"),
            new Choice<string>("A5", "A5 (148 × 210 mm)"),
            new Choice<string>("80mm", "Ticket 80 mm")
        }, "A5");
        _fields.Add(new Field
        {
            Key = SettingsKeys.ReceiptPaperSize,
            Label = "Format du papier",
            Control = paper,
            Read = () => paper.SelectedValue<string>() ?? "A5",
            Write = v =>
            {
                if (!paper.Items.Cast<Choice<string>>().Any(c => c.Value == v))
                {
                    List<Choice<string>> items = paper.Items.Cast<Choice<string>>().ToList();
                    items.Add(new Choice<string>(v, v));
                    paper.SetChoices(items, v);
                }
                else
                {
                    paper.SetChoices(paper.Items.Cast<Choice<string>>().ToList(), v);
                }
            }
        });

        FormGrid grid = new(2);
        grid.AddField("Format du papier", paper);
        grid.Add(new Panel { Height = 1 }, 1);
        grid.Add(Toggle(SettingsKeys.ReceiptShowQr, "QR code", "Afficher le QR code de suivi sur le reçu"), 2);
        grid.Add(Toggle(SettingsKeys.ReceiptShowBarcode, "Code-barres", "Afficher le code-barres du ticket sur le reçu"), 2);

        return ScrollPage(Card("Mise en page des reçus", "Options appliquées au PDF généré pour chaque commande.", grid));
    }

    private Control BuildBackupsPage()
    {
        ThemedGrid grid = _backupList.Grid;
        grid.AddText("file", "FICHIER", nameof(BackupRow.FileName), 30, minWidth: 200);
        grid.AddText("created", "CRÉÉE LE", nameof(BackupRow.Created), 18, minWidth: 140);
        grid.AddText("size", "TAILLE", nameof(BackupRow.Size), 12, true, 90);
        grid.AddPill("integrity", "INTÉGRITÉ", 14, 120);
        grid.AddText("folder", "DOSSIER", nameof(BackupRow.Folder), 38, minWidth: 160);

        FlowLayoutPanel actions = new() { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Dock = DockStyle.Top };
        _backupNow.Margin = new Padding(0, 0, Theme.S(10), 0);
        _openFolder.Margin = Padding.Empty;
        actions.Controls.Add(_backupNow);
        actions.Controls.Add(_openFolder);
        Label help = UiKit.Muted("Une copie complète de la base de données est créée puis son intégrité est vérifiée automatiquement.");
        help.Margin = new Padding(0, Theme.S(10), 0, 0);
        help.MaximumSize = new Size(Theme.S(900), 0);
        Panel actionHost = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent };
        actionHost.Controls.Add(Stack(actions, help));

        Panel listHost = new() { Height = Theme.S(320), BackColor = Color.Transparent };
        _backupList.Dock = DockStyle.Fill;
        listHost.Controls.Add(_backupList);
        _backupResultCard.Margin = new Padding(0, 0, 0, Theme.S(14));

        return ScrollPage(
            Card("Sauvegarde de la base", "Protégez vos données avant toute opération importante.", actionHost),
            _backupResultCard,
            Card("Sauvegardes existantes", "Les plus récentes en premier.", listHost));
    }

    private Control BuildAuditPage()
    {
        ThemedGrid grid = _auditList.Grid;
        grid.AddText("when", "DATE", nameof(AuditRow.When), 14, minWidth: 130);
        grid.AddText("actor", "AUTEUR", nameof(AuditRow.Actor), 14, minWidth: 100);
        grid.AddText("action", "ACTION", nameof(AuditRow.Action), 11, minWidth: 100);
        grid.AddText("entity", "ENTITÉ", nameof(AuditRow.Entity), 14, minWidth: 110);
        grid.AddText("details", "DÉTAILS", nameof(AuditRow.Details), 38, minWidth: 180);
        grid.AddText("ip", "ADRESSE IP", nameof(AuditRow.Ip), 11, minWidth: 100);

        _auditSearch.Margin = new Padding(0, 0, 0, Theme.S(12));
        TableLayoutPanel layout = UiKit.Rows((_auditSearch, false), (_auditList, true), (_auditPager, false));
        _auditPager.Dock = DockStyle.Fill;
        return layout;
    }

    // ---- Données ---------------------------------------------------------------------------------
    public override async Task LoadAsync()
    {
        SettingsDto? settings = await Api.GetSettingsAsync();
        if (!Alive || settings is null)
        {
            return;
        }

        ApplySettings(settings);
        _loadedTabs.Clear();
        await LoadTabAsync(_tabs.SelectedKey);
    }

    /// <summary>Affiche une section par sa clé et charge ses données (utilisé aussi par l'auto-test).</summary>
    internal async Task ShowSectionAsync(string key)
    {
        _tabs.Select(key);
        await LoadTabAsync(key);
    }

    private async Task OnTabChangedAsync()
    {
        string key = _tabs.SelectedKey;
        foreach ((string pageKey, Control page) in _pages)
        {
            page.Visible = pageKey == key;
        }

        _save.Visible = Session.IsAdmin && key is not (TabBackups or TabAudit);
        if (Alive)
        {
            await RunAsync(() => LoadTabAsync(key));
        }
    }

    private async Task LoadTabAsync(string key)
    {
        if (_loadedTabs.Contains(key))
        {
            return;
        }

        switch (key)
        {
            case TabEmails:
                await LoadEmailsAsync();
                break;
            case TabBackups when Session.IsAdmin:
                await LoadBackupsAsync();
                break;
            case TabAudit when Session.IsAdmin:
                await LoadAuditAsync();
                break;
        }
    }

    public override async Task RefreshAsync()
    {
        SettingsDto? settings = await Api.GetSettingsAsync();
        if (!Alive || settings is null)
        {
            return;
        }

        ApplySettings(settings);
        _loadedTabs.Clear();
        await LoadTabAsync(_tabs.SelectedKey);
    }

    private void ApplySettings(SettingsDto settings)
    {
        _loaded = new Dictionary<string, string>(SettingsKeys.Defaults);
        foreach ((string key, string value) in settings.Values)
        {
            _loaded[key] = value;
        }

        foreach (Field field in _fields)
        {
            field.Write(_loaded.TryGetValue(field.Key, out string? value) ? value : string.Empty);
        }

        UpdateLoyaltyExample();

        EmailConfigStatusDto email = settings.Email;
        _emailInfo.Clear();
        _emailInfo.Add("Fournisseur", string.IsNullOrWhiteSpace(email.Provider) ? "—" : email.Provider, Theme.Accent, true);
        _emailInfo.Add("Adresse d'envoi", string.IsNullOrWhiteSpace(email.FromName) ? email.FromAddress : $"{email.FromName} <{email.FromAddress}>");
        _emailInfo.Add("Resend", email.ResendConfigured ? "Configuré" : "Non configuré", email.ResendConfigured ? Theme.Green : Theme.TextMuted);
        _emailInfo.Add("SMTP", email.SmtpConfigured ? "Configuré" : "Non configuré", email.SmtpConfigured ? Theme.Green : Theme.TextMuted);
        _emailWarning.Text = string.IsNullOrWhiteSpace(email.Warning) ? string.Empty : "Attention : " + email.Warning;
        _emailWarning.Visible = !string.IsNullOrWhiteSpace(email.Warning);
    }

    /// <summary>Valeur actuelle d'un champ de paramètre (auto-test).</summary>
    internal string? GetFieldValue(string key) => _fields.FirstOrDefault(f => f.Key == key)?.Read();

    /// <summary>Modifie un champ comme le ferait l'utilisateur (auto-test).</summary>
    internal void SetFieldValue(string key, string value) => _fields.First(f => f.Key == key).Write(value);

    private static bool TryDecimal(string text, out decimal value) =>
        decimal.TryParse(text.Replace(' ', ' ').Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value) ||
        decimal.TryParse(text.Replace(' ', ' ').Trim(), NumberStyles.Number, Fr, out value);

    /// <summary>Valide les champs et retourne uniquement les clés modifiées (null si un champ est invalide).</summary>
    private Dictionary<string, string>? Collect()
    {
        Dictionary<string, string> changes = new();
        foreach (Field field in _fields)
        {
            string value = field.Read();
            string? error = null;
            switch (field.Key)
            {
                case SettingsKeys.ShopName:
                case SettingsKeys.Currency:
                    if (value.Length == 0)
                    {
                        error = $"« {field.Label} » est obligatoire.";
                    }

                    break;
                case SettingsKeys.MaxDiscountPercent:
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int percent) || percent > 100)
                    {
                        error = "La remise maximum doit être un entier entre 0 et 100.";
                    }

                    break;
                case SettingsKeys.LoyaltyPointsPerAmount:
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int perAmount) || perAmount < 1)
                    {
                        error = "Le montant pour 1 point doit être un entier supérieur à 0.";
                    }

                    break;
                case SettingsKeys.LoyaltyPointValue:
                    if (!TryDecimal(value, out decimal pointValue) || pointValue < 0)
                    {
                        error = "La valeur d'un point doit être un nombre positif ou nul.";
                    }
                    else
                    {
                        value = pointValue.ToString(CultureInfo.InvariantCulture);
                    }

                    break;
            }

            if (error is not null)
            {
                AppDialogs.Info(this, "Paramètre invalide", error, MessageKind.Warning);
                field.Control.Focus();
                return null;
            }

            string previous = _loaded.TryGetValue(field.Key, out string? old) ? old.Trim() : string.Empty;
            if (!string.Equals(previous, value, StringComparison.Ordinal))
            {
                changes[field.Key] = value;
            }
        }

        return changes;
    }

    /// <summary>Enregistre les paramètres modifiés ; retourne leur nombre (-1 si invalide ou en erreur).</summary>
    internal async Task<int> SaveChangesAsync()
    {
        if (!Session.IsAdmin)
        {
            return -1;
        }

        Dictionary<string, string>? changes = Collect();
        if (changes is null)
        {
            return -1;
        }

        if (changes.Count == 0)
        {
            Host.Toast("Aucune modification à enregistrer.", MessageKind.Info);
            return 0;
        }

        SettingsDto? result = null;
        bool ok = await RunAsync(async () => result = await Api.UpdateSettingsAsync(changes), "Enregistrement des paramètres impossible");
        if (!ok || !Alive || result is null)
        {
            return -1;
        }

        ApplySettings(result);
        Host.Toast(changes.Count == 1 ? "1 paramètre enregistré." : $"{changes.Count} paramètres enregistrés.");
        return changes.Count;
    }

    // ---- E-mails ----------------------------------------------------------------------------------
    private async Task LoadEmailsAsync()
    {
        PagedResult<EmailMessageDto>? page = await Api.GetEmailsAsync(_emailPageNumber, LogPageSize, _emailFilter.SelectedValue<EmailStatus?>());
        if (!Alive || page is null)
        {
            return;
        }

        _loadedTabs.Add(TabEmails);
        _emailLog.Grid.SetRows(page.Items.Select(m => new EmailRow(m)).ToList());
        _emailLog.UpdateEmpty();
        _emailPager.Update(page.Page, (int)Math.Ceiling(page.Total / (double)Math.Max(1, page.PageSize)), page.Total, "e-mail");
        _retry.Enabled = _emailLog.Grid.SelectedItem<EmailRow>() is { Source.Status: EmailStatus.Failed or EmailStatus.Pending };
    }

    private async Task SendTestEmailAsync()
    {
        string? address = AppDialogs.Prompt(this, "E-mail de test", "Adresse du destinataire", "Envoyer",
            "Un message de test est envoyé avec la configuration actuelle du serveur.", true, false, Session.Staff.Email);
        if (address is null)
        {
            return;
        }

        if (!StaffDialog.IsEmail(address))
        {
            AppDialogs.Info(this, "Adresse invalide", "Saisissez une adresse e-mail valide, par exemple nom@exemple.com.", MessageKind.Warning);
            return;
        }

        EmailMessageDto? result = null;
        bool ok = await RunAsync(async () => result = await Api.SendTestEmailAsync(address), "Envoi de l'e-mail de test impossible");
        if (!ok || !Alive || result is null)
        {
            return;
        }

        if (result.Status == EmailStatus.Failed)
        {
            AppDialogs.Error(this, "L'envoi a échoué : " + (result.Error ?? "erreur inconnue") + ".", "E-mail de test");
        }
        else
        {
            Host.Toast($"E-mail de test à {address} : {result.StatusLabel.ToLowerInvariant()}.");
        }

        _emailPageNumber = 1;
        await RunAsync(LoadEmailsAsync);
    }

    private async Task RetryEmailAsync()
    {
        EmailRow? row = _emailLog.Grid.SelectedItem<EmailRow>();
        if (row is null)
        {
            return;
        }

        EmailMessageDto? result = null;
        bool ok = await RunAsync(async () => result = await Api.RetryEmailAsync(row.Source.Id), "Nouvel envoi impossible");
        if (!ok || !Alive)
        {
            return;
        }

        if (result?.Status == EmailStatus.Failed)
        {
            AppDialogs.Error(this, "Le nouvel envoi a échoué : " + (result.Error ?? "erreur inconnue") + ".", "E-mail");
        }
        else
        {
            Host.Toast("E-mail renvoyé.");
        }

        await RunAsync(LoadEmailsAsync);
    }

    // ---- Sauvegardes -------------------------------------------------------------------------------
    private async Task LoadBackupsAsync()
    {
        List<BackupResultDto>? backups = await Api.GetBackupsAsync();
        if (!Alive)
        {
            return;
        }

        _loadedTabs.Add(TabBackups);
        List<BackupRow> rows = (backups ?? new List<BackupResultDto>()).Select(b => new BackupRow(b)).ToList();
        _backupList.Grid.SetRows(rows);
        _backupList.UpdateEmpty();
        _lastBackupFolder ??= rows.FirstOrDefault()?.Folder;
    }

    private async Task BackupAsync()
    {
        BackupResultDto? result = null;
        bool ok = await RunAsync(async () => result = await Api.CreateBackupAsync(), "Sauvegarde impossible");
        if (!ok || !Alive || result is null)
        {
            return;
        }

        _lastBackupFolder = Path.GetDirectoryName(result.Path);
        _backupInfo.Clear();
        _backupInfo.Add("Fichier", result.FileName, Theme.Accent, true);
        _backupInfo.Add("Taille", FormatSize(result.Size));
        _backupInfo.Add("Créée le", Fmt.DateTime(result.CreatedAt));
        _backupInfo.Add("Intégrité", result.IntegrityOk ? "OK — la copie est lisible et cohérente" : "KO — la copie est corrompue, relancez la sauvegarde",
            result.IntegrityOk ? Theme.Green : Theme.Red, true);
        _backupInfo.Add("Emplacement", result.Path);
        _backupResultCard.Visible = true;
        if (result.IntegrityOk)
        {
            Host.Toast("Sauvegarde créée : " + result.FileName);
        }
        else
        {
            AppDialogs.Error(this, "La sauvegarde a été créée mais le contrôle d'intégrité a échoué. Relancez la sauvegarde.", "Sauvegarde");
        }

        await RunAsync(LoadBackupsAsync);
    }

    private async Task OpenFolderAsync()
    {
        string? folder = _backupList.Grid.SelectedItem<BackupRow>()?.Folder ?? _lastBackupFolder;
        if (folder is null)
        {
            await RunAsync(LoadBackupsAsync);
            folder = _lastBackupFolder;
        }

        if (folder is null || !Directory.Exists(folder))
        {
            AppDialogs.Info(this, "Dossier introuvable",
                folder is null
                    ? "Aucune sauvegarde n'existe encore : créez-en une d'abord."
                    : $"Le dossier « {folder} » n'est pas accessible depuis ce poste. Il se trouve sur le serveur.", MessageKind.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            AppDialogs.Error(this, "Impossible d'ouvrir le dossier : " + ex.Message, "Sauvegardes");
        }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1_048_576 => (bytes / 1_048_576d).ToString("0.0", Fr) + " Mo",
        >= 1024 => (bytes / 1024d).ToString("0", Fr) + " Ko",
        _ => bytes + " o"
    };

    // ---- Journal d'audit ---------------------------------------------------------------------------
    private async Task LoadAuditAsync()
    {
        int version = ++_auditVersion;
        string? search = string.IsNullOrWhiteSpace(_auditSearch.Text) ? null : _auditSearch.Text.Trim();
        PagedResult<AuditLogDto>? page = await Api.GetAuditAsync(_auditPageNumber, AuditPageSize, search);
        if (!Alive || version != _auditVersion || page is null)
        {
            return;
        }

        _loadedTabs.Add(TabAudit);
        _auditList.Grid.SetRows(page.Items.Select(a => new AuditRow(a)).ToList());
        _auditList.UpdateEmpty();
        _auditPager.Update(page.Page, (int)Math.Ceiling(page.Total / (double)Math.Max(1, page.PageSize)), page.Total, "entrée");
    }
}
