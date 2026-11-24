using Pressing.Desktop.Api;
using Pressing.Desktop.Ui;
using Pressing.Shared.Dto;

namespace Pressing.Desktop.Forms;

/// <summary>Création d'un dépôt avec plusieurs articles et tarification automatique.</summary>
public sealed class FrmDepot : PressingForm
{
    private readonly int? _clientIdInitial;
    private ClientDto? _client;
    private List<ArticleCatalogueDto> _catalogue = new();
    private List<TarifDto> _tarifs = new();
    private Dictionary<int, TarifDto> _tarifsParArticle = new();
    private bool _majEnCours;
    private bool _enregistrementEnCours;

    private readonly DataGridView _grid = new();
    private readonly TextBox _rechercheClient = Theme.Texte();
    private readonly TextBox _acompte = Theme.Texte("0");
    private readonly Label _clientLabel = Theme.Label("Aucun client sélectionné", Theme.F(10f, FontStyle.Bold), Theme.Red);
    private readonly Label _totalLabel = Theme.Label("Total : 0 F", Theme.F(13f, FontStyle.Bold), Theme.Navy);
    private readonly Label _acompteLabel = Theme.Label("Acompte : 0 F", Theme.F(9.5f), Theme.Blue);
    private readonly Label _resteLabel = Theme.Label("Reste à payer : 0 F", Theme.F(11f, FontStyle.Bold), Theme.Red);
    private readonly FlatButton _valider = new() { Text = "Valider le dépôt", Width = 170, Height = 42 };

    private sealed class CatalogueItem
    {
        public int Id { get; set; }
        public string Libelle { get; set; } = string.Empty;
        public override string ToString() => Libelle;
    }

