using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Pressing.E2E;

/// <summary>Scénarios 4 et 5 : espace client (connexion, commandes, reçu PDF, profil, déconnexion) et sécurité.</summary>
[Collection(SiteCollection.Name)]
public sealed class ClientAreaTests(SiteFixture site)
{
    // Clients de démonstration avec accès web (DbSeeder) : mot de passe Client@2026!.
    private const string Awa = "awa.kone@example.com";       // commande n°1 (prête)
    private const string Fatou = "fatou.diabate@example.com"; // commande n°5 (déposée)
    private const string Koffi = "koffi.yao@example.com";     // commande n°2 (en cours)

    private static string TicketAwa => $"PRESS-{DateTime.Now.AddDays(-1):yyyy}-00001";
    private static string TicketKoffi => $"PRESS-{DateTime.Now.AddHours(-4):yyyy}-00002";

    [E2EFact]
    public async Task Connexion_tableau_de_bord_commandes_et_recu_pdf()
    {
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();
        var watch = new ConsoleWatch(page);

        await page.LoginAsync(Awa);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("Awa, ravi de vous revoir");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Commandes en cours" })).ToBeVisibleAsync();
        await Expect(page.GetByText("Points fidélité")).ToBeVisibleAsync();

        // Liste des commandes : celle de la cliente, et seulement la sienne.
        await page.GetByRole(AriaRole.Link, new() { Name = "Mes commandes" }).First.ClickAsync();
        await page.WaitForURLAsync("**/compte/commandes");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Mes commandes", Level = 1 })).ToBeVisibleAsync();
        var table = page.GetByRole(AriaRole.Table);
        await Expect(table.GetByRole(AriaRole.Link, new() { Name = TicketAwa, Exact = true })).ToBeVisibleAsync();
        await Expect(table.GetByText(TicketKoffi)).ToHaveCountAsync(0);

        // Détail de la commande.
        await table.GetByRole(AriaRole.Link, new() { Name = TicketAwa, Exact = true }).ClickAsync();
        await page.WaitForURLAsync(new Regex(@"/compte/commandes/\d+$"));
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = TicketAwa, Level = 1 })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.List, new() { Name = "Étapes de la commande" })).ToBeVisibleAsync();

        // Reçu PDF : en-tête %PDF.
        var download = await page.RunAndWaitForDownloadAsync(async () =>
            await page.GetByRole(AriaRole.Link, new() { Name = "Télécharger le reçu", Exact = true }).ClickAsync());
        Assert.Equal($"recu-{TicketAwa}.pdf", download.SuggestedFilename);
        var path = await download.PathAsync();
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.True(bytes.Length > 100, "Le reçu PDF est vide.");
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));

        Assert.Empty(watch.Errors);
    }

    [E2EFact]
    public async Task Profil_changer_le_canal_de_rappel_exige_la_case_de_consentement()
    {
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.LoginAsync(Fatou);

        await page.GetByRole(AriaRole.Link, new() { Name = "Mon profil" }).First.ClickAsync();
        await page.WaitForURLAsync("**/compte/profil");
        var canal = page.GetByLabel("Rappels de retrait et de solde");
        var consentement = page.GetByLabel("J'accepte de recevoir des rappels");
        var enregistrer = page.GetByRole(AriaRole.Button, new() { Name = "Enregistrer les modifications" });

        // Sans la case : refus avec message, rien n'est enregistré.
        await canal.SelectOptionAsync(new SelectOptionValue { Label = "E-mail" });
        await consentement.UncheckAsync();
        await enregistrer.ClickAsync();
        await Expect(page.GetByText("Cochez la case de consentement pour recevoir des rappels.")).ToBeVisibleAsync();
        await Expect(page.Locator(".flash-success")).ToHaveCountAsync(0);

        // Avec la case : enregistré, et le canal est bien conservé au rechargement.
        await canal.SelectOptionAsync(new SelectOptionValue { Label = "E-mail" });
        await consentement.CheckAsync();
        await enregistrer.ClickAsync();
        await Expect(page.Locator(".flash-success")).ToContainTextAsync("Vos informations ont bien été enregistrées.");
        await page.ReloadAsync();
        await Expect(page.GetByLabel("Rappels de retrait et de solde").Locator("option:checked")).ToHaveTextAsync("E-mail");
        await Expect(page.GetByLabel("J'accepte de recevoir des rappels")).ToBeCheckedAsync();
    }

    [E2EFact]
    public async Task Deconnexion_ferme_la_session()
    {
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.LoginAsync(Awa);

        await page.GetByRole(AriaRole.Button, new() { Name = "Se déconnecter" }).ClickAsync();
        await page.WaitForURLAsync(new Regex(@"/$"));
        await Expect(page.Locator(".flash-success")).ToContainTextAsync("Vous êtes déconnecté.");

        await page.GotoAsync("/compte");
        await Expect(page).ToHaveURLAsync(new Regex(@"/compte/connexion"));
    }

    [E2EFact]
    public async Task Identifiants_incorrects_sont_refuses_avec_un_message()
    {
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/compte/connexion");
        await page.GetByLabel("Téléphone ou e-mail").FillAsync(Awa);
        await page.Locator("#Password").FillAsync("mauvais-mot-de-passe");
        await page.GetByRole(AriaRole.Button, new() { Name = "Se connecter" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Alert).Filter(new() { HasTextRegex = new Regex("incorrect", RegexOptions.IgnoreCase) }))
            .ToBeVisibleAsync();
        await Expect(page).ToHaveURLAsync(new Regex(@"/compte/connexion"));
    }

    [E2EFact]
    public async Task Compte_sans_connexion_redirige_vers_la_connexion()
    {
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("/compte");
        await Expect(page).ToHaveURLAsync(new Regex(@"/compte/connexion\?returnUrl="));
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Connexion", Level = 1 })).ToBeVisibleAsync();

        await page.GotoAsync("/compte/commandes/1");
        await Expect(page).ToHaveURLAsync(new Regex(@"/compte/connexion"));
    }

    [E2EFact]
    public async Task Un_client_ne_voit_pas_la_commande_d_un_autre()
    {
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.LoginAsync(Awa);

        // La commande n°2 appartient à Koffi Yao.
        var detail = await page.GotoAsync("/compte/commandes/2");
        Assert.Equal(404, detail!.Status);
        Assert.DoesNotContain(TicketKoffi, await page.ContentAsync());

        var recu = await context.APIRequest.GetAsync("/compte/commandes/2/recu");
        Assert.Equal(404, recu.Status);

        // Sa propre commande reste accessible.
        var propre = await page.GotoAsync("/compte/commandes/1");
        Assert.Equal(200, propre!.Status);
    }

    [E2EFact]
    public async Task Formulaires_POST_sans_jeton_anti_falsification_sont_refuses()
    {
        await using var context = await site.NewContextAsync();
        var page = await context.NewPageAsync();

        // Public : contact.
        var contact = await context.APIRequest.PostAsync("/contact", new()
        {
            Form = context.APIRequest.CreateFormData()
                .Set("Name", "Robot").Set("Email", "robot@example.com").Set("Message", "Message sans jeton anti-falsification."),
            FailOnStatusCode = false,
        });
        Assert.Equal(400, contact.Status);

        // Connecté : profil et déconnexion refusés sans jeton, la session reste ouverte.
        await page.LoginAsync(Koffi);
        var profil = await context.APIRequest.PostAsync("/compte/profil", new()
        {
            Form = context.APIRequest.CreateFormData().Set("FirstName", "Pirate").Set("LastName", "X"),
            FailOnStatusCode = false,
        });
        Assert.Equal(400, profil.Status);
        var sortie = await context.APIRequest.PostAsync("/compte/deconnexion", new() { FailOnStatusCode = false });
        Assert.Equal(400, sortie.Status);

        await page.GotoAsync("/compte/profil");
        await Expect(page.GetByLabel("Prénom")).ToHaveValueAsync("Koffi");
    }
}
