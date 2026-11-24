using System.ComponentModel;
using Pressing.Shared.Dto;

namespace Pressing.Desktop.Ui;

// ---------- Modèles d'affichage ----------

public sealed class LigneVue
{
    public int Id { get; set; }
    public string Designation { get; set; } = string.Empty;
    public string Categorie { get; set; } = string.Empty;
    public int Quantite { get; set; }
    public int QuantiteRetiree { get; set; }
    public int QuantiteRestante { get; set; }
    public decimal PrixUnitaire { get; set; }
    public string Express { get; set; } = string.Empty;
    public decimal Supplement { get; set; }
    public decimal Montant { get; set; }
    public string Statut { get; set; } = string.Empty;
    public string Depot { get; set; } = string.Empty;
    public string Prevue { get; set; } = string.Empty;
    public string Retiree { get; set; } = "-";

    public static LigneVue From(LigneDto ligne) => new()
    {
        Id = ligne.Id,
        Designation = ligne.Designation,
        Categorie = ligne.CategorieNom,
        Quantite = ligne.Quantite,
        QuantiteRetiree = ligne.QuantiteDejaRetiree,
        QuantiteRestante = ligne.QuantiteRestante,
        PrixUnitaire = ligne.PrixUnitaire,
        Express = ligne.EstExpress ? "EXPRESS" : string.Empty,
        Supplement = ligne.SupplementExpress,
        Montant = ligne.MontantLigne,
        Statut = ligne.StatutLibelle,
        Depot = Theme.Date(ligne.DateDepot),
        Prevue = Theme.Date(ligne.DateRetraitPrevue),
        Retiree = ligne.DateRetraitReelle.HasValue ? Theme.Date(ligne.DateRetraitReelle.Value) : "-"
    };
}

public sealed class CommandeVue
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public string Ticket { get; set; } = string.Empty;
    public string Client { get; set; } = string.Empty;
    public string Depot { get; set; } = string.Empty;
    public string Prevue { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public decimal Paye { get; set; }
    public decimal Solde { get; set; }
    public string Statut { get; set; } = string.Empty;
    public string Express { get; set; } = string.Empty;

    public static CommandeVue From(CommandeDto commande)
    {
        int express = commande.Articles.Count(a => a.EstExpress);
        return new CommandeVue
        {
            Id = commande.Id,
            ClientId = commande.ClientId,
            Ticket = commande.NumeroTicket,
            Client = commande.ClientNom,
            Depot = Theme.Date(commande.DateDepot),
            Prevue = Theme.Date(commande.DateRetraitPrevue),
            Total = commande.Total,
            Paye = commande.Paye,
            Solde = commande.Solde,
            Statut = commande.ResumeStatut,
            Express = express > 0 ? "EXPRESS ×" + express : string.Empty
        };
    }
}

public sealed class PaiementVue
{
    public string Date { get; set; } = string.Empty;
    public string Ticket { get; set; } = string.Empty;
    public string Client { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public decimal Montant { get; set; }

    public static PaiementVue From(PaiementDto paiement) => new()
    {
        Date = Theme.DateHeure(paiement.DatePaiement),
        Ticket = paiement.NumeroTicket,
        Client = paiement.ClientNom,
        Type = paiement.TypeLibelle,
        Montant = paiement.Montant
    };
}

public sealed class RetraitVue
{
    public string Date { get; set; } = string.Empty;
    public string Ticket { get; set; } = string.Empty;
    public string Articles { get; set; } = string.Empty;
    public decimal Montant { get; set; }

    public static RetraitVue From(RetraitDto retrait) => new()
    {
        Date = Theme.DateHeure(retrait.DateRetrait),
        Ticket = retrait.NumeroTicket,
        Articles = string.Join(", ", retrait.Articles.Select(a => $"{a.Designation} ×{a.Quantite}")),
        Montant = retrait.MontantVerse
    };
}

public sealed class ImpayeVue
{
    public int ClientId { get; set; }
    public string Client { get; set; } = string.Empty;
    public string Telephone { get; set; } = string.Empty;
    public decimal TotalCommandes { get; set; }
    public decimal TotalPaye { get; set; }
    public decimal Solde { get; set; }
    public string Tickets { get; set; } = string.Empty;