    public FrmDepot(int? clientId = null)
    {
        _clientIdInitial = clientId;
        Text = "Nouveau dépôt";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1250, 800);
        MinimumSize = new Size(980, 620);
        BackColor = Theme.Light;
        ConstruireInterface();
        Shown += async (_, _) => await ChargerAsync();
    }

    private void ConstruireInterface()
    {
        TableLayoutPanel racine = new()
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(14)
        };
        racine.RowStyles.Add(new RowStyle(SizeType.Absolute, 116F));
        racine.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        racine.RowStyles.Add(new RowStyle(SizeType.Absolute, 122F));
        racine.Controls.Add(ConstruireClient(), 0, 0);
        racine.Controls.Add(ConstruireArticles(), 0, 1);
        racine.Controls.Add(ConstruirePied(), 0, 2);
        Controls.Add(racine);
    }

    private RoundedPanel ConstruireClient()
    {
        RoundedPanel panneau = new() { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 8) };
        Label titre = new()
        {
            Text = "1. CLIENT", Dock = DockStyle.Top, Height = 20,
            Font = Theme.F(8.5f, FontStyle.Bold), ForeColor = Theme.Muted, BackColor = Color.Transparent
        };
        TableLayoutPanel table = new()
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Padding = new Padding(0, 4, 0, 0)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140F));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));

        _rechercheClient.PlaceholderText = "Nom, téléphone ou ticket du client";
        _rechercheClient.Dock = DockStyle.Fill;
        _rechercheClient.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) _ = ChoisirClientAsync();
        };
        table.Controls.Add(_rechercheClient, 0, 0);

        FlatButton rechercher = new() { Text = "Rechercher", Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0) };
        rechercher.Click += async (_, _) => await ChoisirClientAsync();
        table.Controls.Add(rechercher, 1, 0);

        FlatButton nouveau = new() { Text = "Nouveau client", Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0) };
        nouveau.SetColors(Theme.Green, Color.FromArgb(16, 124, 58));
        nouveau.Click += async (_, _) => await NouveauClientAsync();
        table.Controls.Add(nouveau, 2, 0);

        _clientLabel.Dock = DockStyle.Fill;
        table.SetColumnSpan(_clientLabel, 3);
        table.Controls.Add(_clientLabel, 0, 1);

        panneau.Controls.Add(table);
        panneau.Controls.Add(titre);
        return panneau;
    }

    private RoundedPanel ConstruireArticles()
    {
        RoundedPanel panneau = new() { Dock = DockStyle.Fill, Padding = new Padding(12) };
        Panel titre = new() { Dock = DockStyle.Top, Height = 46, BackColor = Color.Transparent };
        Label titreLabel = new()
        {
            Text = "2. ARTICLES DU DÉPÔT  —  prix et suppléments calculés automatiquement",
            Dock = DockStyle.Left,
            Width = 620,
            Font = Theme.F(11f, FontStyle.Bold),
            ForeColor = Theme.Navy,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft
        };
        FlatButton supprimer = new() { Text = "− Retirer la ligne", Width = 160, Dock = DockStyle.Right, Margin = new Padding(6, 4, 0, 0) };
        supprimer.SetColors(Theme.Red, Color.FromArgb(185, 28, 28));
        supprimer.Click += SupprimerLigne;
        FlatButton ajouter = new() { Text = "＋ Ajouter un article", Width = 180, Dock = DockStyle.Right, Margin = new Padding(6, 4, 0, 0) };
        ajouter.SetColors(Theme.Green, Color.FromArgb(16, 124, 58));
        ajouter.Click += (_, _) => AjouterLigne();
        titre.Controls.Add(supprimer);
        titre.Controls.Add(ajouter);
        titre.Controls.Add(titreLabel);

        Panel hote = new() { Dock = DockStyle.Fill, BackColor = Color.White };
        _grid.Dock = DockStyle.Fill;
        hote.Controls.Add(_grid);

        panneau.Controls.Add(hote);
        panneau.Controls.Add(titre);
        return panneau;
    }

    private RoundedPanel ConstruirePied()
    {
        RoundedPanel panneau = new() { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 8) };
        Label titre = new()
        {
            Text = "3. RÉCAPITULATIF", Dock = DockStyle.Top, Height = 18,
            Font = Theme.F(8.5f, FontStyle.Bold), ForeColor = Theme.Muted, BackColor = Color.Transparent
        };
        TableLayoutPanel table = new()
        {
            Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, Padding = new Padding(0, 4, 0, 0)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18F));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));

        _totalLabel.Dock = DockStyle.Fill;
        _acompteLabel.Dock = DockStyle.Fill;
        _resteLabel.Dock = DockStyle.Fill;
        table.Controls.Add(_totalLabel, 0, 0);
        table.Controls.Add(_acompteLabel, 1, 0);
        table.Controls.Add(_resteLabel, 2, 0);

        Label acompteTitre = new()
        {
            Text = "Acompte versé (facultatif) :", Dock = DockStyle.Left, Width = 190,
            Font = Theme.F(9f), ForeColor = Theme.Slate, BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _acompte.Width = 110;
        _acompte.Dock = DockStyle.Right;
        _acompte.TextChanged += (_, _) => RecalculerTotaux();
        Panel acomptePanel = new() { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        acomptePanel.Controls.Add(_acompte);
        acomptePanel.Controls.Add(acompteTitre);
        table.Controls.Add(acomptePanel, 0, 1);
        table.SetColumnSpan(acomptePanel, 3);

        FlatButton fermer = new() { Text = "Fermer", Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0) };
        fermer.SetSecondary();
        fermer.Click += (_, _) => Close();
        FlatButton valider = _valider;
        valider.Dock = DockStyle.Fill;
        valider.Click += async (_, _) => await ValiderAsync();
        TableLayoutPanel boutons = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        boutons.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        boutons.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        boutons.Controls.Add(valider, 0, 0);
        boutons.Controls.Add(fermer, 0, 1);
        table.Controls.Add(boutons, 3, 0);
        table.SetRowSpan(boutons, 2);

        panneau.Controls.Add(table);
        panneau.Controls.Add(titre);
        return panneau;
    }

    private async Task ChargerAsync()
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            Task<List<ArticleCatalogueDto>> tCatalogue = AppState.Api.GetCatalogueAsync();
            Task<List<TarifDto>> tTarifs = AppState.Api.GetTarifsAsync();
            await Task.WhenAll(tCatalogue, tTarifs);
            _catalogue = await tCatalogue;
            _tarifs = await tTarifs;
            _tarifsParArticle = _catalogue
                .Select(a => new { a.Id, Tarif = _tarifs.FirstOrDefault(t => t.CategorieId == a.CategorieId) })
                .Where(x => x.Tarif != null)
                .ToDictionary(x => x.Id, x => x.Tarif!);

            ConfigurerGrille();
            AjouterLigne();

            if (_clientIdInitial.HasValue)
            {
                _client = await AppState.Api.GetClientAsync(_clientIdInitial.Value);
                AfficherClient();
            }
        }
        catch (Exception ex)
        {
            Theme.Error(ex, "le chargement du catalogue");
            _valider.Enabled = false;
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void ConfigurerGrille()
    {
        Theme.StyleGrid(_grid, lectureSeule: false);
        _grid.Columns.Clear();

        List<CatalogueItem> items = _catalogue
            .Select(a => new CatalogueItem { Id = a.Id, Libelle = a.Nom + "  —  " + a.CategorieNom })
            .ToList();
        _grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "ArticleId",
            HeaderText = "Article (catégorie)",
            DataSource = items,
            DisplayMember = nameof(CatalogueItem.Libelle),
            ValueMember = nameof(CatalogueItem.Id),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 180
        });
        _grid.Columns.Add(Grids.Col("Quantite", "Qté", 55, lectureSeule: false));
        _grid.Columns.Add(Grids.Col("Designation", "Désignation (facultative)", 150, lectureSeule: false));
        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "EstExpress", HeaderText = "Exprès",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 60
        });
        _grid.Columns.Add(Grids.Col("Badge", "Option", 90));
        _grid.Columns.Add(Grids.Col("DateDepot", "Date dépôt", 105, lectureSeule: false));
        _grid.Columns.Add(Grids.Col("DatePrevue", "Retrait prévu", 110, lectureSeule: false));
        _grid.Columns.Add(Grids.Col("PrixUnitaire", "Prix (F)", 90, "N0"));
        _grid.Columns.Add(Grids.Col("Supplement", "Suppl. (F)", 90, "N0"));
        _grid.Columns.Add(Grids.Col("Montant", "Montant (F)", 110, "N0"));

        _grid.Columns["DateDepot"]!.DefaultCellStyle.Format = "dd/MM/yyyy";
        _grid.Columns["DatePrevue"]!.DefaultCellStyle.Format = "dd/MM/yyyy";
        foreach (DataGridViewRow ligne in _grid.Rows) ligne.Height = 32;
        _grid.RowTemplate.Height = 34;

        _grid.CellValueChanged += (_, e) =>
        {
            if (_majEnCours || e.RowIndex < 0 || e.RowIndex >= _grid.Rows.Count) return;
            DataGridViewRow ligne = _grid.Rows[e.RowIndex];
            string colonne = _grid.Columns[e.ColumnIndex].Name;
            if (colonne is "ArticleId" or "EstExpress" or "DateDepot")
            {
                TarifDto? tarif = TarifDe(ligne);
                DateTime depot = ligne.Cells["DateDepot"].Value is DateTime d ? d : DateTime.Today;
                bool express = Convert.ToBoolean(ligne.Cells["EstExpress"].Value ?? false);
                int jours = express ? tarif?.DelaiExpressJours ?? 1 : tarif?.DelaiNormalJours ?? 3;
                ligne.Cells["DatePrevue"].Value = depot.AddDays(jours);
            }
            RecalculerLigne(ligne);
            RecalculerTotaux();
        };
        _grid.CellFormatting += (s, e) =>
        {
            Theme.ExpressFormatting(s, e, "Badge");
            if (s is DataGridView grille && e.RowIndex >= 0 && grille.Columns[e.ColumnIndex].Name == "Montant")
            {
                e.CellStyle.Font = Theme.F(9f, FontStyle.Bold);
                e.CellStyle.ForeColor = Theme.Green;
            }
        };
        _grid.RowsRemoved += (_, _) => RecalculerTotaux();
    }

    private TarifDto? TarifDe(DataGridViewRow ligne)
    {
        int articleId = Convert.ToInt32(ligne.Cells["ArticleId"].Value ?? 0);
        return _tarifsParArticle.TryGetValue(articleId, out TarifDto? tarif) ? tarif : null;
    }

    private void AjouterLigne()
    {
        int index = _grid.Rows.Add();
        if (index < 0) return;
        DataGridViewRow ligne = _grid.Rows[index];
        TarifDto? premier = _tarifs.FirstOrDefault();
        _majEnCours = true;
        try
        {
            ligne.Cells["ArticleId"].Value = _catalogue.FirstOrDefault()?.Id ?? 0;
            ligne.Cells["Quantite"].Value = 1;
            ligne.Cells["Designation"].Value = string.Empty;
            ligne.Cells["EstExpress"].Value = false;
            ligne.Cells["DateDepot"].Value = DateTime.Today;
            ligne.Cells["DatePrevue"].Value = DateTime.Today.AddDays(premier?.DelaiNormalJours ?? 3);
        }
        finally
        {
            _majEnCours = false;
        }
        RecalculerLigne(ligne);
        RecalculerTotaux();
    }

    private void SupprimerLigne(object? sender, EventArgs e)
    {
        if (_grid.CurrentRow == null || _grid.CurrentRow.IsNewRow)
        {
            Theme.Info("Sélectionnez d'abord une ligne à supprimer.");
            return;
        }
        if (_grid.Rows.Count <= 1)
        {
            Theme.Info("Le dépôt doit contenir au moins une ligne.");
            return;
        }
        _grid.Rows.Remove(_grid.CurrentRow);
        RecalculerTotaux();
    }

    private void RecalculerLigne(DataGridViewRow ligne)
    {
        TarifDto? tarif = TarifDe(ligne);
        int quantite = Math.Max(1, Convert.ToInt32(ligne.Cells["Quantite"].Value ?? 1));
        bool express = Convert.ToBoolean(ligne.Cells["EstExpress"].Value ?? false);
        decimal prix = tarif?.Prix ?? 0;
        decimal supplement = express ? tarif?.SupplementExpress ?? 0 : 0;

        _majEnCours = true;
        try
        {
            ligne.Cells["Quantite"].Value = quantite;
            ligne.Cells["Badge"].Value = express ? "EXPRESS" : "Standard";
            ligne.Cells["PrixUnitaire"].Value = prix;
            ligne.Cells["Supplement"].Value = supplement;
            ligne.Cells["Montant"].Value = quantite * (prix + supplement);
        }
        finally
        {
            _majEnCours = false;
        }
    }

    private void RecalculerTotaux()
    {
        decimal total = 0;
        foreach (DataGridViewRow ligne in _grid.Rows)
        {
            if (ligne.IsNewRow) continue;
            TarifDto? tarif = TarifDe(ligne);
            int quantite = Math.Max(0, Convert.ToInt32(ligne.Cells["Quantite"].Value ?? 0));
            bool express = Convert.ToBoolean(ligne.Cells["EstExpress"].Value ?? false);
            total += quantite * ((tarif?.Prix ?? 0) + (express ? tarif?.SupplementExpress ?? 0 : 0));
        }
        decimal acompte = Theme.TryDecimal(_acompte.Text, out decimal valeur) && valeur >= 0 ? valeur : 0;
        _totalLabel.Text = "Total : " + Theme.Money(total);
        _acompteLabel.Text = "Acompte : " + Theme.Money(acompte);
        _resteLabel.Text = "Reste à payer : " + Theme.Money(Math.Max(0, total - acompte));
    }

    private void AfficherClient()
    {
        _clientLabel.Text = _client == null
            ? "Aucun client sélectionné"
            : "Client : " + _client.NomComplet + "   •   Tél. " + _client.Telephone +
              "   •   WhatsApp " + _client.WhatsApp;
        _clientLabel.ForeColor = _client == null ? Theme.Red : Theme.Green;
        _rechercheClient.Text = _client?.Telephone ?? string.Empty;
    }

    private async Task ChoisirClientAsync()
    {
        try
        {
            using ClientPickerDialog dlg = new("Choisir le client du dépôt", _rechercheClient.Text);
            if (dlg.ShowDialog(this) != DialogResult.OK || !dlg.SelectedClientId.HasValue) return;
            _client = await AppState.Api.GetClientAsync(dlg.SelectedClientId.Value);
            AfficherClient();
        }
        catch (Exception ex)
        {
            Theme.Error(ex, "la sélection du client");
        }
    }

    private async Task NouveauClientAsync()
    {
        try
        {
            using FrmClient formulaire = new(demanderClient: false);
            formulaire.ShowDialog(this);
            if (!formulaire.ClientId.HasValue) return;
            _client = await AppState.Api.GetClientAsync(formulaire.ClientId.Value);
            AfficherClient();
        }
        catch (Exception ex)
        {
            Theme.Error(ex, "la création du client");
        }
    }

    private async Task ValiderAsync()
    {
        if (_enregistrementEnCours) return;
        if (_client == null)
        {
            Theme.Info("Sélectionnez d'abord un client.", "Dépôt");
            return;
        }

        try
        {
            _enregistrementEnCours = true;
            _grid.EndEdit();
            CommandeCreateDto commande = new() { ClientId = _client.Id, DateDepot = DateTime.Now };
            decimal total = 0;
            foreach (DataGridViewRow ligne in _grid.Rows)
            {
                if (ligne.IsNewRow) continue;
                int articleId = Convert.ToInt32(ligne.Cells["ArticleId"].Value ?? 0);
                int quantite = Convert.ToInt32(ligne.Cells["Quantite"].Value ?? 0);
                if (articleId <= 0 || quantite <= 0)
                {
                    Theme.Info("Vérifiez l'article et la quantité de chaque ligne.", "Dépôt invalide");
                    return;
                }
                DateTime depot = ligne.Cells["DateDepot"].Value is DateTime d ? d : DateTime.Today;
                DateTime prevue = ligne.Cells["DatePrevue"].Value is DateTime p ? p : depot.AddDays(3);
                commande.Lignes.Add(new LigneCreateDto
                {
                    ArticleId = articleId,
                    Quantite = quantite,
                    EstExpress = Convert.ToBoolean(ligne.Cells["EstExpress"].Value ?? false),
                    Designation = ligne.Cells["Designation"].Value?.ToString(),
                    DateDepot = depot,
                    DateRetraitPrevue = prevue
                });
                total += Convert.ToDecimal(ligne.Cells["Montant"].Value ?? 0);
            }

            if (commande.Lignes.Count == 0)
            {
                Theme.Info("Ajoutez au moins un article au dépôt.", "Dépôt invalide");
                return;
            }

            decimal acompte = Theme.TryDecimal(_acompte.Text, out decimal valeur) ? valeur : 0;
            if (acompte < 0)
            {
                Theme.Info("L'acompte ne peut pas être négatif.", "Dépôt invalide");
                return;
            }
            if (acompte > total)
            {
                Theme.Info("L'acompte ne peut pas dépasser le total du dépôt (" + Theme.Money(total) + ").", "Dépôt invalide");
                return;
            }
            commande.MontantAcompte = acompte;

            Cursor = Cursors.WaitCursor;
            CommandeDto cree = await AppState.Api.CreateCommandeAsync(commande);
            Theme.Info(
                "Dépôt enregistré avec succès.\n\n" +
                "Ticket : " + cree.NumeroTicket + "\n" +
                "Client : " + cree.ClientNom + "\n" +
                "Total : " + Theme.Money(cree.Total) + "\n" +
                "Acompte : " + Theme.Money(cree.Paye) + "\n" +
                "Reste à payer : " + Theme.Money(cree.Solde),
                "Dépôt confirmé");

            _grid.Rows.Clear();
            AjouterLigne();
            _acompte.Text = "0";
            RecalculerTotaux();
        }
        catch (Exception ex)
        {
            Theme.Error(ex, "l'enregistrement du dépôt");
        }
        finally
        {
            _enregistrementEnCours = false;
            Cursor = Cursors.Default;
        }
    }
}
