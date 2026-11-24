using Pressing.Desktop.Api;
using Pressing.Shared.Dto;

namespace Pressing.Desktop.Ui;

/// <summary>Recherche et sélection d'une commande par ticket ou par client.</summary>
public sealed class CommandePickerDialog : PressingForm
{
    private readonly TextBox _filtre;
    private readonly DataGridView _grid;
    private readonly Label _vide;
    private readonly int? _clientId;
    private List<CommandeDto> _commandes = new();

    public int? SelectedCommandeId { get; private set; }
    public CommandeDto? SelectedCommande { get; private set; }

    public CommandePickerDialog(int? clientId = null, string titreFenetre = "Choisir une commande")
    {
        _clientId = clientId;
        Text = titreFenetre;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1050, 600);
        MinimumSize = new Size(760, 420);
        BackColor = Theme.Light;
        ShowInTaskbar = false;
        MinimizeBox = false;

        TableLayoutPanel racine = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16)
        };
        racine.RowStyles.Add(new RowStyle(SizeType.Absolute, 84F));
        racine.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        racine.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));

        RoundedPanel haut = new() { Padding = new Padding(12), Dock = DockStyle.Fill };
        Label titre = new()
        {
            Text = _clientId.HasValue
                ? "COMMANDES DU CLIENT — saisissez un ticket ou un nom pour filtrer"
                : "RECHERCHER — ticket, client, nom ou numéro",
            Dock = DockStyle.Top,
            Height = 20,
            Font = Theme.F(8.5f, FontStyle.Bold),
            ForeColor = Theme.Muted,
            BackColor = Color.Transparent
        };
        TableLayoutPanel ligne = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(0, 4, 0, 0)
        };
        ligne.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        ligne.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
        ligne.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));

        _filtre = Theme.Texte();
        _filtre.PlaceholderText = "Ex. PRESS-2026-00001 ou nom du client";
        _filtre.Dock = DockStyle.Fill;
        _filtre.TextChanged += (_, _) => AppliquerFiltre();
        ligne.Controls.Add(_filtre, 0, 0);

        FlatButton parTicket = new() { Text = "Par ticket", Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0) };
        parTicket.SetSecondary();
        parTicket.Click += async (_, _) => await ChercherTicketAsync();
        ligne.Controls.Add(parTicket, 1, 0);

        FlatButton actualiser = new() { Text = "Actualiser", Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0) };
        actualiser.SetSecondary();
        actualiser.Click += async (_, _) => await ChargerAsync();
        ligne.Controls.Add(actualiser, 2, 0);

        haut.Controls.Add(ligne);
        haut.Controls.Add(titre);

        RoundedPanel centre = UiFactory.Section("Commandes", out _grid, out _vide);
        Grids.Commandes.Configure(_grid);
        _grid.CellDoubleClick += (_, _) => Selectionner();

        Panel bas = new() { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        FlatButton annuler = new() { Text = "Annuler", Width = 110, Dock = DockStyle.Right };
        annuler.SetSecondary();
        annuler.Click += (_, _) => DialogResult = DialogResult.Cancel;
        FlatButton selectionner = new()
        {
            Text = "Sélectionner", Width = 130, Dock = DockStyle.Right, Margin = new Padding(0, 0, 8, 0)
        };
        selectionner.Click += (_, _) => Selectionner();
        bas.Controls.Add(annuler);
        bas.Controls.Add(selectionner);

        racine.Controls.Add(haut, 0, 0);
        racine.Controls.Add(centre, 0, 1);
        racine.Controls.Add(bas, 0, 2);
        Controls.Add(racine);

        AcceptButton = selectionner;
        CancelButton = annuler;
        Shown += async (_, _) => await ChargerAsync();
    }

    private async Task ChargerAsync()
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            _commandes = await AppState.Api.GetCommandesAsync(_clientId);
            AppliquerFiltre();
        }
        catch (Exception ex)
        {
            Theme.Error(ex, "le chargement des commandes");
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void AppliquerFiltre()
    {
        string q = _filtre.Text.Trim();
        IEnumerable<CommandeDto> liste = _commandes;
        if (q.Length > 0)
        {
            string query = q.ToLowerInvariant();
            liste = liste.Where(c =>
                c.NumeroTicket.ToLowerInvariant().Contains(query) ||
                c.ClientNom.ToLowerInvariant().Contains(query));
        }

        Grids.Commandes.Remplir(_grid, liste);
        _vide.Visible = _grid.Rows.Count == 0;
        _vide.BringToFront();
    }

    private async Task ChercherTicketAsync()
    {
        string ticket = _filtre.Text.Trim();
        if (ticket.Length == 0)
        {
            Theme.Info("Saisissez d'abord un numéro de ticket.");
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

            SelectedCommande = commande;
            SelectedCommandeId = commande.Id;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            Theme.Error(ex, "la recherche par ticket");
        }
    }

    private void Selectionner()
    {
        CommandeVue? vue = Grids.Commandes.Selection(_grid);
        if (vue == null)
        {
            Theme.Info("Sélectionnez d'abord une commande.");
            return;
        }

        SelectedCommandeId = vue.Id;
        SelectedCommande = _commandes.FirstOrDefault(c => c.Id == vue.Id);
        DialogResult = DialogResult.OK;
        Close();
    }
}
