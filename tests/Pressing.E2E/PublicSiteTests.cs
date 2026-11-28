using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Pressing.E2E;

/// <summary>Scénarios 1, 2, 3, 6 et 7 : pages publiques, suivi, contact, responsive, désinscription.</summary>
[Collection(SiteCollection.Name)]
public sealed class PublicSiteTests(SiteFixture site)
{
    // Commande de démonstration n°2 : Koffi Yao, « en cours », déposée il y a 4 h (DbSeeder).
    private static string DemoTicket => $"PRESS-{DateTime.Now.AddHours(-4):yyyy}-00002";
    private const string DemoPhone = "+225 05 22 33 44 55";

    [E2EFact]
    public async Task Pages_publiques_repondent_200_avec_leur_titre_et_sans_erreur_JavaScript()
    {
        var pages = new (string Path, string Title, string Heading)[]
        {
            ("/", "Pressing", ""),
            ("/tarifs", "Tarifs — ", "Une grille simple, des prix annoncés d'avance"),
            ("/contact", "Contact — ", "Parlons de vos vêtements"),
            ("/mentions-legales", "Mentions légales — ", "Mentions légales"),
        };

        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();
        var watch = new ConsoleWatch(page);

        foreach (var (path, title, heading) in pages)
        {
            var response = await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.NetworkIdle });
            Assert.Equal(200, response!.Status);
            Assert.Contains(title, await page.TitleAsync());
            var h1 = page.GetByRole(AriaRole.Heading, new() { Level = 1 });
            await Expect(h1.First).ToBeVisibleAsync();
            if (heading.Length > 0) Assert.Equal(heading, (await h1.First.InnerTextAsync()).Trim());
        }

        Assert.Empty(watch.Errors);
    }

    [E2EFact]
    public async Task Suivi_affiche_la_frise_des_etapes_pour_une_commande_de_demonstration()
    {
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();
        var watch = new ConsoleWatch(page);

        await page.GotoAsync("/suivi");
        await page.GetByLabel("Numéro de ticket").FillAsync(DemoTicket);
        await page.GetByLabel("Téléphone").FillAsync(DemoPhone);
        await page.GetByRole(AriaRole.Button, new() { Name = "Rechercher" }).ClickAsync();

        await page.WaitForURLAsync(new Regex(@"/suivi\?"));
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = DemoTicket, Level = 2 })).ToBeVisibleAsync();

        var frise = page.GetByRole(AriaRole.List, new() { Name = "Étapes de la commande" });
        var etapes = frise.GetByRole(AriaRole.Listitem);
        Assert.Equal(4, await etapes.CountAsync());
        Assert.Equal(["Déposé", "En traitement", "Prêt", "Retiré"],
            (await etapes.Locator("strong").AllInnerTextsAsync()).Select(t => t.Trim()).ToArray());
        // « En cours » : la deuxième étape est l'étape courante.
        Assert.Equal("step", await etapes.Nth(1).GetAttributeAsync("aria-current"));

        Assert.Empty(watch.Errors);
    }

    [E2EFact]
    public async Task Suivi_ticket_inconnu_affiche_un_message_clair()
    {
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"/suivi?ticket=PRESS-1999-99999&phone={Uri.EscapeDataString(DemoPhone)}");

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Commande introuvable" })).ToBeVisibleAsync();
        await Expect(page.GetByText("Aucune commande ne correspond à ce ticket et à ce téléphone.")).ToBeVisibleAsync();
    }

    [E2EFact]
    public async Task Suivi_sans_telephone_est_refuse_par_le_formulaire_et_par_le_serveur()
    {
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();

        // 1. Formulaire (JavaScript) : message local, aucune requête envoyée.
        await page.GotoAsync("/suivi");
        await page.GetByLabel("Numéro de ticket").FillAsync(DemoTicket);
        await page.GetByRole(AriaRole.Button, new() { Name = "Rechercher" }).ClickAsync();
        var erreur = page.Locator("#tracking-error");
        await Expect(erreur).ToBeVisibleAsync();
        Assert.Contains("Renseignez à la fois le numéro de ticket et le téléphone", await erreur.InnerTextAsync());
        Assert.DoesNotContain("?ticket", page.Url);

        // 2. Sans JavaScript (requête directe) : même message, rendu par le serveur.
        await page.GotoAsync($"/suivi?ticket={DemoTicket}&phone=");
        Assert.Contains("Renseignez à la fois le numéro de ticket et le téléphone", await page.Locator("#tracking-error").InnerTextAsync());
        await Expect(page.Locator("#tracking-error")).ToBeVisibleAsync();

        // 3. L'API elle-même exige le téléphone, avec l'accentuation exacte.
        using var r = await site.Http.GetAsync($"{E2EEnvironment.ApiUrl}/api/public/tracking/{DemoTicket}");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("Le numéro de téléphone est requis pour consulter une commande.", body!["message"]);
    }

    [E2EFact]
    public async Task Contact_message_valide_affiche_le_succes()
    {
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();
        var watch = new ConsoleWatch(page);

        await page.GotoAsync("/contact");
        await page.GetByLabel("Nom complet").FillAsync("Test Playwright");
        await page.GetByLabel("Adresse e-mail").FillAsync("playwright@example.com");
        await page.GetByLabel("Votre message").FillAsync("Bonjour, ceci est un message de test automatique.");
        await page.GetByRole(AriaRole.Button, new() { Name = "Envoyer le message" }).ClickAsync();

        var succes = page.Locator(".flash-success");
        await Expect(succes).ToBeVisibleAsync();
        Assert.Contains("message", (await succes.InnerTextAsync()).ToLowerInvariant());
        Assert.Empty(watch.Errors);
    }

    [E2EFact]
    public async Task Contact_champ_piege_rempli_simule_le_succes_sans_envoi()
    {
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/contact");
        // Le champ piège est invisible pour une personne : un robot le remplit.
        await page.Locator("#Website").FillAsync("http://spam.example", new() { Force = true });
        // Nom/e-mail/message volontairement vides : si le message partait, l'API le refuserait.
        await page.GetByRole(AriaRole.Button, new() { Name = "Envoyer le message" }).ClickAsync();

        var succes = page.Locator(".flash-success");
        await Expect(succes).ToBeVisibleAsync();
        Assert.Equal("Merci, votre message a bien été envoyé.", (await succes.Locator("p").InnerTextAsync()).Trim());
    }

    [E2EFact]
    public async Task Contact_message_invalide_est_refuse_avec_les_erreurs()
    {
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/contact");
        await page.GetByRole(AriaRole.Button, new() { Name = "Envoyer le message" }).ClickAsync();

        var erreurs = page.Locator(".flash-error");
        await Expect(erreurs).ToBeVisibleAsync();
        Assert.Contains("Merci d'indiquer votre nom.", await erreurs.InnerTextAsync());
        Assert.Empty(await page.Locator(".flash-success").AllAsync());
    }

    [E2EFact]
    public async Task Desabonnement_avec_jeton_invalide_affiche_lien_invalide()
    {
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync("/desabonnement?token=invalide");
        Assert.Equal(200, response!.Status);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Lien invalide" })).ToBeVisibleAsync();
    }

    [E2EFact]
    public async Task Accueil_et_suivi_ne_debordent_pas_horizontalement_en_mobile_et_en_bureau()
    {
        foreach (var (width, height) in new[] { (375, 812), (1280, 800) })
        {
            await using var context = await site.NewContextAsync(width, height);
            var page = await context.NewPageAsync();
            foreach (var path in new[] { "/", "/suivi" })
            {
                await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.NetworkIdle });
                Assert.True(await page.HorizontalOverflowAsync() <= 0,
                    $"Débordement horizontal sur {path} en {width}×{height}.");
                await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 }).First).ToBeVisibleAsync();
            }
        }
    }
}
