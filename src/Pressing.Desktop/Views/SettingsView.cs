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

    // ---- Navigation verticale des sections (colonne de 3) ------------------------------------
    private sealed class SectionTabs : Control
    {
        private readonly List<(string Key, string Text, string Glyph)> _tabs = new();
        private int _selected;
        private int _hover = -1;

        public SectionTabs()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Font = Theme.F(10f);
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        private static int ItemHeight => Theme.S(44);

        private static int Inset => Theme.S(8);

        public event EventHandler? SelectedChanged;

        public string SelectedKey => _tabs.Count == 0 ? string.Empty : _tabs[_selected].Key;

        public void Add(string key, string text, string glyph = "")
        {
            _tabs.Add((key, text, glyph));
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

        public override Size GetPreferredSize(Size proposedSize) =>
            new(proposedSize.Width, _tabs.Count * ItemHeight + 2 * Inset);

        private Rectangle ItemRect(int i) => new(Inset, Inset + i * ItemHeight, Width - 2 * Inset, ItemHeight);

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.BackOf(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle card = new(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Theme.RoundedRect(card, Theme.S(12)))
            using (SolidBrush back = new(Theme.Surface))
            using (Pen border = new(Theme.Border))
            {
                g.FillPath(back, path);
                g.DrawPath(border, path);
            }

            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine |
                                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            for (int i = 0; i < _tabs.Count; i++)
            {
                Rectangle r = ItemRect(i);
                bool active = i == _selected;
                if (active || i == _hover)
                {
                    using GraphicsPath p = Theme.RoundedRect(r, Theme.S(8));
                    using SolidBrush b = new(active ? Theme.Mix(Theme.Surface, Theme.Accent, 0.14f) : Theme.SurfaceHover);
                    g.FillPath(b, p);
                }

                if (active)
                {
                    using SolidBrush bar = new(Theme.Accent);
                    using GraphicsPath p = Theme.RoundedRect(new Rectangle(r.X, r.Y + Theme.S(10), Theme.S(3), r.Height - Theme.S(20)), Theme.S(2));
                    g.FillPath(bar, p);
                }

                Color color = active ? Theme.Accent : i == _hover ? Theme.Text : Theme.TextMuted;
                int x = r.X + Theme.S(16);
                if (_tabs[i].Glyph.Length > 0)
                {
                    TextRenderer.DrawText(g, _tabs[i].Glyph, Theme.Icon(11f), new Rectangle(x, r.Y, Theme.S(20), r.Height), color,
                        flags | TextFormatFlags.HorizontalCenter);
                    x += Theme.S(32);
                }

                TextRenderer.DrawText(g, _tabs[i].Text, active ? Theme.Semi(10f) : Font, new Rectangle(x, r.Y, Math.Max(1, r.Right - x - Theme.S(8)), r.Height),
                    active ? Theme.Text : color, flags);
            }
        }

        private int HitTest(Point p)
        {
            for (int i = 0; i < _tabs.Count; i++)
            {
                if (ItemRect(i).Contains(p))
                {
                    return i;
                }
            }

            return -1;
        }

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

    /// <summary>Affiche la section visible à sa hauteur préférée (la page défile, jamais la section).</summary>
    private sealed class SectionHost : LayoutControl
    {
        private Control? Current => Controls.Cast<Control>().FirstOrDefault(LayoutUtil.IsShown);

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            if (e.Control is not null)
            {
                LayoutUtil.Prepare(e.Control);
            }
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int width = proposedSize.Width > 0 ? proposedSize.Width : Width;
            return new Size(width, Current is { } c ? LayoutUtil.PreferredHeight(c, width) : 0);
        }

        protected override void Arrange()
        {
            if (Current is { } c)
            {
                c.SetBounds(0, 0, Width, Math.Max(Height, LayoutUtil.PreferredHeight(c, Width)));
            }
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
    private const string TabSecurity = "security";
    private const string TabBackups = "backups";
    private const string TabAudit = "audit";
    private const int LogPageSize = 12;
    private const int AuditPageSize = 30;

    private readonly List<Field> _fields = new();
    private readonly Dictionary<string, Control> _pages = new();
    private readonly HashSet<string> _loadedTabs = new();
    private readonly SectionTabs _tabs = new();
    private readonly FlatButton _save = new() { Text = "Enregistrer", Kind = ButtonKind.Primary, IconGlyph = Icons.Save };
    private readonly TextLabel _loyaltyExample = new() { Wrap = true, ForeColor = Theme.TextMuted, Font = Theme.F(9.5f) };
    private readonly InfoList _emailInfo = new();
    private readonly TextLabel _emailWarning = new() { Wrap = true, ForeColor = Theme.Amber, Font = Theme.F(9.5f), Visible = false };
    private readonly FlatButton _testEmail = new() { Text = "Envoyer un e-mail de test", Kind = ButtonKind.Secondary, IconGlyph = Icons.Mail };
    private readonly SelectBox _emailFilter = new() { Width = Theme.S(180), Height = Theme.S(32) };
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
    private PageLayout? _layout;

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

        _tabs.Add(TabShop, "Boutique", Icons.Settings);
        _tabs.Add(TabOrders, "Commandes", Icons.Orders);
        _tabs.Add(TabLoyalty, "Fidélité", Icons.Gift);
        _tabs.Add(TabEmails, "E-mails", Icons.Mail);
        _tabs.Add(TabReceipts, "Reçus", Icons.Print);
        if (admin)
        {
            _tabs.Add(TabSecurity, "Sécurité", Icons.Key);
            _tabs.Add(TabBackups, "Sauvegardes", Icons.Save);
            _tabs.Add(TabAudit, "Journal d'audit", Icons.Clock);
        }

        _backupResultCard = new TitledCard("Dernière sauvegarde", _backupInfo, "Vérifiée automatiquement après création", false, true) { Visible = false };

        SectionHost host2 = new();
        _pages[TabShop] = BuildShopPage();
        _pages[TabOrders] = BuildOrdersPage();
        _pages[TabLoyalty] = BuildLoyaltyPage();
        _pages[TabEmails] = BuildEmailsPage();
        _pages[TabReceipts] = BuildReceiptsPage();
        if (admin)
        {
            _pages[TabSecurity] = BuildSecurityPage();
            _pages[TabBackups] = BuildBackupsPage();
            _pages[TabAudit] = BuildAuditPage();
        }

        foreach (Control page in _pages.Values)
        {
            page.Visible = false;
            host2.Controls.Add(page);
        }

        _pages[TabShop].Visible = true;

        // Navigation des sections (3 colonnes, collante) + formulaire (9 colonnes) ; la page défile.
        VStack form = new();
        if (!admin)
        {
            form.Add(new TextLabel { Text = "Lecture seule : seuls les administrateurs peuvent modifier les paramètres.", ForeColor = Theme.Amber, Font = Theme.F(9.5f), Wrap = true });
        }

        form.Add(host2);
        GridRow body = new GridRow { StackBelow = 900 }.Add(_tabs, 3).Add(form, 9).Sticky(_tabs);
        _layout = SetPage(header, body, scroll: true);

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
    private static VStack ScrollPage(params Control[] cards)
    {
        VStack page = new();
        foreach (Control card in cards)
        {
            page.Add(card);
        }

        return page;
    }

    private static TitledCard Card(string title, string subtitle, Control content) =>
        new(title, content, subtitle, false, true);

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
        ToggleBox box = new() { Text = text };
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
        StepperBox box = new() { Minimum = min, Maximum = max };
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

    private Control BuildSecurityPage()
    {
        FormGrid twoFactor = new(1);
        twoFactor.Add(Toggle(SettingsKeys.RequireManagerTwoFactor, "Double authentification",
            "Exiger la double authentification pour les managers"));
        twoFactor.Add(new TextLabel
        {
            Text = "Activé : un manager saisit un code à usage unique après son mot de passe et doit l'activer à sa première connexion. " +
                   "Désactivé : les managers se connectent avec le seul mot de passe. Les administrateurs et les caissiers n'ont pas de double authentification. " +
                   "Pour un seul compte (téléphone perdu), utilisez « Réinitialiser la 2FA » dans l'écran Personnel.",
            Wrap = true,
            Font = Theme.F(9.5f),
            ForeColor = Theme.TextMuted
        });

        FormGrid lockout = new(1);
        lockout.Add(new TextLabel
        {
            Text = "Après 5 échecs de connexion consécutifs, un compte du personnel est verrouillé 15 minutes. " +
                   "Un administrateur peut le déverrouiller depuis l'écran Personnel.",
            Wrap = true,
            Font = Theme.F(9.5f),
            ForeColor = Theme.TextMuted
        });

        return ScrollPage(
            Card("Connexion des managers", "Réservé aux administrateurs. Chaque changement est inscrit au journal d'audit.", twoFactor),
            Card("Verrouillage des comptes", "Règle appliquée à tout le personnel (non modifiable).", lockout));
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

        ActionBar testBar = new(alignRight: false);
        testBar.Add(_testEmail);
        VStack config = new VStack(16).Add(_emailInfo).Add(_emailWarning).Add(testBar);

        // Journal
        ThemedGrid grid = _emailLog.Grid;
        grid.AddText("created", "DATE", nameof(EmailRow.Created), fixedWidth: 148);
        grid.AddText("to", "DESTINATAIRE", nameof(EmailRow.To), 30, minWidth: 140);
        grid.AddText("subject", "OBJET", nameof(EmailRow.Subject), 40, minWidth: 140);
        grid.AddText("template", "MODÈLE", nameof(EmailRow.Template), 20, minWidth: 100);
        grid.AddPill("status", "STATUT", fixedWidth: 120);
        grid.AddText("attempts", "ESSAIS", nameof(EmailRow.Attempts), right: true, fixedWidth: 80);
        grid.AddText("error", "ERREUR", nameof(EmailRow.Error), 20, minWidth: 90);
        grid.SetOptional("template", "attempts", "error");
        grid.SelectionChanged += (_, _) => _retry.Enabled = grid.SelectedItem<EmailRow>() is { Source.Status: EmailStatus.Failed or EmailStatus.Pending };
        _emailLog.AutoHeightRows = 5;
        _emailLog.MaxRows = LogPageSize;
        VStack logHost = new VStack(16).Add(_emailLog).Add(new Padded(_emailPager, new Padding(Theme.CardPadding - 1, 0, Theme.CardPadding - 1, 0)));

        _emailFilter.SetChoices(new[]
        {
            new Choice<EmailStatus?>(null, "Tous les statuts"),
            new Choice<EmailStatus?>(EmailStatus.Pending, "En attente"),
            new Choice<EmailStatus?>(EmailStatus.Sent, "Envoyés"),
            new Choice<EmailStatus?>(EmailStatus.Failed, "En échec"),
            new Choice<EmailStatus?>(EmailStatus.Simulated, "Simulés")
        }, null);

        TitledCard logCard = new("Journal des e-mails", logHost, "Les e-mails en échec peuvent être renvoyés.", true, true);
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

        InputBox printerName = TextField(SettingsKeys.TicketPrinterName, "Imprimante ticket 80 mm",
            "Nom exact de l'imprimante Windows (vide = désactivé)");
        ToggleBox drawer = Toggle(SettingsKeys.TicketPrinterOpenDrawer, "Tiroir-caisse", "Ouvrir le tiroir-caisse à l'impression du ticket");
        FlatButton testPrint = new() { Text = "Imprimer un ticket de test", Kind = ButtonKind.Secondary, IconGlyph = Icons.Print };
        testPrint.Click += async (_, _) => await TestPrinterAsync(printerName.Text.Trim(), drawer.Checked);
        FormGrid thermal = new(2);
        thermal.AddField("Imprimante ticket 80 mm", printerName, 2,
            "Impression directe ESC/POS (sans pilote de mise en page). Laissez vide pour désactiver le bouton « Ticket 80 mm ».");
        thermal.Add(drawer, 2);
        thermal.Add(testPrint, 1);

        return ScrollPage(
            Card("Mise en page des reçus", "Options appliquées au PDF généré pour chaque commande.", grid),
            Card("Ticket thermique 80 mm", "Imprimante de tickets du comptoir (ESC/POS, CP858) ; enregistrez avant de tester.", thermal));
    }

    private async Task TestPrinterAsync(string printerName, bool openDrawer)
    {
        TicketPrintOutcome outcome = TicketPrintOutcome.NotConfigured;
        bool ok = await RunAsync(async () => outcome = await TicketPrintService.PrintTestAsync(printerName, openDrawer),
            "Ticket de test");
        if (!ok || !Alive)
        {
            return;
        }

        switch (outcome)
        {
            case TicketPrintOutcome.Printed:
                Host.Toast($"Ticket de test envoyé à « {printerName} ».");
                break;
            case TicketPrintOutcome.Skipped:
                Host.Toast("Mode autotest : impression ignorée.", MessageKind.Info);
                break;
            default:
                AppDialogs.Info(this, "Imprimante de tickets non configurée", TicketPrintService.HowToConfigure, MessageKind.Info);
                break;
        }
    }

    private Control BuildBackupsPage()
    {
        ThemedGrid grid = _backupList.Grid;
        grid.AddText("file", "FICHIER", nameof(BackupRow.FileName), 45, minWidth: 200);
        grid.AddText("created", "CRÉÉE LE", nameof(BackupRow.Created), fixedWidth: 148);
        grid.AddText("size", "TAILLE", nameof(BackupRow.Size), right: true, fixedWidth: 96);
        grid.AddPill("integrity", "INTÉGRITÉ", fixedWidth: 128);
        grid.AddText("folder", "DOSSIER", nameof(BackupRow.Folder), 55, minWidth: 140);
        grid.SetOptional("folder");

        ActionBar actions = new(alignRight: false);
        actions.Add(_backupNow);
        actions.Add(_openFolder);
        TextLabel help = new() { Text = "Une copie complète de la base de données est créée puis son intégrité est vérifiée automatiquement.", Wrap = true, ForeColor = Theme.TextMuted, Font = Theme.F(9.5f) };
        VStack actionHost = new VStack(16).Add(help).Add(actions);
        _backupList.AutoHeightRows = 4;
        _backupList.MaxRows = 10;
        TitledCard listCard = new("Sauvegardes existantes", _backupList, "Les plus récentes en premier.", true, true);

        return ScrollPage(
            Card("Sauvegarde de la base", "Protégez vos données avant toute opération importante.", actionHost),
            _backupResultCard,
            listCard);
    }

    private Control BuildAuditPage()
    {
        ThemedGrid grid = _auditList.Grid;
        grid.AddText("when", "DATE", nameof(AuditRow.When), fixedWidth: 148);
        grid.AddText("actor", "AUTEUR", nameof(AuditRow.Actor), 18, minWidth: 100);
        grid.AddText("action", "ACTION", nameof(AuditRow.Action), 16, minWidth: 100);
        grid.AddText("entity", "ENTITÉ", nameof(AuditRow.Entity), 18, minWidth: 100);
        grid.AddText("details", "DÉTAILS", nameof(AuditRow.Details), 48, minWidth: 160);
        grid.AddText("ip", "ADRESSE IP", nameof(AuditRow.Ip), fixedWidth: 120);
        grid.SetOptional("ip", "entity");
        _auditList.AutoHeightRows = 8;
        _auditList.MaxRows = AuditPageSize;
        TitledCard card = new("Journal d'audit", new VStack(16).Add(_auditList).Add(new Padded(_auditPager, new Padding(Theme.CardPadding - 1, 0, Theme.CardPadding - 1, 0))),
            "Toutes les opérations sensibles, les plus récentes en premier.", true, true);
        return new VStack(16).Add(new GridRow().Add(_auditSearch, 8)).Add(card);
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

        _layout?.Viewport?.ScrollTo(0);
        _tabs.RequestPageLayout();

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