    public static ImpayeVue From(ImpayeDto impaye) => new()
    {
        ClientId = impaye.ClientId,
        Client = impaye.ClientNom,
        Telephone = impaye.Telephone,
        TotalCommandes = impaye.TotalCommandes,
        TotalPaye = impaye.TotalPaye,
        Solde = impaye.Solde,
        Tickets = string.Join(", ", impaye.Commandes.Select(c => c.NumeroTicket))
    };
}

// ---------- Configuration des DataGridView ----------

public static class Grids
{
    public static DataGridViewTextBoxColumn Col(
        string nom, string titre, int poids = 100, string? format = null,
        bool lectureSeule = true, bool visible = true)
    {
        DataGridViewTextBoxColumn colonne = new()
        {
            Name = nom,
            DataPropertyName = nom,
            HeaderText = titre,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = poids,
            ReadOnly = lectureSeule,
            Visible = visible,
            MinimumWidth = visible ? 70 : 2
        };
        if (format != null)
        {
            colonne.DefaultCellStyle.Format = format;
            colonne.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            colonne.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        }
        return colonne;
    }

    private static void RemplirGrille<T>(DataGridView grid, IEnumerable<T> elements)
    {
        grid.DataSource = new BindingList<T>(elements.ToList());
        if (grid.Rows.Count > 0) grid.FirstDisplayedScrollingRowIndex = 0;
    }

    public static class Articles
    {
        public static void Configure(DataGridView grid)
        {
            Theme.StyleGrid(grid);
            grid.Columns.Clear();
            grid.Columns.Add(Col("Id", "Id", 1, visible: false));
            grid.Columns.Add(Col("Designation", "Article", 175));
            grid.Columns.Add(Col("Categorie", "Catégorie", 100));
            grid.Columns.Add(Col("Quantite", "Qté", 50));
            grid.Columns.Add(Col("QuantiteRetiree", "Retiré", 60));
            grid.Columns.Add(Col("QuantiteRestante", "Reste", 60));
            grid.Columns.Add(Col("PrixUnitaire", "Prix (F)", 90, "N0"));
            grid.Columns.Add(Col("Express", "Option", 85));
            grid.Columns.Add(Col("Supplement", "Suppl. (F)", 90, "N0"));
            grid.Columns.Add(Col("Montant", "Montant (F)", 105, "N0"));
            grid.Columns.Add(Col("Statut", "Statut", 120));
            grid.Columns.Add(Col("Depot", "Dépôt", 100));
            grid.Columns.Add(Col("Prevue", "Prévue", 100));
            grid.Columns.Add(Col("Retiree", "Retrait réel", 100));
            grid.CellFormatting += (s, e) =>
            {
                Theme.StatusFormatting(s, e, "Statut");
                Theme.ExpressFormatting(s, e, "Express");
            };
        }

        public static void Remplir(DataGridView grid, IEnumerable<LigneDto> lignes) =>
            RemplirGrille(grid, lignes.Select(LigneVue.From));
    }

    public static class Commandes
    {
        public static void Configure(DataGridView grid)
        {
            Theme.StyleGrid(grid);
            grid.Columns.Clear();
            grid.Columns.Add(Col("Id", "Id", 1, visible: false));
            grid.Columns.Add(Col("ClientId", "ClientId", 1, visible: false));
            grid.Columns.Add(Col("Ticket", "Ticket", 140));
            grid.Columns.Add(Col("Client", "Client", 150));
            grid.Columns.Add(Col("Depot", "Dépôt", 95));
            grid.Columns.Add(Col("Prevue", "Prévue", 95));
            grid.Columns.Add(Col("Total", "Total (F)", 95, "N0"));
            grid.Columns.Add(Col("Paye", "Payé (F)", 95, "N0"));
            grid.Columns.Add(Col("Solde", "Solde (F)", 95, "N0"));
            grid.Columns.Add(Col("Statut", "Statut", 120));
            grid.Columns.Add(Col("Express", "Option", 95));
            grid.CellFormatting += (s, e) =>
            {
                Theme.StatusFormatting(s, e, "Statut");
                Theme.ExpressFormatting(s, e, "Express");
            };
        }

