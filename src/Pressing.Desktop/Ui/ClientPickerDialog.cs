using System.ComponentModel;
using Pressing.Desktop.Api;
using Pressing.Desktop.Forms;
using Pressing.Shared.Dto;

namespace Pressing.Desktop.Ui;

/// <summary>Recherche et sélection d'un client (réutilisable).</summary>
public sealed class ClientPickerDialog : PressingForm
{
    private readonly TextBox _recherche;
    private readonly DataGridView _grid;
    private readonly Label _vide;
    private readonly List<ClientDto> _resultatsInitiaux;

    private sealed class ClientLigne
    {
        public int Id { get; set; }
        public string Nom { get; set; } = string.Empty;
        public string Prenom { get; set; } = string.Empty;
        public string Telephone { get; set; } = string.Empty;
        public string WhatsApp { get; set; } = string.Empty;
    }

    public int? SelectedClientId { get; private set; }
    public ClientDto? SelectedClient { get; private set; }

    public ClientPickerDialog(
        string titreFenetre = "Choisir un client",
        string? rechercheInitiale = null,
        List<ClientDto>? resultats = null)
    {
        Text = titreFenetre;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(920, 580);
        MinimumSize = new Size(680, 420);
        BackColor = Theme.Light;
        ShowInTaskbar = false;
        MinimizeBox = false;
        _resultatsInitiaux = resultats ?? new List<ClientDto>();

        TableLayoutPanel racine = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16)
        };
        racine.RowStyles.Add(new RowStyle(SizeType.Absolute, 88F));
        racine.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        racine.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));

        RoundedPanel haut = new() { Dock = DockStyle.Fill, Padding = new Padding(12) };
        Label titre = new()
        {
            Text = "RECHERCHER UN CLIENT",
            Dock = DockStyle.Top,
            Height = 20,
            Font = Theme.F(8.5f, FontStyle.Bold),
            ForeColor = Theme.Muted,
            BackColor = Color.Transparent
        };

        TableLayoutPanel ligneRecherche = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Padding = new Padding(0, 4, 0, 0)
        };
        ligneRecherche.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        ligneRecherche.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
        ligneRecherche.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140F));
        ligneRecherche.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));

        _recherche = Theme.Texte(rechercheInitiale);
        _recherche.PlaceholderText = "Nom, prénom, téléphone, WhatsApp ou ticket";
        _recherche.Dock = DockStyle.Fill;
        ligneRecherche.Controls.Add(_recherche, 0, 0);

        FlatButton rechercher = new() { Text = "Rechercher", Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0) };
        rechercher.Click += async (_, _) => await ChargerAsync();
        ligneRecherche.Controls.Add(rechercher, 1, 0);

        FlatButton nouveau = new()
        {
            Text = "Nouveau client",
            Dock = DockStyle.Fill,
            Margin = new Padding(6, 0, 0, 0)
        };
        nouveau.SetOutline(Theme.Green);
        nouveau.Click += NouveauClient;
        ligneRecherche.Controls.Add(nouveau, 2, 0);

        FlatButton actualiser = new() { Text = "Tout voir", Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0) };
        actualiser.SetSecondary();
        actualiser.Click += async (_, _) =>
        {
            _recherche.Text = string.Empty;
            await ChargerAsync();
        };
        ligneRecherche.Controls.Add(actualiser, 3, 0);

        haut.Controls.Add(ligneRecherche);
        haut.Controls.Add(titre);

        RoundedPanel centre = UiFactory.Section("Clients", out _grid, out _vide);
        ConfigurerGrille();

        Panel bas = new() { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        FlatButton annuler = new() { Text = "Annuler", Width = 110, Dock = DockStyle.Right };
        annuler.SetSecondary();
        annuler.Click += (_, _) => DialogResult = DialogResult.Cancel;
        FlatButton selectionner = new() { Text = "Sélectionner", Width = 130, Dock = DockStyle.Right, Margin = new Padding(0, 0, 8, 0) };
        selectionner.Click += Selectionner;
        bas.Controls.Add(annuler);
        bas.Controls.Add(selectionner);

        racine.Controls.Add(haut, 0, 0);
        racine.Controls.Add(centre, 0, 1);
        racine.Controls.Add(bas, 0, 2);
        Controls.Add(racine);

        AcceptButton = rechercher;
        CancelButton = annuler;
        _recherche.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) _ = ChargerAsync();
        };
        _grid.CellDoubleClick += (_, _) => Selectionner(this, EventArgs.Empty);

        Shown += async (_, _) =>
        {
            if (_resultatsInitiaux.Count > 0) Afficher(_resultatsInitiaux);
            else await ChargerAsync();
            _recherche.Focus();
        };
    }

    private void ConfigurerGrille()
    {
        Theme.StyleGrid(_grid);
        _grid.Columns.Add(Grids.Col("Id", "Id", 1, visible: false));
        _grid.Columns.Add(Grids.Col("Nom", "Nom", 130));
        _grid.Columns.Add(Grids.Col("Prenom", "Prénom", 130));
        _grid.Columns.Add(Grids.Col("Telephone", "Téléphone", 130));
        _grid.Columns.Add(Grids.Col("WhatsApp", "WhatsApp", 130));
    }

    private async Task ChargerAsync()
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            List<ClientDto> clients = await AppState.Api.GetClientsAsync(_recherche.Text);
            Afficher(clients);
        }
        catch (Exception ex)
        {
            Theme.Error(ex, "la recherche de clients");
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void Afficher(List<ClientDto> clients)
    {
        List<ClientLigne> lignes = clients.Select(c => new ClientLigne
        {
            Id = c.Id, Nom = c.Nom, Prenom = c.Prenom,
            Telephone = c.Telephone, WhatsApp = c.WhatsApp
        }).ToList();
        _grid.DataSource = new BindingList<ClientLigne>(lignes);
        _vide.Visible = _grid.Rows.Count == 0;
        _vide.BringToFront();
    }

    private async void Selectionner(object? sender, EventArgs e)
    {
        if (_grid.CurrentRow?.DataBoundItem is not ClientLigne ligne)
        {
            Theme.Info("Sélectionnez d'abord un client.");
            return;
        }

        try
        {
            SelectedClientId = ligne.Id;
            SelectedClient = _resultatsInitiaux.FirstOrDefault(c => c.Id == ligne.Id)
                            ?? await AppState.Api.GetClientAsync(ligne.Id);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            Theme.Error(ex, "la sélection du client");
        }
    }

    private async void NouveauClient(object? sender, EventArgs e)
    {
        try
        {
            using FrmClient formulaire = new();
            formulaire.ShowDialog(this);
            if (formulaire.ClientId.HasValue)
            {
                SelectedClientId = formulaire.ClientId;
                SelectedClient = await AppState.Api.GetClientAsync(formulaire.ClientId.Value);
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                await ChargerAsync();
            }
        }
        catch (Exception ex)
        {
            Theme.Error(ex, "la création du client");
        }
    }
}
