using System.ComponentModel;
using Pressing.Desktop.Api;
using Pressing.Desktop.Ui;
using Pressing.Shared.Dto;

namespace Pressing.Desktop.Forms;

/// <summary>Retrait d'articles (partiel ou total) avec paiement associé.</summary>
public sealed class FrmRetrait : PressingForm
{
    private readonly int? _commandeIdInitial;
    private readonly int? _clientIdInitial;
    private readonly string? _ticketInitial;
    private CommandeDto? _commande;
    private bool _maj;
    private bool _montantModifie;
    private bool _enregistrementEnCours;

    private sealed class LigneRetraitVue
    {
        public int Id { get; set; }
        public string Designation { get; set; } = string.Empty;
        public string Categorie { get; set; } = string.Empty;
        public int Quantite { get; set; }
        public int DejaRetire { get; set; }
        public int Reste { get; set; }
        public int ARetirer { get; set; }
        public string Express { get; set; } = string.Empty;
        public string Statut { get; set; } = string.Empty;
        public decimal PrixUnitaire { get; set; }
        public decimal Supplement { get; set; }
        public decimal Montant { get; set; }
    }

    private readonly TextBox _ticket = Theme.Texte();
    private readonly TextBox _montant = Theme.Texte("0");
    private readonly DataGridView _grid = new();
    private DataGridView _gridHistorique = new();
    private readonly Label _vide = Theme.EmptyLabel();
    private Label _videHistorique = Theme.EmptyLabel();
    private readonly Label _suggestion = Theme.Label("Montant suggéré : 0 F", Theme.F(9f, FontStyle.Bold), Theme.Blue);
    private readonly Panel _infoHost = new() { Dock = DockStyle.Top, Height = 92, BackColor = Color.Transparent, Padding = new Padding(0, 0, 0, 8) };