        public static void Remplir(DataGridView grid, IEnumerable<CommandeDto> commandes) =>
            RemplirGrille(grid, commandes.Select(CommandeVue.From));

        public static CommandeVue? Selection(DataGridView grid) =>
            grid.CurrentRow?.DataBoundItem as CommandeVue;
    }

    public static class Paiements
    {
        public static void Configure(DataGridView grid)
        {
            Theme.StyleGrid(grid);
            grid.Columns.Clear();
            grid.Columns.Add(Col("Date", "Date", 150));
            grid.Columns.Add(Col("Ticket", "Ticket", 140));
            grid.Columns.Add(Col("Client", "Client", 150));
            grid.Columns.Add(Col("Type", "Type", 160));
            grid.Columns.Add(Col("Montant", "Montant (F)", 110, "N0"));
        }

        public static void Remplir(DataGridView grid, IEnumerable<PaiementDto> paiements) =>
            RemplirGrille(grid, paiements.Select(PaiementVue.From));
    }

    public static class Retraits
    {
        public static void Configure(DataGridView grid)
        {
            Theme.StyleGrid(grid);
            grid.Columns.Clear();
            grid.Columns.Add(Col("Date", "Date", 160));
            grid.Columns.Add(Col("Ticket", "Ticket", 140));
            grid.Columns.Add(Col("Articles", "Articles retirés", 300));
            grid.Columns.Add(Col("Montant", "Versé (F)", 120, "N0"));
        }

        public static void Remplir(DataGridView grid, IEnumerable<RetraitDto> retraits) =>
            RemplirGrille(grid, retraits.Select(RetraitVue.From));
    }

    public static class Impayes
    {
        public static void Configure(DataGridView grid)
        {
            Theme.StyleGrid(grid);
            grid.Columns.Clear();
            grid.Columns.Add(Col("ClientId", "Id", 1, visible: false));
            grid.Columns.Add(Col("Client", "Client", 170));
            grid.Columns.Add(Col("Telephone", "Téléphone", 120));
            grid.Columns.Add(Col("TotalCommandes", "Total (F)", 110, "N0"));
            grid.Columns.Add(Col("TotalPaye", "Payé (F)", 110, "N0"));
            grid.Columns.Add(Col("Solde", "Solde (F)", 115, "N0"));
            grid.Columns.Add(Col("Tickets", "Tickets", 260));
        }

        public static void Remplir(DataGridView grid, IEnumerable<ImpayeDto> impayes) =>
            RemplirGrille(grid, impayes.Select(ImpayeVue.From));

        public static ImpayeVue? Selection(DataGridView grid) =>
            grid.CurrentRow?.DataBoundItem as ImpayeVue;
    }

    public static class Tarifs
    {
        public static void Configure(DataGridView grid, bool editable = true)
        {
            Theme.StyleGrid(grid, editable);
            grid.Columns.Clear();
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "CategorieId", DataPropertyName = "CategorieId", Visible = false
            });
            grid.Columns.Add(Col("CategorieNom", "Catégorie", 180, lectureSeule: true));
            grid.Columns.Add(Col("Prix", "Prix (F)", 120, "N0", editable));
            grid.Columns.Add(Col("SupplementExpress", "Suppl. express (F)", 140, "N0", editable));
            grid.Columns.Add(Col("DelaiNormalJours", "Délai normal (j)", 120, "N0", editable));
            grid.Columns.Add(Col("DelaiExpressJours", "Délai express (j)", 125, "N0", editable));
        }

        public static void Remplir(DataGridView grid, IEnumerable<TarifDto> tarifs) =>
            RemplirGrille(grid, tarifs.OrderBy(t => t.Ordre));

        public static TarifDto? Selection(DataGridView grid) =>
            grid.CurrentRow?.DataBoundItem as TarifDto;
    }
}
