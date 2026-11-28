using Microsoft.Playwright;

namespace Pressing.E2E;

/// <summary>Collecte les erreurs JavaScript et les erreurs de console du site (les ressources tierces comme Google Fonts sont ignorées).</summary>
public sealed class ConsoleWatch
{
    private readonly List<string> _errors = [];
    public IReadOnlyList<string> Errors { get { lock (_errors) return [.. _errors]; } }

    public ConsoleWatch(IPage page)
    {
        page.PageError += (_, message) => Add("Exception JS : " + message);
        page.Console += (_, m) =>
        {
            if (m.Type != "error") return;
            // Les erreurs réseau d'une ressource tierce (polices Google hors connexion, par exemple) ne sont pas celles du site.
            if (m.Location is not null && m.Location.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                && !m.Location.StartsWith(E2EEnvironment.WebUrl, StringComparison.OrdinalIgnoreCase)) return;
            Add($"Console : {m.Text} ({m.Location})");
        };
    }

    private void Add(string text) { lock (_errors) _errors.Add(text); }
}

public static class PageExtensions
{
    /// <summary>Largeur de contenu défilable moins largeur visible : doit rester nulle (aucun débordement horizontal).</summary>
    public static Task<int> HorizontalOverflowAsync(this IPage page) =>
        page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");

    public static async Task LoginAsync(this IPage page, string email)
    {
        await page.GotoAsync("/compte/connexion");
        await page.GetByLabel("Téléphone ou e-mail").FillAsync(email);
        await page.Locator("#Password").FillAsync(SiteFixture.ClientPassword);
        await page.GetByRole(AriaRole.Button, new() { Name = "Se connecter" }).ClickAsync();
        await page.WaitForURLAsync("**/compte");
    }
}
