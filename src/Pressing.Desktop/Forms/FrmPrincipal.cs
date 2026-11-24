using System.Text.RegularExpressions;
using Pressing.Desktop.Api;
using Pressing.Desktop.Ui;
using Pressing.Shared.Dto;

namespace Pressing.Desktop.Forms;

/// <summary>Tableau de bord et navigation principale.</summary>
public sealed class FrmPrincipal : PressingForm
{
    private readonly Dictionary<string, NavButton> _navigation = new();
    private TextBox _recherche = null!;
    private Panel _contenu = null!;
    private TableLayoutPanel _corps = null!;

    private StatCard _carteCaisseJour = null!;
    private StatCard _carteCaisseTotale = null!;
    private StatCard _carteCommandes = null!;
    private StatCard _cartePretes = null!;
    private StatCard _carteImpayes = null!;

    private DataGridView _gridTarifs = null!;
    private DataGridView _gridEnCours = null!;
    private DataGridView _gridPret = null!;
    private DataGridView _gridImpayes = null!;
    private DataGridView _gridCaisse = null!;
    private Label _videTarifs = null!;
    private Label _videEnCours = null!;
    private Label _videPret = null!;
    private Label _videImpayes = null!;
    private Label _videCaisse = null!;

    public FrmPrincipal()
    {
        Text = "Pressing — Tableau de bord";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1440, 900);
        MinimumSize = new Size(1080, 680);
        BackColor = Theme.Light;
        WindowState = FormWindowState.Maximized;
        ConstruireInterface();
        Shown += async (_, _) => await RafraichirAsync();
    }

    private void ConstruireInterface()
    {
        TableLayoutPanel racine = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Theme.Light
        };
        racine.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 244F));
        racine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        racine.Controls.Add(ConstruireSidebar(), 0, 0);
        racine.Controls.Add(ConstruireZonePrincipale(), 1, 0);
        Controls.Add(racine);
    }

    private Panel ConstruireSidebar()
    {
        Panel sidebar = new() { Dock = DockStyle.Fill, BackColor = Theme.Navy, Padding = new Padding(0) };

        Panel logo = new() { Dock = DockStyle.Top, Height = 96, BackColor = Theme.Navy };
        Label marque = new()
        {
            Text = "PRESSING",
            Font = Theme.F(18f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Location = new Point(20, 20),
            AutoSize = true
        };
        Label sousTitre = new()
        {
            Text = "Gestion professionnelle",
            Font = Theme.F(8.5f),
            ForeColor = Color.FromArgb(148, 163, 184),
            BackColor = Color.Transparent,
            Location = new Point(22, 56),
            AutoSize = true
        };
        logo.Controls.Add(sousTitre);
        logo.Controls.Add(marque);

        Label pied = new()
        {
            Text = "  Pressing Desktop  •  v1.0",
            Dock = DockStyle.Bottom,
            Height = 36,
            Font = Theme.F(8f),
            ForeColor = Color.FromArgb(100, 116, 139),
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft
        };

        TableLayoutPanel menu = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 10,
            BackColor = Theme.Navy,
            Padding = new Padding(10, 6, 10, 6)
        };
        menu.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        (string Cle, string Libelle, string Icone)[] elements =
        {
            ("tableau de bord", "Tableau de bord", "\uE80F"),
            ("clients", "Clients", "\uE716"),
            ("nouveau dépôt", "Nouveau dépôt", "\uE710"),
            ("retrait", "Retrait", "\uE72A"),
            ("paiements", "Paiements", "\uE8EF"),
            ("tarifs", "Tarifs", "\uE8EC"),
            ("impayés", "Impayés", "\uE7BA"),
            ("historique", "Historique", "\uE81C"),
            ("recherche", "Recherche", "\uE721"),
            ("sauvegarde", "Sauvegarder les données", "\uE896")
        };

        for (int i = 0; i < elements.Length; i++)
        {
            string cle = elements[i].Cle;
            NavButton bouton = new()
            {
                Text = elements[i].Libelle,
                Icone = elements[i].Icone,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 2, 0, 2),
                Actif = cle == "tableau de bord"
            };
            bouton.Click += (_, _) => Naviguer(cle);
            _navigation[cle] = bouton;
            menu.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            menu.Controls.Add(bouton, 0, i);
        }

        sidebar.Controls.Add(menu);
        sidebar.Controls.Add(logo);
        sidebar.Controls.Add(pied);
        return sidebar;
    }

    private Panel ConstruireZonePrincipale()
    {
        Panel zone = new() { Dock = DockStyle.Fill, BackColor = Theme.Light };

        Panel barre = new() { Dock = DockStyle.Top, Height = 76, BackColor = Color.White };
        Label soulignement = new() { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Border, Margin = Padding.Empty };

        TableLayoutPanel barreTable = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = new Padding(16, 12, 16, 12)
        };
        barreTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250F));
        barreTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380F));
        barreTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        barreTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330F));
        barreTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        Label titre = new()
        {
            Text = "Tableau de bord",
            Dock = DockStyle.Fill,
            AutoSize = false,
            Margin = Padding.Empty,
            Font = Theme.F(17f, FontStyle.Bold),
            ForeColor = Theme.Navy,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 0, 8, 0)
        };

        Panel rechercheHote = new()
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(0, 22, 12, 12)
        };
        _recherche = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = Theme.F(9.5f),
            PlaceholderText = "Rechercher client, téléphone ou ticket…",
            BorderStyle = BorderStyle.FixedSingle,
            MinimumSize = new Size(180, 30)
        };
        _recherche.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) _ = RechercherAsync();
        };
        rechercheHote.Controls.Add(_recherche);

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        FlatButton boutonRecherche = new() { Text = "Rechercher", Width = 112, Margin = new Padding(0, 16, 8, 10) };
        boutonRecherche.SetSecondary();
        boutonRecherche.Click += async (_, _) => await RechercherAsync();
        FlatButton boutonDepot = new() { Text = "Nouveau dépôt", Width = 140, Margin = new Padding(0, 16, 8, 10) };
        boutonDepot.Click += (_, _) => Ouvrir(new FrmDepot());
        FlatButton boutonRefresh = new() { Text = "↻", Width = 42, Margin = new Padding(0, 16, 0, 10) };
        boutonRefresh.SetSecondary();
        boutonRefresh.Click += async (_, _) => await RafraichirAsync();
        actions.Controls.Add(boutonRecherche);
        actions.Controls.Add(boutonDepot);
        actions.Controls.Add(boutonRefresh);

        Panel espaceur = new() { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Color.Transparent };

        barreTable.Controls.Add(titre, 0, 0);
        barreTable.Controls.Add(rechercheHote, 1, 0);
        barreTable.Controls.Add(espaceur, 2, 0);
        barreTable.Controls.Add(actions, 3, 0);
        barre.Controls.Add(barreTable);
        barre.Controls.Add(soulignement);

        _contenu = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Theme.Light,
            Padding = new Padding(14),
            AutoScrollMinSize = new Size(1000, 780)
        };
        ConstruireContenu();

        zone.Controls.Add(_contenu);
        zone.Controls.Add(barre);
        return zone;
    }

    private void ConstruireContenu()
    {
        _corps = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 5,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
        for (int i = 0; i < 6; i++)
            _corps.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 6));
        _corps.RowStyles.Add(new RowStyle(SizeType.Absolute, 104F));
        _corps.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
        _corps.RowStyles.Add(new RowStyle(SizeType.Percent, 32F));
        _corps.RowStyles.Add(new RowStyle(SizeType.Percent, 36F));
        _corps.RowStyles.Add(new RowStyle(SizeType.Percent, 32F));

        Ajouter(_corps, ConstruireCartes(), 0, 0, 6);
        Ajouter(_corps, ConstruireActionsRapides(), 0, 1, 6);

        _gridTarifs = new DataGridView();
        RoundedPanel carteTarifs = UiFactory.Section("Grille des tarifs", out _gridTarifs, out _videTarifs);
        Grids.Tarifs.Configure(_gridTarifs, editable: false);
        _gridTarifs.CellDoubleClick += (_, _) => Ouvrir(new FrmTarifs());
        Ajouter(_corps, carteTarifs, 0, 2, 6);

        RoundedPanel carteEnCours = UiFactory.Section("Commandes en cours", out _gridEnCours, out _videEnCours);
        Grids.Commandes.Configure(_gridEnCours);
        _gridEnCours.Columns["Depot"]!.Visible = false;
        _gridEnCours.Columns["Prevue"]!.Visible = false;
        _gridEnCours.Columns["Paye"]!.Visible = false;
        _gridEnCours.CellDoubleClick += (_, _) =>
        {
            CommandeVue? cmd = Grids.Commandes.Selection(_gridEnCours);
            if (cmd != null) Ouvrir(new FrmClient(cmd.ClientId, commandeId: cmd.Id));
        };
        Ajouter(_corps, carteEnCours, 0, 3, 3);

        RoundedPanel cartePret = UiFactory.Section("Prêts à retirer", out _gridPret, out _videPret);
        Grids.Commandes.Configure(_gridPret);
        _gridPret.Columns["Depot"]!.Visible = false;
        _gridPret.Columns["Prevue"]!.Visible = false;
        _gridPret.Columns["Paye"]!.Visible = false;
        _gridPret.CellDoubleClick += (_, _) =>
        {
            CommandeVue? cmd = Grids.Commandes.Selection(_gridPret);
            if (cmd != null) Ouvrir(new FrmRetrait(commandeId: cmd.Id));
        };
        Ajouter(_corps, cartePret, 3, 3, 3);

        RoundedPanel carteImpayes = UiFactory.Section("Impayés", out _gridImpayes, out _videImpayes);
        Grids.Impayes.Configure(_gridImpayes);
        _gridImpayes.CellDoubleClick += (_, _) =>
        {
            ImpayeVue? impaye = Grids.Impayes.Selection(_gridImpayes);
            if (impaye != null) Ouvrir(new FrmClient(impaye.ClientId));
        };
        Ajouter(_corps, carteImpayes, 0, 4, 3);

        RoundedPanel carteCaisse = UiFactory.Section("Caisse — historique des paiements", out _gridCaisse, out _videCaisse);
        Grids.Paiements.Configure(_gridCaisse);
        Ajouter(_corps, carteCaisse, 3, 4, 3);

        _contenu.Controls.Add(_corps);
    }

    private TableLayoutPanel ConstruireCartes()
    {
        TableLayoutPanel cartes = new() { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1 };
        for (int i = 0; i < 5; i++)
            cartes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));

        _carteCaisseJour = new StatCard("Caisse aujourd'hui", "—", Theme.Green, "Encaissements du jour");
        _carteCaisseTotale = new StatCard("Caisse totale", "—", Theme.Blue, "Total depuis le début");
        _carteCommandes = new StatCard("Commandes en cours", "—", Theme.Orange, "Toutes commandes actives");
        _cartePretes = new StatCard("Articles prêts", "—", Color.FromArgb(13, 148, 136), "En attente de retrait");
        _carteImpayes = new StatCard("Impayés", "—", Theme.Red, "Solde restant dû");

        Ajouter(cartes, _carteCaisseJour, 0, 0);
        Ajouter(cartes, _carteCaisseTotale, 1, 0);
        Ajouter(cartes, _carteCommandes, 2, 0);
        Ajouter(cartes, _cartePretes, 3, 0);
        Ajouter(cartes, _carteImpayes, 4, 0);
        return cartes;
    }

    private Panel ConstruireActionsRapides()
    {
        FlowLayoutPanel panneau = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 2, 0, 0)
        };

        FlatButton depot = new() { Text = "＋  Nouveau dépôt", Width = 190 };
        depot.Click += (_, _) => Ouvrir(new FrmDepot());

        FlatButton retrait = new() { Text = "Retrait", Width = 130 };
        retrait.SetOutline(Theme.Green);
        retrait.Click += (_, _) => Ouvrir(new FrmRetrait());

        FlatButton rechercheClient = new() { Text = "Recherche client", Width = 170 };
        rechercheClient.SetSecondary();
        rechercheClient.Click += (_, _) =>
        {
            using ClientPickerDialog dlg = new("Rechercher un client");
            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedClientId.HasValue)
                Ouvrir(new FrmClient(dlg.SelectedClientId.Value));
        };

        FlatButton impayes = new() { Text = "Voir les impayés", Width = 170 };
        impayes.SetOutline(Theme.Red);
        impayes.Click += (_, _) => Ouvrir(new FrmImpayes());

        foreach (FlatButton b in new[] { depot, retrait, rechercheClient, impayes })
        {
            b.Margin = new Padding(6, 4, 4, 0);
            panneau.Controls.Add(b);
        }
        return panneau;
    }

    private static void Ajouter(TableLayoutPanel table, Control controle, int colonne, int ligne, int colspan = 1)
    {
        controle.Dock = DockStyle.Fill;
        controle.Margin = new Padding(6);
        table.Controls.Add(controle, colonne, ligne);
        if (colspan > 1) table.SetColumnSpan(controle, colspan);
    }

    private void Naviguer(string cle)
    {
        switch (cle)
        {
            case "tableau de bord":
                _ = RafraichirAsync();
                break;
            case "clients":
                using (ClientPickerDialog dlg = new("Liste des clients"))
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK && dlg.SelectedClientId.HasValue)
                        Ouvrir(new FrmClient(dlg.SelectedClientId.Value));
                }
                break;
            case "nouveau dépôt": Ouvrir(new FrmDepot()); break;
            case "retrait": Ouvrir(new FrmRetrait()); break;
            case "paiements": Ouvrir(new FrmPaiement()); break;
            case "tarifs": Ouvrir(new FrmTarifs()); break;
            case "impayés": Ouvrir(new FrmImpayes()); break;
            case "historique": Ouvrir(new FrmHistorique()); break;
            case "sauvegarde": _ = SauvegarderAsync(); break;
            case "recherche":
                _recherche.Focus();
                _recherche.SelectAll();
                break;
        }
    }

    private static void Ouvrir(Form formulaire) => formulaire.Show();

    private async Task SauvegarderAsync()
    {
        if (!Theme.Confirm("Créer une copie vérifiée de la base SQLite ? La base active ne sera pas remplacée."))
            return;
        try
        {
            Cursor = Cursors.WaitCursor;
            string fileName = await AppState.Api.CreateBackupAsync();
            Theme.Info("Sauvegarde vérifiée créée dans le dossier Backups :\n" + fileName,
                "Sauvegarde terminée");
        }
        catch (Exception ex)
        {
            Theme.Error(ex, "la sauvegarde des données");
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private async Task RafraichirAsync()
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            Task<DashboardDto> tacheDashboard = AppState.Api.GetDashboardAsync();
            Task<CaisseDto> tacheCaisse = AppState.Api.GetCaisseAsync();
            Task<List<TarifDto>> tacheTarifs = AppState.Api.GetTarifsAsync();
            await Task.WhenAll(tacheDashboard, tacheCaisse, tacheTarifs);

            DashboardDto dashboard = await tacheDashboard;
            CaisseDto caisse = await tacheCaisse;
            List<TarifDto> tarifs = await tacheTarifs;

            _carteCaisseJour.Set(Theme.Money(dashboard.CaisseAujourdhui), "Encaissements du jour");
            _carteCaisseTotale.Set(Theme.Money(dashboard.CaisseTotale), "Total depuis le début");
            _carteCommandes.Set(dashboard.CommandesEnCours.ToString(), "Commandes actives");
            _cartePretes.Set(dashboard.ArticlesPretARetirer.ToString(), "En attente de retrait");
            _carteImpayes.Set(Theme.Money(dashboard.SoldeImpaye), dashboard.NombreImpayes + " client(s) concerné(s)");

            Grids.Tarifs.Remplir(_gridTarifs, tarifs);
            Grids.Commandes.Remplir(_gridEnCours, dashboard.CommandesRecentes.Where(c => c.ResumeStatut != "Retiré"));
            Grids.Commandes.Remplir(_gridPret, dashboard.PretARetirer);
            Grids.Impayes.Remplir(_gridImpayes, dashboard.Impayes);
            Grids.Paiements.Remplir(_gridCaisse, caisse.Paiements.Take(30));

            MajVide(_videTarifs, _gridTarifs);
            MajVide(_videEnCours, _gridEnCours);
            MajVide(_videPret, _gridPret);
            MajVide(_videImpayes, _gridImpayes);
            MajVide(_videCaisse, _gridCaisse);
            Text = "Pressing — Tableau de bord  •  caisse aujourd'hui : " + Theme.Money(caisse.TotalAujourdhui);
        }
        catch (Exception ex)
        {
            Theme.Error(ex, "le chargement du tableau de bord");
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private static void MajVide(Label vide, DataGridView grid)
    {
        vide.Visible = grid.Rows.Count == 0;
        vide.BringToFront();
    }

    private async Task RechercherAsync()
    {
        string query = _recherche.Text.Trim();
        if (query.Length == 0)
        {
            Theme.Info("Saisissez un nom, un prénom, un téléphone, un WhatsApp ou un numéro de ticket.");
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            CommandeDto? commande = Regex.IsMatch(query, @"^PRESS-\d{4}-\d+$", RegexOptions.IgnoreCase)
                ? await AppState.Api.GetCommandeByTicketAsync(query)
                : null;

            List<ClientDto> clients = await AppState.Api.GetClientsAsync(query);
            if (commande != null && clients.All(c => c.Id != commande.ClientId))
            {
                ClientDto? proprietaire = await AppState.Api.GetClientAsync(commande.ClientId);
                if (proprietaire != null) clients.Add(proprietaire);
            }

            if (clients.Count == 0 && commande == null)
            {
                Theme.Info("Aucun client ni ticket ne correspond à « " + query + " ».", "Recherche");
                return;
            }

            using ClientPickerDialog dlg = new("Résultats de recherche", query, clients);
            if (dlg.ShowDialog(this) != DialogResult.OK || !dlg.SelectedClientId.HasValue) return;

            if (commande != null && commande.ClientId == dlg.SelectedClientId.Value)
                Ouvrir(new FrmClient(commande.ClientId, commandeId: commande.Id));
            else
                Ouvrir(new FrmClient(dlg.SelectedClientId.Value));
        }
        catch (Exception ex)
        {
            Theme.Error(ex, "la recherche");
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }
}
