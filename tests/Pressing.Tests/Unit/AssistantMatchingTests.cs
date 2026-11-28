using Pressing.Core;
using Pressing.Core.Assistant;

namespace Pressing.Tests.Unit;

/// <summary>Rapprochement du texte entendu avec le catalogue, les services, les clients et les tickets (sans base).</summary>
public class AssistantMatchingTests
{
    // Extrait du catalogue seed (DbSeeder).
    private static readonly CatalogEntry[] Catalog =
    [
        new(1, "Chemise"), new(2, "Pantalon"), new(3, "Costume 2 pièces"), new(4, "Costume 3 pièces"),
        new(5, "Robe de soirée"), new(6, "Chemisier"), new(7, "Jean"), new(8, "Pull"), new(9, "Cravate"),
        new(10, "Manteau"), new(11, "Drap 2 places")
    ];

    private static readonly ClientEntry[] Clients =
    [
        new(1, "Marie Ngo", "699112233"), new(2, "Paul Ngo", "677000111"),
        new(3, "Awa Koné", "0700112233"), new(4, "Noël Dupont", null)
    ];

    // ---------- Normalisation ----------

    [Fact]
    public void Normalize_retire_accents_casse_et_ponctuation()
    {
        Assert.Equal("lavage repassage", AssistantMatching.Normalize("  Lavage + REPASSAGE "));
        Assert.Equal("nettoyage a sec", AssistantMatching.Normalize("Nettoyage à sec"));
        Assert.Equal("robe de soiree", AssistantMatching.Normalize("Robe de soirée"));
        Assert.Equal(string.Empty, AssistantMatching.Normalize(null));
        Assert.Equal(string.Empty, AssistantMatching.Normalize("  "));
    }

    [Fact]
    public void Distance_compte_les_fautes_de_frappe()
    {
        Assert.Equal(0, AssistantMatching.Distance("chemise", "chemise"));
        Assert.Equal(1, AssistantMatching.Distance("chemize", "chemise"));
        Assert.Equal(1, AssistantMatching.Distance("repasage", "repassage"));
        Assert.True(AssistantMatching.Distance("pantalon", "chemise", 2) > 2);
    }

    // ---------- Catégories ----------

    [Fact]
    public void Categorie_exacte_est_trouvee()
    {
        var result = AssistantMatching.MatchCategory("Chemise", Catalog);

        Assert.Equal(MatchKind.Single, result.Kind);
        Assert.Equal(1, result.Match!.Id);
        Assert.False(result.Approximate);
    }

    [Fact]
    public void Categorie_au_pluriel_est_trouvee()
    {
        Assert.Equal(1, AssistantMatching.MatchCategory("chemises", Catalog).Match!.Id);
        Assert.Equal(2, AssistantMatching.MatchCategory("pantalons", Catalog).Match!.Id);
        Assert.Equal(10, AssistantMatching.MatchCategory("manteaux", Catalog).Match!.Id);
        Assert.Equal(7, AssistantMatching.MatchCategory("jeans", Catalog).Match!.Id);
    }

    [Fact]
    public void Categorie_par_synonyme_du_comptoir()
    {
        var result = AssistantMatching.MatchCategory("costard 3 pièces", Catalog);

        Assert.Equal(MatchKind.Single, result.Kind);
        Assert.Equal(4, result.Match!.Id);
    }

    [Fact]
    public void Categorie_avec_chiffre_dit_en_lettres()
    {
        Assert.Equal(3, AssistantMatching.MatchCategory("costume deux pièces", Catalog).Match!.Id);
        Assert.Equal(4, AssistantMatching.MatchCategory("costume trois pieces", Catalog).Match!.Id);
    }

    [Fact]
    public void Categorie_sans_accent_ni_mot_vide()
    {
        Assert.Equal(5, AssistantMatching.MatchCategory("robe soiree", Catalog).Match!.Id);
        Assert.Equal(5, AssistantMatching.MatchCategory("Robe de soirée", Catalog).Match!.Id);
    }