    public FrmRetrait(int? commandeId = null, int? clientId = null, string? ticket = null)
    {
        _commandeIdInitial = commandeId;
        _clientIdInitial = clientId;
        _ticketInitial = ticket;
        Text = "Retrait d'articles";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1180, 820);
        MinimumSize = new Size(940, 620);
        BackColor = Theme.Light;
        ConstruireInterface();
        Shown += async (_, _) => await ChargerInitialAsync();
    }

    private void ConstruireInterface()
    {
        TableLayoutPanel racine = new()
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(14)
        };
        racine.RowStyles.Add(new RowStyle(SizeType.Absolute, 96F));
        racine.RowStyles.Add(new RowStyle(SizeType.Absolute, 98F));
        racine.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        racine.RowStyles.Add(new RowStyle(SizeType.Absolute, 172F));
        racine.Controls.Add(ConstruireSelection(), 0, 0);
        racine.Controls.Add(_infoHost, 0, 1);
        racine.Controls.Add(ConstruireArticles(), 0, 2);
        racine.Controls.Add(ConstruireHistorique(), 0, 3);
        Controls.Add(racine);
    }

    private RoundedPanel ConstruireSelection()
    {
        RoundedPanel panneau = new() { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 8) };
        Label titre = new()
        {
            Text = "1. COMMANDER À RETIRER", Dock = DockStyle.Top, Height = 20,
            Font = Theme.F(8.5f, FontStyle.Bold), ForeColor = Theme.Muted, BackColor = Color.Transparent
        };
        TableLayoutPanel table = new() { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
        for (int i = 0; i < 4; i++) table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));

        _ticket.PlaceholderText = "Ticket : PRESS-2026-00001";
        _ticket.Dock = DockStyle.Fill;
        _ticket.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) _ = ChercherTicketAsync();
        };
        table.Controls.Add(_ticket, 0, 0);

        FlatButton parTicket = new() { Text = "Chercher le ticket", Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0) };
        parTicket.Click += async (_, _) => await ChercherTicketAsync();
        table.Controls.Add(parTicket, 1, 0);

        FlatButton choisir = new() { Text = "Choisir une commande", Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0) };
        choisir.SetSecondary();
        choisir.Click += async (_, _) => await ChoisirCommandeAsync();
        table.Controls.Add(choisir, 2, 0);

        FlatButton parClient = new() { Text = "Par client", Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0) };
        parClient.SetSecondary();
        parClient.Click += async (_, _) => await ChoisirParClientAsync();
        table.Controls.Add(parClient, 3, 0);

        panneau.Controls.Add(table);
        panneau.Controls.Add(titre);
        return panneau;
    }

    private RoundedPanel ConstruireArticles()
    {
        RoundedPanel panneau = new() { Dock = DockStyle.Fill, Padding = new Padding(12) };
        Label titre = new()
        {
            Text = "2. ARTICLES À RETIRER  —  saisissez la quantité retirée pour chaque article",
            Dock = DockStyle.Top, Height = 28, Font = Theme.F(11f, FontStyle.Bold),
            ForeColor = Theme.Navy, BackColor = Color.Transparent
        };

        Panel hote = new() { Dock = DockStyle.Fill, BackColor = Color.White };
        _grid.Dock = DockStyle.Fill;
        hote.Controls.Add(_grid);
        hote.Controls.Add(_vide);
        ConfigurerGrille();

        Panel bas = new() { Dock = DockStyle.Bottom, Height = 62, BackColor = Color.Transparent };
        FlatButton tout = new() { Text = "Tout sélectionner", Width = 165, Dock = DockStyle.Left, Margin = new Padding(0, 8, 8, 0) };
        tout.SetSecondary();
        tout.Click += ToutSelectionner;
        Label montantTitre = new()
        {
            Text = "Montant versé maintenant (F) :", Width = 200, Dock = DockStyle.Left,
            Font = Theme.F(9f), ForeColor = Theme.Slate, BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(8, 8, 0, 0)
        };
        _montant.Width = 120;
        _montant.Dock = DockStyle.Left;
        _montant.Margin = new Padding(4, 8, 0, 0);
        _montant.TextChanged += (_, _) =>
        {
            if (!_maj) _montantModifie = true;
            MajSuggestion();
        };
        _suggestion.Margin = new Padding(12, 8, 0, 0);
        FlatButton enregistrer = new() { Text = "Enregistrer le retrait", Width = 190, Dock = DockStyle.Right, Margin = new Padding(8, 8, 0, 0) };
        enregistrer.SetColors(Theme.Green, Color.FromArgb(16, 124, 58));
        enregistrer.Click += async (_, _) => await EnregistrerAsync();
        FlatButton fermer = new() { Text = "Fermer", Width = 100, Dock = DockStyle.Right, Margin = new Padding(8, 8, 0, 0) };
        fermer.SetSecondary();
        fermer.Click += (_, _) => Close();
        bas.Controls.Add(enregistrer);
        bas.Controls.Add(fermer);
        bas.Controls.Add(_suggestion);
        bas.Controls.Add(_montant);
        bas.Controls.Add(montantTitre);
        bas.Controls.Add(tout);

        panneau.Controls.Add(hote);
        panneau.Controls.Add(bas);
        panneau.Controls.Add(titre);
        return panneau;
    }

    private RoundedPanel ConstruireHistorique()
    {
        RoundedPanel panneau = UiFactory.Section("Historique des retraits du client", out _gridHistorique, out _videHistorique);
        Grids.Retraits.Configure(_gridHistorique);
        return panneau;
    }

    private void ConfigurerGrille()
    {
        Theme.StyleGrid(_grid, lectureSeule: false);
        _grid.Columns.Clear();
        _grid.Columns.Add(Grids.Col("Id", "Id", 1, visible: false));
        _grid.Columns.Add(Grids.Col("Designation", "Article", 170));
        _grid.Columns.Add(Grids.Col("Categorie", "Catégorie", 100));
        _grid.Columns.Add(Grids.Col("Quantite", "Déposée", 80));
        _grid.Columns.Add(Grids.Col("DejaRetire", "Déjà retirée", 95));
        _grid.Columns.Add(Grids.Col("Reste", "Reste", 75));
        _grid.Columns.Add(Grids.Col("ARetirer", "À retirer", 85, lectureSeule: false));
        _grid.Columns.Add(Grids.Col("Express", "Option", 90));
        _grid.Columns.Add(Grids.Col("Statut", "Statut", 110));
        _grid.Columns.Add(Grids.Col("Montant", "Montant (F)", 105, "N0"));
        _grid.CellValueChanged += (_, e) =>
        {
            if (_maj || e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count) return;
            DataGridViewRow ligne = _grid.Rows[e.RowIndex];
            if (_grid.Columns[e.ColumnIndex].Name != "ARetirer") return;
            if (ligne.DataBoundItem is not LigneRetraitVue vue) return;

            if (vue.ARetirer < 0) vue.ARetirer = 0;
            if (vue.ARetirer > vue.Reste) vue.ARetirer = vue.Reste;
            _maj = true;
            try
            {
                ligne.Cells["ARetirer"].Value = vue.ARetirer;
                vue.Montant = vue.ARetirer * (vue.PrixUnitaire +
                    (vue.Express.Contains("EXPRESS", StringComparison.OrdinalIgnoreCase) ? vue.Supplement : 0));
                ligne.Cells["Montant"].Value = vue.Montant;
            }
            finally
            {
                _maj = false;
            }
            MajSuggestion();
        };
        _grid.CellFormatting += (s, e) =>
        {
            Theme.StatusFormatting(s, e, "Statut");
            Theme.ExpressFormatting(s, e, "Express");
        };
    }

    private async Task ChargerInitialAsync()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_ticketInitial))
            {
                CommandeDto? commande = await AppState.Api.GetCommandeByTicketAsync(_ticketInitial);
                if (commande != null) await AfficherCommandeAsync(commande);
                else Theme.Info("Aucune commande ne correspond à ce ticket.", "Ticket introuvable");
                return;
            }
            if (_commandeIdInitial.HasValue)
            {
                CommandeDto? commande = await AppState.Api.GetCommandeAsync(_commandeIdInitial.Value);
                if (commande != null) await AfficherCommandeAsync(commande);
                return;
            }
            if (_clientIdInitial.HasValue) await ChoisirCommandeAsync(_clientIdInitial.Value);
        }
        catch (Exception ex)
        {
            Theme.Error(ex, "le chargement de la commande");
        }
    }

    private async Task ChercherTicketAsync()
    {
        string ticket = _ticket.Text.Trim();
        if (ticket.Length == 0)
        {
            Theme.Info("Saisissez un numéro de ticket.");
            return;
        }
        try
        {
            CommandeDto? commande = await AppState.Api.GetCommandeByTicketAsync(ticket);
            if (commande == null)
            {
                Theme.Info("Aucune commande ne correspond à ce ticket.", "Ticket introuvable");
                return;
            }
            await AfficherCommandeAsync(commande);
        }
        catch (Exception ex)
        {
            Theme.Error(ex, "la recherche du ticket");
        }
    }

    private async Task ChoisirCommandeAsync(int? clientId = null)
    {
        try
        {
            using CommandePickerDialog dlg = new(clientId);
            if (dlg.ShowDialog(this) != DialogResult.OK || !dlg.SelectedCommandeId.HasValue) return;
            CommandeDto? commande = dlg.SelectedCommande
                                    ?? await AppState.Api.GetCommandeAsync(dlg.SelectedCommandeId.Value);
            if (commande != null) await AfficherCommandeAsync(commande);
        }
        catch (Exception ex)
        {
            Theme.Error(ex, "la sélection de la commande");
        }
    }

    private async Task ChoisirParClientAsync()
    {
        try
        {
            using ClientPickerDialog dlg = new("Choisir le client");
            if (dlg.ShowDialog(this) != DialogResult.OK || !dlg.SelectedClientId.HasValue) return;
            await ChoisirCommandeAsync(dlg.SelectedClientId.Value);
        }
        catch (Exception ex)
        {
            Theme.Error(ex, "la sélection du client");
        }
    }

    private async Task AfficherCommandeAsync(CommandeDto commande)
    {
        _commande = commande;
        _ticket.Text = commande.NumeroTicket;
        RemplacerInfo(commande);

        List<LigneRetraitVue> lignes = commande.Articles.Select(l => new LigneRetraitVue
        {
            Id = l.Id,
            Designation = l.Designation,
            Categorie = l.CategorieNom,
            Quantite = l.Quantite,
            DejaRetire = l.QuantiteDejaRetiree,
            Reste = Math.Max(0, l.Quantite - l.QuantiteDejaRetiree),
            ARetirer = 0,
            Express = l.EstExpress ? "EXPRESS" : string.Empty,
            Statut = l.StatutLibelle,
            PrixUnitaire = l.PrixUnitaire,
            Supplement = l.SupplementExpress,
            Montant = 0
        }).ToList();
        _grid.DataSource = new BindingList<LigneRetraitVue>(lignes);
        _vide.Visible = _grid.Rows.Count == 0;
        _vide.BringToFront();

        _maj = true;
        _montant.Text = "0";
        _montantModifie = false;
        _maj = false;
        MajSuggestion();
        await ChargerHistoriqueAsync(commande);
    }

    private void RemplacerInfo(CommandeDto commande)
    {
        RoundedPanel carte = UiFactory.InfoCard(
            "Commande " + commande.NumeroTicket,
            ("Client", commande.ClientNom),
            ("Dépôt", Theme.Date(commande.DateDepot)),
            ("Total", Theme.Money(commande.Total)),
            ("Payé", Theme.Money(commande.Paye)),
            ("Solde", Theme.Money(commande.Solde)),
            ("Statut", commande.ResumeStatut));
        _infoHost.Controls.Clear();
        carte.Dock = DockStyle.Fill;
        _infoHost.Controls.Add(carte);
    }

    private async Task ChargerHistoriqueAsync(CommandeDto commande)
    {
        try
        {
            List<RetraitDto> retraits = await AppState.Api.GetRetraitsClientAsync(commande.ClientId);
            Grids.Retraits.Remplir(_gridHistorique, retraits);
            _videHistorique.Visible = _gridHistorique.Rows.Count == 0;
            _videHistorique.BringToFront();
        }
        catch (Exception ex)
        {
            Theme.Error(ex, "le chargement des retraits");
        }
    }

    private void ToutSelectionner(object? sender, EventArgs e)
    {
        if (_grid.DataSource is not BindingList<LigneRetraitVue> lignes)
        {
            Theme.Info("Sélectionnez d'abord une commande.");
            return;
        }
        _maj = true;
        try
        {
            foreach (LigneRetraitVue ligne in lignes)
            {
                ligne.ARetirer = ligne.Reste;
                ligne.Montant = ligne.ARetirer * (ligne.PrixUnitaire +
                    (ligne.Express.Contains("EXPRESS", StringComparison.OrdinalIgnoreCase) ? ligne.Supplement : 0));
            }
            _grid.Refresh();
        }
        finally
        {
            _maj = false;
        }
        MajSuggestion();
    }

    private void MajSuggestion()
    {
        decimal suggere = 0;
        if (_grid.DataSource is BindingList<LigneRetraitVue> lignes)
            suggere = lignes.Sum(l => l.Montant);
        if (_commande != null) suggere = Math.Min(suggere, _commande.Solde);
        _suggestion.Text = "Montant suggéré : " + Theme.Money(suggere);
        if (_commande != null && !_montantModifie && !_maj)
        {
            _maj = true;
            _montant.Text = suggere.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            _maj = false;
        }
    }

    private async Task EnregistrerAsync()
    {
        if (_enregistrementEnCours) return;
        if (_commande == null)
        {
            Theme.Info("Sélectionnez d'abord une commande.", "Retrait");
            return;
        }
        _grid.EndEdit();
        RetraitCreateDto retrait = new();
        if (_grid.DataSource is BindingList<LigneRetraitVue> lignes)
        {
            foreach (LigneRetraitVue ligne in lignes)
            {
                if (ligne.ARetirer <= 0) continue;
                if (ligne.ARetirer > ligne.Reste)
                {
                    Theme.Info("La quantité retirée de « " + ligne.Designation + " » n'est pas valide.", "Retrait invalide");
                    return;
                }
                retrait.Articles.Add(new RetraitLigneDto
                {
                    ArticleCommandeId = ligne.Id,
                    Quantite = ligne.ARetirer
                });
            }
        }
        if (retrait.Articles.Count == 0)
        {
            Theme.Info("Sélectionnez au moins un article à retirer.", "Retrait invalide");
            return;
        }
        if (!Theme.TryDecimal(_montant.Text, out decimal verse) || verse < 0)
        {
            Theme.Info("Saisissez un montant valide (supérieur ou égal à zéro).", "Retrait invalide");
            return;
        }
        if (verse > _commande.Solde)
        {
            Theme.Info("Le montant versé dépasse le solde restant (" + Theme.Money(_commande.Solde) + ").", "Retrait invalide");
            return;
        }
        retrait.MontantVerse = verse;

        try
        {
            _enregistrementEnCours = true;
            Cursor = Cursors.WaitCursor;
            RetraitDto resultat = await AppState.Api.AddRetraitAsync(_commande.Id, retrait);
            CommandeDto? miseAJour = await AppState.Api.GetCommandeAsync(_commande.Id);
            if (miseAJour != null) await AfficherCommandeAsync(miseAJour);
            Theme.Info(
                "Retrait enregistré.\n\nTicket : " + (string.IsNullOrWhiteSpace(resultat.NumeroTicket)
                    ? _commande.NumeroTicket : resultat.NumeroTicket) +
                "\nArticles : " + resultat.Articles.Count +
                "\nMontant versé : " + Theme.Money(resultat.MontantVerse) +
                "\nDate : " + Theme.DateHeure(resultat.DateRetrait),
                "Retrait confirmé");
        }
        catch (Exception ex)
        {
            Theme.Error(ex, "l'enregistrement du retrait");
        }
        finally
        {
            _enregistrementEnCours = false;
            Cursor = Cursors.Default;
        }
    }
}
