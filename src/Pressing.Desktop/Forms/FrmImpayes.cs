using System.ComponentModel;
using Pressing.Desktop.Api;
using Pressing.Desktop.Ui;
using Pressing.Shared.Dto;

namespace Pressing.Desktop.Forms;

/// <summary>Liste des clients en retard de paiement et de leurs tickets.</summary>
public sealed class FrmImpayes : PressingForm
{
    private DataGridView _grid = new();
    private DataGridView _gridTickets = new();
    private Label _vide = Theme.EmptyLabel();
    private Label _videTickets = Theme.EmptyLabel();
    private readonly Label _total = Theme.Label("Solde total : —", Theme.F(12f, FontStyle.Bold), Theme.Red);
    private List<ImpayeDto> _impayes = new();

    private sealed class TicketLigne
    {
        public int CommandeId { get; set; }
        public string Ticket { get; set; } = string.Empty;
        public string Depot { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public decimal Paye { get; set; }
        public decimal Solde { get; set; }
    }

    public FrmImpayes()
    {
        Text = "Impayés";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1120, 760);
        MinimumSize = new Size(860, 560);
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
        racine.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));
        racine.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        racine.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));

        RoundedPanel resume = new() { Dock = DockStyle.Fill, Padding = new Padding(14, 8, 14, 8) };
        _total.Dock = DockStyle.Left;
        Label note = Theme.Label(
            "Double-cliquez sur un ticket pour ouvrir le paiement correspondant.",
            Theme.F(9f), Theme.Muted, DockStyle.Right);
        resume.Controls.Add(note);
        resume.Controls.Add(_total);
        racine.Controls.Add(resume, 0, 0);

        TableLayoutPanel grilles = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        grilles.RowStyles.Add(new RowStyle(SizeType.Percent, 52F));
        grilles.RowStyles.Add(new RowStyle(SizeType.Percent, 48F));

        RoundedPanel panneauImpayes = UiFactory.Section("Clients avec solde restant", out _grid, out _vide);
        Grids.Impayes.Configure(_grid);
        _grid.SelectionChanged += (_, _) => AfficherTickets();
        _grid.CellDoubleClick += (_, _) => OuvrirFicheClient();
        grilles.Controls.Add(panneauImpayes, 0, 0);

        RoundedPanel panneauTickets = UiFactory.Section("Tickets impayés du client sélectionné", out _gridTickets, out _videTickets);
        _gridTickets.SelectionChanged += (_, _) => { };
        _gridTickets.CellDoubleClick += (_, _) => OuvrirPaiement();
        ConfigurerTickets();
        grilles.Controls.Add(panneauTickets, 0, 1);
        racine.Controls.Add(grilles, 0, 1);

        Panel barre = new() { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        FlatButton fermer = new() { Text = "Fermer", Width = 110, Dock = DockStyle.Right };
        fermer.SetSecondary();
        fermer.Click += (_, _) => Close();
        FlatButton actualiser = new() { Text = "Actualiser", Width = 120, Dock = DockStyle.Right, Margin = new Padding(0, 0, 8, 0) };
        actualiser.SetSecondary();
        actualiser.Click += async (_, _) => await ChargerAsync();
        FlatButton payer = new() { Text = "Payer le ticket", Width = 150, Dock = DockStyle.Right, Margin = new Padding(0, 0, 8, 0) };
        payer.SetColors(Theme.Green, Color.FromArgb(16, 124, 58));
        payer.Click += (_, _) => OuvrirPaiement();
        FlatButton fiche = new() { Text = "Fiche client", Width = 140, Dock = DockStyle.Right, Margin = new Padding(0, 0, 8, 0) };
        fiche.Click += (_, _) => OuvrirFicheClient();
        barre.Controls.Add(fermer);
        barre.Controls.Add(actualiser);
        barre.Controls.Add(payer);
        barre.Controls.Add(fiche);
        racine.Controls.Add(barre, 0, 2);
        Controls.Add(racine);
    }

    private void ConfigurerTickets()
    {
        Theme.StyleGrid(_gridTickets);
        _gridTickets.Columns.Clear();
        _gridTickets.Columns.Add(Grids.Col("CommandeId", "Id", 1, visible: false));
        _gridTickets.Columns.Add(Grids.Col("Ticket", "Ticket", 170));
        _gridTickets.Columns.Add(Grids.Col("Depot", "Date dépôt", 120));
        _gridTickets.Columns.Add(Grids.Col("Total", "Total (F)", 120, "N0"));
        _gridTickets.Columns.Add(Grids.Col("Paye", "Payé (F)", 120, "N0"));
        _gridTickets.Columns.Add(Grids.Col("Solde", "Solde (F)", 120, "N0"));
    }

    private async Task ChargerAsync()
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            _impayes = await AppState.Api.GetImpayesAsync();
            Grids.Impayes.Remplir(_grid, _impayes);
            _total.Text = "Solde total impayé : " + Theme.Money(_impayes.Sum(i => i.Solde)) +
                          "   •   " + _impayes.Count + " client(s)";
            AfficherTickets();
            _vide.Visible = _grid.Rows.Count == 0;
            _vide.BringToFront();
        }
        catch (Exception ex)
        {
            Theme.Error(ex, "le chargement des impayés");
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void AfficherTickets()
    {
        ImpayeVue? impaye = Grids.Impayes.Selection(_grid);
        ImpayeDto? dto = impaye == null ? null : _impayes.FirstOrDefault(i => i.ClientId == impaye.ClientId);
        List<TicketLigne> tickets = dto?.Commandes.Select(c => new TicketLigne
        {
            CommandeId = c.CommandeId,
            Ticket = c.NumeroTicket,
            Depot = Theme.Date(c.DateDepot),
            Total = c.Total,
            Paye = c.Paye,
            Solde = c.Solde
        }).ToList() ?? new List<TicketLigne>();
        _gridTickets.DataSource = new BindingList<TicketLigne>(tickets);
        _videTickets.Visible = _gridTickets.Rows.Count == 0;
        _videTickets.BringToFront();
    }

    private void OuvrirFicheClient()
    {
        ImpayeVue? impaye = Grids.Impayes.Selection(_grid);
        if (impaye == null)
        {
            Theme.Info("Sélectionnez d'abord un client.");
            return;
        }
        using FrmClient formulaire = new(impaye.ClientId);
        formulaire.ShowDialog(this);
    }

    private async void OuvrirPaiement()
    {
        try
        {
            if (_gridTickets.CurrentRow?.DataBoundItem is not TicketLigne ticket)
            {
                Theme.Info("Sélectionnez d'abord un ticket impayé.");
                return;
            }
            CommandeDto? commande = await AppState.Api.GetCommandeAsync(ticket.CommandeId);
            if (commande == null)
            {
                Theme.Info("Cette commande n'existe plus.", "Ticket introuvable");
                return;
            }
            using FrmPaiement formulaire = new(commandeId: commande.Id);
            formulaire.ShowDialog(this);
            await ChargerAsync();
        }
        catch (Exception ex)
        {
            Theme.Error(ex, "l'ouverture du paiement");
        }
    }
}