    [Fact]
    public void Categorie_avec_faute_est_trouvee_et_marquee_approximative()
    {
        var result = AssistantMatching.MatchCategory("chemize", Catalog);

        Assert.Equal(MatchKind.Single, result.Kind);
        Assert.Equal(1, result.Match!.Id);
        Assert.True(result.Approximate);
        Assert.Equal(9, AssistantMatching.MatchCategory("cravatte", Catalog).Match!.Id);
    }

    [Fact]
    public void Categorie_ambigue_ne_devine_pas()
    {
        // « costume » seul correspond aux deux costumes : le backend ne choisit pas.
        var result = AssistantMatching.MatchCategory("costume", Catalog);

        Assert.Equal(MatchKind.Ambiguous, result.Kind);
        Assert.Null(result.Match);
        Assert.Equal(2, result.Candidates.Count);
    }

    [Fact]
    public void Categorie_chemise_ne_se_confond_pas_avec_chemisier()
    {
        Assert.Equal(1, AssistantMatching.MatchCategory("chemise", Catalog).Match!.Id);
        Assert.Equal(6, AssistantMatching.MatchCategory("chemisier", Catalog).Match!.Id);
    }

    [Fact]
    public void Categorie_absente_du_catalogue_est_refusee()
    {
        Assert.Equal(MatchKind.None, AssistantMatching.MatchCategory("hélicoptère", Catalog).Kind);
        Assert.Equal(MatchKind.None, AssistantMatching.MatchCategory("", Catalog).Kind);
        Assert.Equal(MatchKind.None, AssistantMatching.MatchCategory(null, Catalog).Kind);
        // Un mot court n'est jamais « corrigé » vers un autre (aucune tolérance jusqu'à 4 lettres).
        Assert.Equal(MatchKind.None, AssistantMatching.MatchCategory("pul", Catalog).Kind);
        Assert.Equal(MatchKind.None, AssistantMatching.MatchCategory("jea", Catalog).Kind);
    }

    // ---------- Services ----------

    [Fact]
    public void Service_lavage_repassage_sous_toutes_ses_ecritures()
    {
        Assert.Equal(ServiceType.WashAndIron, AssistantMatching.MatchService("Lavage + repassage"));
        Assert.Equal(ServiceType.WashAndIron, AssistantMatching.MatchService("lavage repassage"));
        Assert.Equal(ServiceType.WashAndIron, AssistantMatching.MatchService("lavage et repassage"));
        Assert.Equal(ServiceType.WashAndIron, AssistantMatching.MatchService("laver et repasser"));
    }

    [Fact]
    public void Service_repasse_et_repassage_donnent_repassage()
    {
        Assert.Equal(ServiceType.Ironing, AssistantMatching.MatchService("repassage"));
        Assert.Equal(ServiceType.Ironing, AssistantMatching.MatchService("repassé"));
        Assert.Equal(ServiceType.Ironing, AssistantMatching.MatchService("repasage")); // faute
    }

    [Fact]
    public void Service_lavage_nettoyage_a_sec_et_detachage()
    {
        Assert.Equal(ServiceType.Washing, AssistantMatching.MatchService("lavage"));
        Assert.Equal(ServiceType.DryCleaning, AssistantMatching.MatchService("nettoyage à sec"));
        Assert.Equal(ServiceType.DryCleaning, AssistantMatching.MatchService("nettoyage a sec"));
        Assert.Equal(ServiceType.DryCleaning, AssistantMatching.MatchService("à sec"));
        Assert.Equal(ServiceType.StainRemoval, AssistantMatching.MatchService("détachage"));
    }

    [Fact]
    public void Service_inconnu_ou_vide_donne_null()
    {
        Assert.Null(AssistantMatching.MatchService("teinture"));
        Assert.Null(AssistantMatching.MatchService(""));
        Assert.Null(AssistantMatching.MatchService(null));
    }

