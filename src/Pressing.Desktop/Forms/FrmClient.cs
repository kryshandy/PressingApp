using Pressing.Desktop.Api;
using Pressing.Desktop.Ui;
using Pressing.Shared.Dto;
using Pressing.Shared.Models;

namespace Pressing.Desktop.Forms;

/// <summary>Fiche client : création, modification, historique, solde et retraits.</summary>
public sealed class FrmClient : PressingForm
{
    private int? _clientId;
    private readonly string? _recherche;
    private readonly int? _commandeId;
    private readonly bool _demanderClient;
    private List<CommandeDto> _historique = new();

    private readonly TextBox _nom = Theme.Texte();
    private readonly TextBox _prenom = Theme.Texte();
    private readonly TextBox _telephone = Theme.Texte();
    private readonly TextBox _whatsapp = Theme.Texte();
    private readonly TextBox _identifiant = Theme.Texte();
    private readonly TextBox _motDePasse = Theme.Texte();
    private readonly TextBox _dateCreation = Theme.Texte(lectureSeule: true);
    private readonly Label _infoCompte = Theme.Label(string.Empty);

    private readonly Label _resumeNom = Theme.Label("—", Theme.F(11f, FontStyle.Bold), Theme.Navy);
    private readonly Label _resumeTelephone = Theme.Label("—");
    private readonly Label _resumeWhatsapp = Theme.Label("—");
    private readonly Label _resumeIdentifiant = Theme.Label("—");
    private readonly Label _resumeDate = Theme.Label("—");

    private readonly Label _totalCommandes = Theme.Label("—", Theme.F(15f, FontStyle.Bold), Theme.Navy);
    private readonly Label _totalPaye = Theme.Label("—", Theme.F(15f, FontStyle.Bold), Theme.Green);
    private readonly Label _restant = Theme.Label("—", Theme.F(15f, FontStyle.Bold), Theme.Red);

