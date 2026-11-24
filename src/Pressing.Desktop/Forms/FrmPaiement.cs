using Pressing.Desktop.Api;
using Pressing.Desktop.Ui;
using Pressing.Shared.Dto;
using Pressing.Shared.Models;

namespace Pressing.Desktop.Forms;

/// <summary>Enregistrement d'un paiement (acompte ou direct) sur une commande.</summary>
public sealed class FrmPaiement : PressingForm
{
    private readonly int? _commandeIdInitial;
    private readonly int? _clientIdInitial;
    private readonly string? _ticketInitial;
    private CommandeDto? _commande;
    private bool _enregistrementEnCours;

    private readonly TextBox _ticket = Theme.Texte();
    private readonly TextBox _montant = Theme.Texte("0");
    private readonly ComboBox _type = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Font = Theme.F(9.5f),
        Width = 180,
        Height = 32
    };
    private DataGridView _grid = new();
    private Label _vide = Theme.EmptyLabel();
    private readonly Panel _infoHost = new()
    {
        Dock = DockStyle.Top, Height = 92, BackColor = Color.Transparent, Padding = new Padding(0, 0, 0, 8)
    };

    public FrmPaiement(int? commandeId = null, int? clientId = null, string? ticket = null)
    {
        _commandeIdInitial = commandeId;
        _clientIdInitial = clientId;
        _ticketInitial = ticket;
        Text = "Paiement d'une commande";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1100, 740);
        MinimumSize = new Size(860, 560);
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
        racine.RowStyles.Add(new RowStyle(SizeType.Absolute, 132F));
        racine.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        racine.Controls.Add(ConstruireSelection(), 0, 0);
        racine.Controls.Add(_infoHost, 0, 1);
        racine.Controls.Add(ConstruireSaisie(), 0, 2);
        racine.Controls.Add(ConstruireHistorique(), 0, 3);
        Controls.Add(racine);
    }

    private RoundedPanel ConstruireSelection()
    {
        RoundedPanel panneau = new() { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 8) };
        Label titre = new()
        {
            Text = "1. COMMANDE À PAYER", Dock = DockStyle.Top, Height = 20,
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

    private RoundedPanel ConstruireSaisie()
    {
        RoundedPanel panneau = new() { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 8) };
        Label titre = new()
        {
            Text = "2. PAIEMENT", Dock = DockStyle.Top, Height = 20,
            Font = Theme.F(8.5f, FontStyle.Bold), ForeColor = Theme.Muted, BackColor = Color.Transparent
        };
        TableLayoutPanel table = new() { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Padding = new Padding(0, 4, 0, 0) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));

        _type.Items.Add(TypePaiement.Acompte.Libelle());
        _type.Items.Add(TypePaiement.Direct.Libelle());
        _type.SelectedIndex = 0;
        table.Controls.Add(Theme.Champ("Type de paiement", _type), 0, 0);
        table.Controls.Add(Theme.Champ("Montant (F)", _montant), 1, 0);

        FlatButton enregistrer = new() { Text = "Enregistrer le paiement", Width = 210, Height = 40, Dock = DockStyle.Left, Margin = new Padding(0, 0, 8, 0) };
        enregistrer.SetColors(Theme.Green, Color.FromArgb(16, 124, 58));
        enregistrer.Click += async (_, _) => await EnregistrerAsync();
        FlatButton fermer = new() { Text = "Fermer", Width = 110, Height = 40, Dock = DockStyle.Left };
        fermer.SetSecondary();
        fermer.Click += (_, _) => Close();
        Panel boutons = new() { Dock = DockStyle.Fill, BackColor = Color.Transparent };
        boutons.Controls.Add(fermer);
        boutons.Controls.Add(enregistrer);
        table.Controls.Add(boutons, 2, 0);
        table.SetColumnSpan(boutons, 1);

        panneau.Controls.Add(table);
        panneau.Controls.Add(titre);
        return panneau;
    }

    private RoundedPanel ConstruireHistorique()
    {
        RoundedPanel panneau = UiFactory.Section("Historique des paiements de la commande", out _grid, out _vide);
        Grids.Paiements.Configure(_grid);
        return panneau;
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
        RoundedPanel carte = UiFactory.InfoCard(
            "Commande " + commande.NumeroTicket,
            ("Client", commande.ClientNom),
            ("Dépôt", Theme.Date(commande.DateDepot)),
            ("Total", Theme.Money(commande.Total)),
            ("Payé", Theme.Money(commande.Paye)),
            ("Solde", Theme.Money(commande.Solde)));
        _infoHost.Controls.Clear();
        carte.Dock = DockStyle.Fill;
        _infoHost.Controls.Add(carte);
        _montant.Text = commande.Solde.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        List<PaiementDto> paiements = await AppState.Api.GetPaiementsAsync(commande.Id);
        Grids.Paiements.Remplir(_grid, paiements);
        _vide.Visible = _grid.Rows.Count == 0;
        _vide.BringToFront();
    }

    private async Task EnregistrerAsync()
    {
        if (_enregistrementEnCours) return;
        if (_commande == null)
        {
            Theme.Info("Sélectionnez d'abord une commande.", "Paiement");
            return;
        }
        if (!Theme.TryDecimal(_montant.Text, out decimal montant) || montant <= 0)
        {
            Theme.Info("Saisissez un montant supérieur à zéro.", "Paiement invalide");
            return;
        }
        if (montant > _commande.Solde)
        {
            Theme.Info("Le montant dépasse le solde restant (" + Theme.Money(_commande.Solde) + ").", "Paiement invalide");
            return;
        }

        string libelleType = _type.SelectedItem?.ToString() ?? TypePaiement.Acompte.Libelle();
        TypePaiement type = libelleType == TypePaiement.Direct.Libelle()
            ? TypePaiement.Direct
            : TypePaiement.Acompte;

        try
        {
            _enregistrementEnCours = true;
            Cursor = Cursors.WaitCursor;
            PaiementDto paiement = await AppState.Api.AddPaiementAsync(_commande.Id, new PaiementCreateDto
            {
                Montant = montant,
                Type = type
            });
            CommandeDto? miseAJour = await AppState.Api.GetCommandeAsync(_commande.Id);
            if (miseAJour != null) await AfficherCommandeAsync(miseAJour);
            Theme.Info(
                "Paiement enregistré.\n\nTicket : " + paiement.NumeroTicket +
                "\nMontant : " + Theme.Money(paiement.Montant) +
                "\nType : " + paiement.TypeLibelle +
                "\nDate : " + Theme.DateHeure(paiement.DatePaiement),
                "Paiement confirmé");
        }
        catch (Exception ex)
        {
            Theme.Error(ex, "l'enregistrement du paiement");
        }
        finally
        {
            _enregistrementEnCours = false;
            Cursor = Cursors.Default;
        }
    }
}