    // ---------- Clients ----------

    [Fact]
    public void Client_par_nom_complet_sans_civilite()
    {
        var result = AssistantMatching.MatchClient("Mme Ngo Marie", Clients);

        Assert.Equal(MatchKind.Single, result.Kind);
        Assert.Equal(1, result.Match!.Id);
        Assert.False(result.Approximate);
    }

    [Fact]
    public void Client_nom_et_prenom_dans_l_autre_ordre()
    {
        Assert.Equal(1, AssistantMatching.MatchClient("Marie Ngo", Clients).Match!.Id);
        Assert.Equal(1, AssistantMatching.MatchClient("Ngo Marie", Clients).Match!.Id);
    }

    [Fact]
    public void Client_homonymes_sont_signales_sans_choisir()
    {
        // « Ngo » seul : Marie Ngo et Paul Ngo.
        var result = AssistantMatching.MatchClient("Mme Ngo", Clients);

        Assert.Equal(MatchKind.Ambiguous, result.Kind);
        Assert.Null(result.Match);
        Assert.Equal(2, result.Candidates.Count);
        Assert.Contains(result.Candidates, c => c.Id == 1);
        Assert.Contains(result.Candidates, c => c.Id == 2);
    }

    [Fact]
    public void Client_insensible_aux_accents_et_a_la_casse()
    {
        Assert.Equal(3, AssistantMatching.MatchClient("awa kone", Clients).Match!.Id);
        Assert.Equal(4, AssistantMatching.MatchClient("NOEL", Clients).Match!.Id);
    }

    [Fact]
    public void Client_avec_faute_de_reconnaissance_est_approximatif()
    {
        var result = AssistantMatching.MatchClient("Dupond", Clients);

        Assert.Equal(MatchKind.Single, result.Kind);
        Assert.Equal(4, result.Match!.Id);
        Assert.True(result.Approximate);
    }

    [Fact]
    public void Client_inconnu_ou_vide_n_est_pas_trouve()
    {
        Assert.Equal(MatchKind.None, AssistantMatching.MatchClient("Mme Zorro", Clients).Kind);
        Assert.Equal(MatchKind.None, AssistantMatching.MatchClient("Mme", Clients).Kind);
        Assert.Equal(MatchKind.None, AssistantMatching.MatchClient(null, Clients).Kind);
    }

    // ---------- Tickets ----------

    [Fact]
    public void Ticket_complet_est_reconnu_sous_plusieurs_ecritures()
    {
        Assert.Equal("PRESS-2026-00142", AssistantMatching.ParseTicket("PRESS-2026-00142")!.FullTicket);
        Assert.Equal("PRESS-2026-00142", AssistantMatching.ParseTicket("press 2026 142")!.FullTicket);
        Assert.Equal("PRESS-2026-00007", AssistantMatching.ParseTicket(" Press-2026-7 ")!.FullTicket);
    }

    [Fact]
    public void Ticket_court_donne_un_numero()
    {
        Assert.Equal(142, AssistantMatching.ParseTicket("142")!.Number);
        Assert.Equal(142, AssistantMatching.ParseTicket("ticket 142")!.Number);
        Assert.Equal(142, AssistantMatching.ParseTicket("00142")!.Number);
        Assert.Null(AssistantMatching.ParseTicket("142")!.FullTicket);
    }

    [Fact]
    public void Ticket_illisible_ou_vide_donne_null()
    {
        Assert.Null(AssistantMatching.ParseTicket(null));
        Assert.Null(AssistantMatching.ParseTicket(""));
        Assert.Null(AssistantMatching.ParseTicket("le ticket de Marie"));
        Assert.Null(AssistantMatching.ParseTicket("0"));
        Assert.Null(AssistantMatching.ParseTicket("123456789"));
    }
}
