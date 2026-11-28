using Pressing.Core.Assistant;

namespace Pressing.Tests.Unit.Assistant;

public class CatalogMatcherTests
{
    private static CatalogHints Catalog() => new(
        new[]
        {
            new CatalogTerm("Chemise", []),
            new CatalogTerm("Manteau", []),
            new CatalogTerm("Rideau", []),
            new CatalogTerm("Costume 2 pièces", ["costard"]),
            new CatalogTerm("Costume 3 pièces", ["costard"]),
            new CatalogTerm("Jupe", []),
            new CatalogTerm("Pantalon", []),
            new CatalogTerm("Cravate", [])
        },
        new[]
        {
            new CatalogTerm("Lavage", []),
            new CatalogTerm("Repassage", ["repassé"]),
            new CatalogTerm("Lavage + repassage", ["repassé"]),
            new CatalogTerm("Nettoyage à sec", [])
        },
        new DateOnly(2026, 11, 27));

    [Fact]
    public void Un_pluriel_irregulier_est_rapproche_du_libelle_exact()
    {
        CatalogHints catalog = Catalog();

        Assert.Equal("Manteau", CatalogMatcher.ResolveCategory(catalog, "Manteau", "Dépôt de deux manteaux.").Value);
        Assert.Equal("Rideau", CatalogMatcher.ResolveCategory(catalog, "Rideau", "Dépôt de trois rideaux à repasser.").Value);
    }

    [Fact]
    public void Un_nombre_en_toutes_lettres_est_equivalent_au_chiffre_du_libelle()
    {
        Assert.Equal(
            "Costume 2 pièces",
            CatalogMatcher.ResolveCategory(Catalog(), "Costume deux pièces", "Le costume deux pièces est prêt.").Value);
    }

    [Fact]
    public void Un_alias_ambigu_est_desambiguiE_par_la_transcription()
    {
        CatalogHints catalog = Catalog();

        Assert.Equal(
            "Costume 2 pièces",
            CatalogMatcher.ResolveCategory(catalog, "costard", "Un costume deux pièces à laver.").Value);
        Assert.Null(CatalogMatcher.ResolveCategory(catalog, "costard", "Un costard à laver.").Value);
        Assert.Equal(
            "Repassage",
            CatalogMatcher.ResolveService(catalog, "repassé", "Dépôt d'une chemise à repasser.").Value);
        Assert.Equal(
            "Lavage + repassage",
            CatalogMatcher.ResolveService(catalog, "repassé", "Dépôt d'une chemise lavage et repassage.").Value);
    }

    [Fact]
    public void Une_categorie_proposee_par_le_modele_n_est_pas_prouvee_par_un_alias_ambigu()
    {
        CatalogHints catalog = Catalog();

        Assert.Null(CatalogMatcher.ResolveCategory(catalog, "Costume 2 pièces", "Dépôt d'un costard à laver.").Value);
        Assert.Null(CatalogMatcher.ResolveCategory(catalog, null, "Dépôt d'un costard à laver.").Value);
        Assert.Equal(
            "Costume 2 pièces",
            CatalogMatcher.ResolveCategory(catalog, "Costume 2 pièces", "Dépôt d'un costume deux pièces à laver.").Value);
    }

    [Fact]
    public void Une_valeur_hors_catalogue_est_corrigee_par_le_seul_libelle_prononce()
    {
        CatalogMatch result = CatalogMatcher.ResolveCategory(
            Catalog(), "Drapeau 2 places", "Dépôt de trois rideaux à repasser.");

        Assert.Equal("Rideau", result.Value);
        Assert.Contains("transcription", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Une_prestation_avec_ponctuation_est_reconnue()
    {
        Assert.Equal("Lavage", CatalogMatcher.ExplicitService("Dépôt de chemises à laver."));
        Assert.Equal("Lavage + repassage", CatalogMatcher.ExplicitService("Dépôt de chemises lavage repassage."));
    }

    [Fact]
    public void Chaque_article_garde_sa_prestation_quand_plusieurs_sont_prononcees()
    {
        CatalogHints catalog = Catalog();
        string multi = "Bonjour, je depose deux chemises a laver, trois pantalons a repasser et une cravate en nettoyage a sec.";

        Assert.Equal("Lavage", CatalogMatcher.ResolveServiceForItem(catalog, "Lavage", multi, "Chemise").Value);
        Assert.Equal("Repassage", CatalogMatcher.ResolveServiceForItem(catalog, "Repassage", multi, "Pantalon").Value);
        Assert.Equal("Nettoyage à sec", CatalogMatcher.ResolveServiceForItem(catalog, "Nettoyage à sec", multi, "Cravate").Value);
        Assert.Equal(
            "Lavage",
            CatalogMatcher.ResolveServiceForItem(catalog, null, "Bonjour, je voudrais faire laver trois chemises et un pantalon.", "Pantalon").Value);
        Assert.Equal(
            "Lavage",
            CatalogMatcher.ResolveServiceForItem(catalog, "Lavage + repassage", "Je voudrais faire laver une chemise en express.", "Chemise").Value);
    }
}