    private DataGridView _gridHistorique = new();
    private DataGridView _gridArticles = new();
    private DataGridView _gridPaiements = new();
    private DataGridView _gridImpayees = new();
    private DataGridView _gridRetraits = new();
    private Label _videHistorique = Theme.EmptyLabel();
    private Label _videArticles = Theme.EmptyLabel();
    private Label _videPaiements = Theme.EmptyLabel();
    private Label _videImpayees = Theme.EmptyLabel();
    private Label _videRetraits = Theme.EmptyLabel();
    private readonly ComboBox _cmbStatut = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Font = Theme.F(9f),
        Width = 140,
        Height = 30
    };

    public int? ClientId => _clientId;

    public FrmClient(int? clientId = null, string? recherche = null, int? commandeId = null, bool demanderClient = true)
    {
        _clientId = clientId;
        _recherche = recherche;
        _commandeId = commandeId;
        _demanderClient = demanderClient;
        Text = "Fiche client";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1180, 800);
        MinimumSize = new Size(900, 600);
        BackColor = Theme.Light;
        ConstruireInterface();
        Shown += async (_, _) => await ChargerAsync();
    }

    private void ConstruireInterface()
    {
        TableLayoutPanel racine = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14)
        };
        racine.RowStyles.Add(new RowStyle(SizeType.Absolute, 216F));
        racine.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        racine.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));
        racine.Controls.Add(ConstruireEntete(), 0, 0);
        racine.Controls.Add(ConstruireOnglets(), 0, 1);
        racine.Controls.Add(ConstruireBarreActions(), 0, 2);
        Controls.Add(racine);
    }

    private RoundedPanel ConstruireEntete()
    {
        RoundedPanel entete = new() { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 10) };
        Label titre = new()
        {
            Text = "FICHE CLIENT",
            Dock = DockStyle.Top,
            Height = 22,
            Font = Theme.F(8.5f, FontStyle.Bold),
            ForeColor = Theme.Muted,
            BackColor = Color.Transparent
        };

        TableLayoutPanel champs = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 3,
            Padding = new Padding(0, 4, 0, 0)
        };
        for (int i = 0; i < 4; i++) champs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        champs.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
        champs.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
        champs.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        _dateCreation.Text = "—";
        _telephone.PlaceholderText = "Ex. 612345678";
        _whatsapp.PlaceholderText = "Ex. 612345678";
        _identifiant.PlaceholderText = "Facultatif (téléphone par défaut)";
        _motDePasse.PlaceholderText = "Requis pour activer l'accès Web";

        champs.Controls.Add(Theme.Champ("Nom *", _nom), 0, 0);
        champs.Controls.Add(Theme.Champ("Prénom *", _prenom), 1, 0);
        champs.Controls.Add(Theme.Champ("Téléphone *", _telephone), 2, 0);
        champs.Controls.Add(Theme.Champ("WhatsApp *", _whatsapp), 3, 0);
        _motDePasse.UseSystemPasswordChar = true;
        _motDePasse.PlaceholderText = "Required to activate web access";
        champs.Controls.Add(Theme.Champ("Identifiant web (facultatif)", _identifiant), 0, 1);
        champs.Controls.Add(Theme.Champ("Mot de passe web (facultatif)", _motDePasse), 1, 1);
        champs.Controls.Add(Theme.Champ("Client depuis", _dateCreation), 2, 1);

        _infoCompte.MaximumSize = new Size(0, 40);
        _infoCompte.Font = Theme.F(8.5f);
        _infoCompte.Text = "Saisissez un mot de passe initial pour activer l'accès Web. Sans mot de passe, le client n'a pas de compte en ligne.";
        champs.Controls.Add(_infoCompte, 3, 1);
        champs.SetColumnSpan(_infoCompte, 1);

        entete.Controls.Add(champs);
        entete.Controls.Add(titre);
        return entete;
    }

    private TabControl ConstruireOnglets()
    {
        TabControl onglets = new()
        {
            Dock = DockStyle.Fill,
            Font = Theme.F(9.5f),
            Padding = new Point(10, 4),
            DrawMode = TabDrawMode.OwnerDrawFixed,
            SizeMode = TabSizeMode.Fixed,
            ItemSize = new Size(130, 30)
        };
        onglets.DrawItem += (_, e) =>
        {
            Rectangle r = onglets.GetTabRect(e.Index);
            bool actif = e.Index == onglets.SelectedIndex;
            using SolidBrush fond = new(actif ? Theme.Blue : Theme.NavyLight);
            e.Graphics.FillRectangle(fond, r);
            TextRenderer.DrawText(e.Graphics, onglets.TabPages[e.Index].Text, Theme.F(9f, FontStyle.Bold),
                r, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
        onglets.TabPages.Add(OngletFiche());
        onglets.TabPages.Add(OngletHistorique());
        onglets.TabPages.Add(OngletSolde());
        onglets.TabPages.Add(OngletRetraits());
        return onglets;
    }

    private TabPage OngletFiche()
    {
        TabPage page = new("Fiche") { BackColor = Theme.Light, Padding = new Padding(10) };
        TableLayoutPanel table = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

        RoundedPanel identite = new() { Dock = DockStyle.Fill, Padding = new Padding(18) };
        identite.Controls.Add(Theme.Titre("IDENTITÉ", DockStyle.Top, 26));
        TableLayoutPanel lignes = new()
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, Padding = new Padding(0, 8, 0, 0)
        };
        lignes.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140F));
        lignes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (int i = 0; i < 5; i++) lignes.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        (string, Label)[] infos =
        {
            ("Nom complet", _resumeNom), ("Téléphone", _resumeTelephone),
            ("WhatsApp", _resumeWhatsapp), ("Identifiant web", _resumeIdentifiant),
            ("Client depuis", _resumeDate)
        };
        for (int i = 0; i < infos.Length; i++)
        {
            lignes.Controls.Add(Theme.Label(infos[i].Item1, Theme.F(8.5f, FontStyle.Bold), Theme.Muted,
                DockStyle.Fill, autoSize: false), 0, i);
            lignes.Controls.Add(infos[i].Item2, 1, i);
        }
        identite.Controls.Add(lignes);

        RoundedPanel compte = new() { Dock = DockStyle.Fill, Padding = new Padding(18) };
        Label titre = Theme.Titre("COMPTE WEB & ACCÈS RAPIDE", DockStyle.Top, 26);
        Label texte = Theme.Label(
            "Un compte web est créé automatiquement pour chaque client.\n\n" +
            "• Identifiant par défaut : le numéro de téléphone.\n" +
            "L'accès Web est facultatif. Pour l'activer ou le réinitialiser, saisissez un identifiant (ou laissez le téléphone) et un mot de passe dans l'en-tête, puis cliquez sur « Enregistrer ».\n\n" +
            "Le mot de passe est masqué pendant la saisie. Aucun mot de passe initial partagé n'est attribué automatiquement.",
            Theme.F(9.5f));
        texte.Dock = DockStyle.Fill;
        texte.MaximumSize = new Size(0, 0);
        compte.Controls.Add(texte);
        compte.Controls.Add(titre);

        table.Controls.Add(identite, 0, 0);
        table.Controls.Add(compte, 1, 0);
        page.Controls.Add(table);
        return page;
    }

    private TabPage OngletHistorique()
    {
        TabPage page = new("Historique complet") { BackColor = Theme.Light, Padding = new Padding(8) };
        TableLayoutPanel table = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 42F));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 58F));

        RoundedPanel haut = UiFactory.Section("Toutes les commandes", out _gridHistorique, out _videHistorique);
        Grids.Commandes.Configure(_gridHistorique);
        _gridHistorique.SelectionChanged += (_, _) => AfficherArticlesCommande();
        _gridHistorique.CellDoubleClick += (_, _) => OuvrirCommandeSelectionnee();

        RoundedPanel bas = UiFactory.Section("Articles — cliquez sur une ligne pour changer son statut", out _gridArticles, out _videArticles);
        Grids.Articles.Configure(_gridArticles);

        Panel outils = new() { Dock = DockStyle.Bottom, Height = 44, BackColor = Color.Transparent };
        _cmbStatut.Items.AddRange(Enum.GetValues<StatutArticle>().Select(s => s.Libelle()).ToArray());
        _cmbStatut.SelectedIndex = 0;
        FlatButton appliquer = new() { Text = "Appliquer le statut", Width = 170, Dock = DockStyle.Right };
        appliquer.Click += async (_, _) => await AppliquerStatutAsync();
        outils.Controls.Add(appliquer);
        outils.Controls.Add(_cmbStatut);
        bas.Controls.Add(outils);

        table.Controls.Add(haut, 0, 0);
        table.Controls.Add(bas, 0, 1);
        page.Controls.Add(table);
        return page;
    }

    private TabPage OngletSolde()
    {
        TabPage page = new("Solde") { BackColor = Theme.Light, Padding = new Padding(8) };
        TableLayoutPanel table = new()
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(0)
        };
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 100F));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

        TableLayoutPanel cartes = new() { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        for (int i = 0; i < 3; i++) cartes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        Panel Carte(string titre, Label valeur, Color couleur)
        {
            RoundedPanel p = new() { Dock = DockStyle.Fill, Padding = new Padding(14) };
            Label t = Theme.Label(titre.ToUpperInvariant(), Theme.F(8f, FontStyle.Bold), Theme.Muted,
                DockStyle.Top, 20);
            valeur.Dock = DockStyle.Fill;
            valeur.Font = Theme.F(16f, FontStyle.Bold);
            valeur.ForeColor = couleur;
            p.Controls.Add(valeur);
            p.Controls.Add(t);
            return p;
        }
        cartes.Controls.Add(Carte("Total commandes", _totalCommandes, Theme.Navy));
        cartes.Controls.Add(Carte("Total payé", _totalPaye, Theme.Green));
        cartes.Controls.Add(Carte("Reste à payer", _restant, Theme.Red));
        table.Controls.Add(cartes, 0, 0);

        RoundedPanel paiements = UiFactory.Section("Liste des paiements", out _gridPaiements, out _videPaiements);
        Grids.Paiements.Configure(_gridPaiements);
        table.Controls.Add(paiements, 0, 1);

        RoundedPanel impayees = UiFactory.Section("Commandes impayées", out _gridImpayees, out _videImpayees);
        Grids.Commandes.Configure(_gridImpayees);
        _gridImpayees.CellDoubleClick += (_, _) =>
        {
            CommandeVue? cmd = Grids.Commandes.Selection(_gridImpayees);
            if (cmd != null) Ouvrir(new FrmPaiement(commandeId: cmd.Id));
        };
        table.Controls.Add(impayees, 0, 2);

        page.Controls.Add(table);
        return page;
    }

    private TabPage OngletRetraits()
    {
        TabPage page = new("Retraits") { BackColor = Theme.Light, Padding = new Padding(8) };
        RoundedPanel section = UiFactory.Section("Historique des retraits du client", out _gridRetraits, out _videRetraits);
        Grids.Retraits.Configure(_gridRetraits);
        FlatButton nouveau = new() { Text = "Nouveau retrait", Width = 160, Dock = DockStyle.Bottom, Margin = new Padding(0, 8, 0, 0) };
        nouveau.SetColors(Theme.Green, Color.FromArgb(16, 124, 58));
        nouveau.Click += (_, _) => Ouvrir(new FrmRetrait(clientId: _clientId));
        section.Controls.Add(nouveau);
        page.Controls.Add(section);
        return page;
    }

    private Panel ConstruireBarreActions()
    {
        Panel barre = new() { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        FlatButton fermer = new() { Text = "Fermer", Width = 105, Dock = DockStyle.Right };
        fermer.SetSecondary();
        fermer.Click += (_, _) => Close();
        FlatButton actualiser = new() { Text = "Actualiser", Width = 110, Dock = DockStyle.Right, Margin = new Padding(0, 0, 8, 0) };
        actualiser.SetSecondary();
        actualiser.Click += async (_, _) => await ChargerAsync();
        FlatButton paiement = new() { Text = "Paiement", Width = 110, Dock = DockStyle.Right, Margin = new Padding(0, 0, 8, 0) };
        paiement.Click += (_, _) => Ouvrir(new FrmPaiement(clientId: _clientId));
        FlatButton retrait = new() { Text = "Retrait", Width = 105, Dock = DockStyle.Right, Margin = new Padding(0, 0, 8, 0) };
        retrait.SetColors(Theme.Green, Color.FromArgb(16, 124, 58));
        retrait.Click += (_, _) => Ouvrir(new FrmRetrait(clientId: _clientId));
        FlatButton depot = new() { Text = "Nouveau dépôt", Width = 145, Dock = DockStyle.Right, Margin = new Padding(0, 0, 8, 0) };
        depot.Click += (_, _) => Ouvrir(new FrmDepot(clientId: _clientId));
        FlatButton enregistrer = new() { Text = "Enregistrer", Width = 130, Dock = DockStyle.Left };
        enregistrer.SetColors(Theme.Blue, Theme.BlueDark);
        enregistrer.Click += async (_, _) => await EnregistrerAsync();
        barre.Controls.Add(fermer);
        barre.Controls.Add(actualiser);
        barre.Controls.Add(paiement);
        barre.Controls.Add(retrait);
        barre.Controls.Add(depot);
        barre.Controls.Add(enregistrer);
        return barre;
    }

    private static void Ouvrir(Form formulaire) => formulaire.Show();

    private async Task ChargerAsync()
    {
        try
        {
            if (!_clientId.HasValue) await ResoudreClientAsync();
            if (!_clientId.HasValue)
            {
                Text = "Nouveau client";
                return;
            }

            ClientDto? client = await AppState.Api.GetClientAsync(_clientId.Value);
            if (client == null)
            {
                Theme.Info("Ce client n'existe plus.", "Client introuvable");
                _clientId = null;
                return;
            }

            _nom.Text = client.Nom;
            _prenom.Text = client.Prenom;
            _telephone.Text = client.Telephone;
            _whatsapp.Text = client.WhatsApp;
            _identifiant.Text = string.Empty;
            _motDePasse.Text = string.Empty;
            _dateCreation.Text = Theme.Date(client.DateCreation);
            _infoCompte.Text = string.IsNullOrWhiteSpace(client.Identifiant)
                ? "Aucun identifiant web renseigné."
                : "Identifiant Web actuel : « " + client.Identifiant + " ». Laissez le mot de passe vide pour le conserver.";

            _resumeNom.Text = client.NomComplet;
            _resumeTelephone.Text = client.Telephone;
            _resumeWhatsapp.Text = client.WhatsApp;
            _resumeIdentifiant.Text = string.IsNullOrWhiteSpace(client.Identifiant) ? "—" : client.Identifiant;
            _resumeDate.Text = Theme.Date(client.DateCreation);
            Text = "Fiche client — " + client.NomComplet;

            Task<ClientSoldeDto> tSolde = AppState.Api.GetSoldeClientAsync(_clientId.Value);
            Task<List<CommandeDto>> tHistorique = AppState.Api.GetHistoriqueClientAsync(_clientId.Value);
            Task<List<RetraitDto>> tRetraits = AppState.Api.GetRetraitsClientAsync(_clientId.Value);
            await Task.WhenAll(tSolde, tHistorique, tRetraits);

            ClientSoldeDto solde = await tSolde;
            _historique = await tHistorique;
            List<RetraitDto> retraits = await tRetraits;

            _totalCommandes.Text = Theme.Money(solde.TotalCommandes);
            _totalPaye.Text = Theme.Money(solde.TotalPaye);
            _restant.Text = Theme.Money(solde.Solde);

            Grids.Commandes.Remplir(_gridHistorique, _historique);
            Grids.Commandes.Remplir(_gridImpayees, solde.CommandesImpayees);
            Grids.Paiements.Remplir(_gridPaiements, solde.Paiements);
            Grids.Retraits.Remplir(_gridRetraits, retraits);
            AfficherArticlesCommande();

            MajVide(_videHistorique, _gridHistorique);
            MajVide(_videPaiements, _gridPaiements);
            MajVide(_videImpayees, _gridImpayees);
            MajVide(_videRetraits, _gridRetraits);

            if (_commandeId.HasValue)
            {
                for (int i = 0; i < _gridHistorique.Rows.Count; i++)
                {
                    if (Grids.Commandes.Selection(_gridHistorique)?.Id == _commandeId.Value) break;
                    _gridHistorique.CurrentCell = _gridHistorique.Rows[i].Cells[2];
                }
            }
        }
        catch (Exception ex)
        {
            Theme.Error(ex, "le chargement de la fiche client");
        }
    }

    private async Task ResoudreClientAsync()
    {
        List<ClientDto> clients;
        if (!string.IsNullOrWhiteSpace(_recherche))
            clients = await AppState.Api.GetClientsAsync(_recherche);
        else if (!_demanderClient)
            return;
        else
            clients = await AppState.Api.GetClientsAsync();

        if (_recherche != null && clients.Count == 1)
        {
            _clientId = clients[0].Id;
            return;
        }

        using ClientPickerDialog dlg = new("Choisir un client", _recherche, clients);
        if (dlg.ShowDialog(this) == DialogResult.OK) _clientId = dlg.SelectedClientId;
    }

    private void AfficherArticlesCommande()
    {
        CommandeVue? vue = Grids.Commandes.Selection(_gridHistorique);
        CommandeDto? commande = vue == null ? null : _historique.FirstOrDefault(c => c.Id == vue.Id);
        Grids.Articles.Remplir(_gridArticles, commande?.Articles ?? new List<LigneDto>());
        MajVide(_videArticles, _gridArticles);
    }

    private void OuvrirCommandeSelectionnee()
    {
        CommandeVue? vue = Grids.Commandes.Selection(_gridHistorique);
        if (vue != null) Ouvrir(new FrmRetrait(commandeId: vue.Id));
    }

    private async Task AppliquerStatutAsync()
    {
        LigneVue? ligne = _gridArticles.CurrentRow?.DataBoundItem as LigneVue;
        if (ligne == null)
        {
            Theme.Info("Sélectionnez d'abord un article.");
            return;
        }

        string libelle = _cmbStatut.SelectedItem?.ToString() ?? string.Empty;
        StatutArticle statut = Enum.GetValues<StatutArticle>().First(s => s.Libelle() == libelle);
        try
        {
            await AppState.Api.UpdateStatutAsync(ligne.Id, statut);
            Theme.Info("Statut mis à jour : " + libelle);
            await ChargerAsync();
        }
        catch (Exception ex)
        {
            Theme.Error(ex, "la mise à jour du statut");
        }
    }

    private async Task EnregistrerAsync()
    {
        if (string.IsNullOrWhiteSpace(_nom.Text) || string.IsNullOrWhiteSpace(_prenom.Text) ||
            string.IsNullOrWhiteSpace(_telephone.Text) || string.IsNullOrWhiteSpace(_whatsapp.Text))
        {
            Theme.Info("Le nom, le prénom, le téléphone et le WhatsApp sont obligatoires.", "Champs requis");
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            string? identifiant = string.IsNullOrWhiteSpace(_identifiant.Text) ? null : _identifiant.Text.Trim();
            string? motDePasse = string.IsNullOrWhiteSpace(_motDePasse.Text) ? null : _motDePasse.Text.Trim();
            ClientDto enregistre;

            if (_clientId.HasValue)
            {
                ClientUpdateDto update = new()
                {
                    Nom = _nom.Text.Trim(), Prenom = _prenom.Text.Trim(),
                    Telephone = _telephone.Text.Trim(), WhatsApp = _whatsapp.Text.Trim(),
                    Identifiant = identifiant, MotDePasse = motDePasse
                };
                enregistre = await AppState.Api.UpdateClientAsync(_clientId.Value, update);
            }
            else
            {
                ClientCreateDto create = new()
                {
                    Nom = _nom.Text.Trim(), Prenom = _prenom.Text.Trim(),
                    Telephone = _telephone.Text.Trim(), WhatsApp = _whatsapp.Text.Trim(),
                    Identifiant = identifiant, MotDePasse = motDePasse
                };
                enregistre = await AppState.Api.CreateClientAsync(create);
                _clientId = enregistre.Id;
            }

            await ChargerAsync();
            Theme.Info(string.IsNullOrWhiteSpace(enregistre.Identifiant)
                ? "Client enregistré. Aucun accès Web n'est activé pour cette fiche."
                : "Client enregistré. Identifiant Web : " + enregistre.Identifiant + ". Communiquez au client le mot de passe défini avec lui.",
                "Enregistrement");
        }
        catch (Exception ex)
        {
            Theme.Error(ex, "l'enregistrement du client");
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
}
