using System.Diagnostics;
using System.Text;
using Microsoft.Playwright;

namespace Pressing.E2E;

[CollectionDefinition(Name)]
public sealed class SiteCollection : ICollectionFixture<SiteFixture>
{
    public const string Name = "site";
}

/// <summary>
/// Démarre l'API (5321) et le site (5322) en Release avec une base jetable et des données de démonstration,
/// puis un Chromium sans interface. Tout est arrêté (arbre de processus) et supprimé à la fin.
/// Les ports 5180/5181 (instance de l'utilisateur) ne sont jamais utilisés.
/// </summary>
public sealed class SiteFixture : IAsyncLifetime
{
    public const string ClientPassword = "Client@2026!";

    private readonly List<Process> _processes = [];
    private readonly StringBuilder _logs = new();
    private string? _workDir;
    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = null!;
    public HttpClient Http { get; } = new() { Timeout = TimeSpan.FromSeconds(10) };

    public async Task InitializeAsync()
    {
        if (E2EEnvironment.SkipReason is not null) return; // tous les tests sont ignorés
        if (E2EEnvironment.RepoRoot is null) throw new InvalidOperationException("Racine du dépôt introuvable.");

        try
        {
            foreach (var url in new[] { E2EEnvironment.ApiUrl + "/api/health", E2EEnvironment.WebUrl + "/" })
                if (await IsUpAsync(url))
                    throw new InvalidOperationException($"Le port de {url} est déjà utilisé : arrêtez le processus qui y répond.");

            _workDir = Path.Combine(Path.GetTempPath(), "pressing-e2e-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_workDir);

            var env = new Dictionary<string, string>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Production",
                ["DOTNET_ENVIRONMENT"] = "Production",
                ["PRESSING_DB_PATH"] = Path.Combine(_workDir, "pressing-e2e.db"),
                ["PRESSING_BACKUP_PATH"] = Path.Combine(_workDir, "Backups"),
                ["PRESSING_JWT_KEY"] = "e2e-jwt-key-0123456789-abcdefghijklmnopqrstuvwxyz-ABCDEF",
                ["PRESSING_SEED_DEMO"] = "true",
                ["PRESSING_ADMIN_USERNAME"] = "admin",
                ["PRESSING_ADMIN_PASSWORD"] = "Admin@2026!",
                ["EMAIL_PROVIDER"] = "simulated",
                ["EMAIL_BACKGROUND_ENABLED"] = "false",
                ["REMINDERS_ENABLED"] = "false",
                ["BACKUP_SCHEDULE_ENABLED"] = "false",
                ["WHATSAPP_PROVIDER"] = "simulated",
                ["PRESSING_API_URL"] = E2EEnvironment.ApiUrl,
                ["PRESSING_WEB_URL"] = E2EEnvironment.WebUrl,
                ["PRESSING_PUBLIC_API_URL"] = E2EEnvironment.ApiUrl,
            };

            Start("Pressing.Api", E2EEnvironment.ApiUrl, env);
            await WaitAsync(E2EEnvironment.ApiUrl + "/api/health", "API");
            Start("Pressing.Web", E2EEnvironment.WebUrl, env);
            await WaitAsync(E2EEnvironment.WebUrl + "/", "site");

            _playwright = await Playwright.CreateAsync();
            Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        }
        catch (Exception ex)
        {
            var message = ex is PlaywrightException
                ? $"{ex.Message}\n{E2EEnvironment.InstallHelp}"
                : ex.Message;
            string logs;
            lock (_logs) logs = _logs.ToString();
            await DisposeAsync();
            throw new InvalidOperationException($"Démarrage de l'environnement E2E impossible : {message}\n--- Journaux ---\n{logs}", ex);
        }
    }

    public async Task DisposeAsync()
    {
        try { if (Browser is not null) await Browser.CloseAsync(); } catch { /* arrêt au mieux */ }
        _playwright?.Dispose();
        _playwright = null;
        foreach (var p in _processes)
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); p.WaitForExit(5000); } catch { /* déjà arrêté */ }
            p.Dispose();
        }
        _processes.Clear();
        Http.Dispose();
        if (_workDir is not null)
        {
            for (var i = 0; i < 5; i++)
            {
                try { Directory.Delete(_workDir, true); break; }
                catch { await Task.Delay(300); }
            }
            _workDir = null;
        }
    }

    /// <summary>Nouveau contexte de navigation isolé (cookies propres), langue française.</summary>
    public Task<IBrowserContext> NewContextAsync(int width = 1280, int height = 800) =>
        Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = E2EEnvironment.WebUrl,
            Locale = "fr-FR",
            ViewportSize = new ViewportSize { Width = width, Height = height },
            AcceptDownloads = true,
        });

    private void Start(string project, string url, Dictionary<string, string> env)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = E2EEnvironment.ProjectDir(project),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(E2EEnvironment.DllPath(project));
        psi.ArgumentList.Add("--urls");
        psi.ArgumentList.Add(url);
        foreach (var (k, v) in env) psi.Environment[k] = v;
        psi.Environment.Remove("ASPNETCORE_URLS");

        var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (_logs) _logs.AppendLine($"[{project}] {e.Data}"); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (_logs) _logs.AppendLine($"[{project}!] {e.Data}"); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _processes.Add(process);
    }

    private async Task<bool> IsUpAsync(string url)
    {
        try { using var r = await Http.GetAsync(url); return (int)r.StatusCode < 500; }
        catch { return false; }
    }

    private async Task WaitAsync(string url, string name)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            if (_processes.Any(p => p.HasExited)) throw new InvalidOperationException($"Un processus s'est arrêté avant que {name} soit prêt.");
            if (await IsUpAsync(url)) return;
            await Task.Delay(500);
        }
        throw new TimeoutException($"{name} ({url}) n'a pas répondu en 90 s.");
    }
}
