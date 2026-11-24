using Pressing.Desktop.Api;
using Pressing.Desktop.Ui;
using Pressing.Shared.Dto;
using Pressing.Shared.Models;

namespace Pressing.Desktop.Forms;

/// <summary>Historique complet des commandes avec filtres et changement de statut.</summary>
public sealed class FrmHistorique : PressingForm
{
    private DataGridView _grid = new();
    private DataGridView _gridArticles = new();
    private Label _vide = Theme.EmptyLabel();
    private Label _videArticles = Theme.EmptyLabel();
    private readonly TextBox _recherche = Theme.Texte();
    private readonly ComboBox _statut = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.F(9.5f), Width = 150, Height = 32
    };
    private readonly CheckBox _express = new()
    {
        Text = "Express uniquement", Font = Theme.F(9f), ForeColor = Theme.Slate,
        BackColor = Color.Transparent, AutoSize = true
    };
    private readonly ComboBox _cmbNouveauStatut = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, Font = Theme.F(9f), Width = 140, Height = 30
    };
    private List<CommandeDto> _commandes = new();

    public FrmHistorique()
    {
        Text = "Historique des commandes";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1300, 830);
        MinimumSize = new Size(960, 620);
        BackColor = Theme.Light;
        ConstruireInterface();
        Shown += async (_, _) => await ChargerAsync();
    }

    private void ConstruireInterface()
    {
        TableLayoutPanel racine = new()
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(14)
        };
        racine.RowStyles.Add(new RowStyle(SizeType.Absolute, 92F));
        racine.RowStyles.Add(new RowStyle(SizeType.Percent, 52F));
        racine.RowStyles.Add(new RowStyle(SizeType.Percent, 48F));
        racine.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
        racine.Controls.Add(ConstruireFiltres(), 0, 0);
        racine.Controls.Add(ConstruireCommandes(), 0, 1);
        racine.Controls.Add(ConstruireArticles(), 0, 2);
        racine.Controls.Add(ConstruireBoutons(), 0, 3);
        Controls.Add(racine);
    }

    private RoundedPanel ConstruireFiltres()
    {
        RoundedPanel panneau = new() { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 8) };
        Label titre = new()
        {
            Text = "FILTRES", Dock = DockStyle.Top, Height = 20,
            Font = Theme.F(8.5f, FontStyle.Bold), ForeColor = Theme.Muted, BackColor = Color.Transparent
        };
        TableLayoutPanel table = new() { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220F));

        _recherche.PlaceholderText = "Ticket, client, téléphone…";
        _recherche.Dock = DockStyle.Fill;
        _recherche.TextChanged += (_, _) => AppliquerFiltre();
        table.Controls.Add(_recherche, 0, 0);

        _statut.Items.AddRange(new[] { "Tous", "Déposé", "En traitement", "Prêt", "Retiré" });
        _statut.SelectedIndex = 0;
        _statut.SelectedIndexChanged += (_, _) => AppliquerFiltre();
        table.Controls.Add(_statut, 1, 0);

        _express.CheckedChanged += (_, _) => AppliquerFiltre();
        table.Controls.Add(_express, 2, 0);

        FlatButton actualiser = new() { Text = "Actualiser", Dock = DockStyle.Fill, Margin = new Padding(8, 0, 0, 0) };
        actualiser.SetSecondary();
        actualiser.Click += async (_, _) => await ChargerAsync();
        table.Controls.Add(actualiser, 3, 0);

        panneau.Controls.Add(table);
        panneau.Controls.Add(titre);
        return panneau;
    }

    private RoundedPanel ConstruireCommandes()
    {
        RoundedPanel panneau = UiFactory.Section("Commandes", out _grid, out _vide);
        Grids.Commandes.Configure(_grid);
        _grid.SelectionChanged += (_, _) => AfficherArticles();
        _grid.CellDoubleClick += (_, _) => OuvrirFicheClient();
        return panneau;
    }

    private RoundedPanel ConstruireArticles()
    {
        RoundedPanel panneau = UiFactory.Section("Articles de la commande sélectionnée", out _gridArticles, out _videArticles);
        Grids.Articles.Configure(_gridArticles);

        Panel outils = new() { Dock = DockStyle.Bottom, Height = 46, BackColor = Color.Transparent };
        _cmbNouveauStatut.Items.AddRange(Enum.GetValues<StatutArticle>().Select(s => s.Libelle()).ToArray());
        _cmbNouveauStatut.SelectedIndex = 0;
        FlatButton appliquer = new() { Text = "Appliquer le statut", Width = 180, Dock = DockStyle.Right };
        appliquer.Click += async (_, _) => await AppliquerStatutAsync();
        outils.Controls.Add(appliquer);
        outils.Controls.Add(_cmbNouveauStatut);
        panneau.Controls.Add(outils);
        return panneau;
    }

    private Panel ConstruireBoutons()
    {
        Panel barre = new() { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        FlatButton fermer = new() { Text = "Fermer", Width = 110, Dock = DockStyle.Right };
        fermer.SetSecondary();
        fermer.Click += (_, _) => Close();
        FlatButton paiement = new() { Text = "Paiement", Width = 115, Dock = DockStyle.Right, Margin = new Padding(0, 0, 8, 0) };
        paiement.Click += (_, _) =>
        {
            CommandeVue? c = Grids.Commandes.Selection(_grid);
            if (c != null) Ouvrir(new FrmPaiement(commandeId: c.Id));
        };
        FlatButton retrait = new() { Text = "Retrait", Width = 110, Dock = DockStyle.Right, Margin = new Padding(0, 0, 8, 0) };
        retrait.SetColors(Theme.Green, Color.FromArgb(16, 124, 58));
        retrait.Click += (_, _) =>
        {
            CommandeVue? c = Grids.Commandes.Selection(_grid);
            if (c != null) Ouvrir(new FrmRetrait(commandeId: c.Id));
        };
        FlatButton fiche = new() { Text = "Fiche client", Width = 140, Dock = DockStyle.Right, Margin = new Padding(0, 0, 8, 0) };
        fiche.Click += (_, _) => OuvrirFicheClient();
        barre.Controls.Add(fermer);
        barre.Controls.Add(paiement);
        barre.Controls.Add(retrait);
        barre.Controls.Add(fiche);
        return barre;
    }

    private static void Ouvrir(Form formulaire) => formulaire.Show();

    private async Task ChargerAsync()
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            _commandes = await AppState.Api.GetCommandesAsync();
            AppliquerFiltre();
        }
        catch (Exception ex)
        {
            Theme.Error(ex, "le chargement de l'historique");
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void AppliquerFiltre()
    {
        string q = _recherche.Text.Trim();
        string statut = _statut.SelectedItem?.ToString() ?? "Tous";
        IEnumerable<CommandeDto> liste = _commandes;
        if (q.Length > 0)
        {
            string query = q.ToLowerInvariant();
            liste = liste.Where(c => c.NumeroTicket.ToLowerInvariant().Contains(query) ||
                                     c.ClientNom.ToLowerInvariant().Contains(query)).ToList();
        }
        if (statut != "Tous") liste = liste.Where(c => c.ResumeStatut == statut).ToList();
        if (_express.Checked) liste = liste.Where(c => c.Articles.Any(a => a.EstExpress)).ToList();

        Grids.Commandes.Remplir(_grid, liste);
        _vide.Visible = _grid.Rows.Count == 0;
        _vide.BringToFront();
        AfficherArticles();
    }

    private void AfficherArticles()
    {
        CommandeVue? vue = Grids.Commandes.Selection(_grid);
        CommandeDto? commande = vue == null ? null : _commandes.FirstOrDefault(c => c.Id == vue.Id);
        Grids.Articles.Remplir(_gridArticles, commande?.Articles ?? new List<LigneDto>());
        _videArticles.Visible = _gridArticles.Rows.Count == 0;
        _videArticles.BringToFront();
    }

    private void OuvrirFicheClient()
    {
        CommandeVue? vue = Grids.Commandes.Selection(_grid);
        if (vue == null)
        {
            Theme.Info("Sélectionnez d'abord une commande.");
            return;
        }
        using FrmClient formulaire = new(vue.ClientId, commandeId: vue.Id);
        formulaire.ShowDialog(this);
    }

    private async Task AppliquerStatutAsync()
    {
        LigneVue? ligne = _gridArticles.CurrentRow?.DataBoundItem as LigneVue;
        if (ligne == null)
        {
            Theme.Info("Sélectionnez d'abord un article.");
            return;
        }
        string libelle = _cmbNouveauStatut.SelectedItem?.ToString() ?? string.Empty;
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
}
