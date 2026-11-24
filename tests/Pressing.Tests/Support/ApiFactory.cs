using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Pressing.Client;

namespace Pressing.Tests.Support;

/// <summary>
/// Hôte de test de l'API : base SQLite temporaire (seed démo activé), clé JWT locale, e-mails simulés.
/// Program.cs lit plusieurs valeurs via <c>Environment.GetEnvironmentVariable</c> avant la construction de l'hôte ;
/// les variables sont donc positionnées dans le constructeur (et restaurées à la libération).
/// Comme les variables d'environnement sont globales au processus, toutes les classes d'intégration
/// appartiennent à la collection <see cref="ApiCollection"/> et s'exécutent donc l'une après l'autre.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private static readonly string[] ManagedVariables =
    [
        "PRESSING_DB_PATH", "PRESSING_BACKUP_PATH", "PRESSING_JWT_KEY", "PRESSING_SEED_DEMO",
        "PRESSING_ADMIN_USERNAME", "PRESSING_ADMIN_PASSWORD",
        "EMAIL_PROVIDER", "EMAIL_BACKGROUND_ENABLED",
        "RESEND_API_KEY", "SMTP_HOST", "SMTP_USER", "SMTP_APP_PASSWORD"
    ];

    private readonly Dictionary<string, string?> _previous = new();
    private readonly Dictionary<string, string> _tokens = new();
    private readonly object _tokenLock = new();

    public ApiFactory()
    {
        Directory = Path.Combine(Path.GetTempPath(), "pressing-api-tests-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
        DbPath = Path.Combine(Directory, "pressing-test.db");

        foreach (string name in ManagedVariables)
        {
            _previous[name] = Environment.GetEnvironmentVariable(name);
        }

        // Positionnées AVANT EnvFile.Load() (qui ne remplace jamais une variable déjà définie) :
        // le fichier .env du dépôt ne peut donc ni rediriger la base ni activer un vrai fournisseur d'e-mails.
        Environment.SetEnvironmentVariable("PRESSING_DB_PATH", DbPath);
        Environment.SetEnvironmentVariable("PRESSING_BACKUP_PATH", Path.Combine(Directory, "Backups"));
        Environment.SetEnvironmentVariable("PRESSING_JWT_KEY", "tests-only-jwt-key-0123456789-abcdefghijklmnopqrstuvwxyz");
        Environment.SetEnvironmentVariable("PRESSING_SEED_DEMO", "true");
        Environment.SetEnvironmentVariable("PRESSING_ADMIN_USERNAME", "admin");
        Environment.SetEnvironmentVariable("PRESSING_ADMIN_PASSWORD", AdminPassword);
        Environment.SetEnvironmentVariable("EMAIL_PROVIDER", "simulated");
        Environment.SetEnvironmentVariable("EMAIL_BACKGROUND_ENABLED", "false");
        Environment.SetEnvironmentVariable("RESEND_API_KEY", null);
        Environment.SetEnvironmentVariable("SMTP_HOST", null);
        Environment.SetEnvironmentVariable("SMTP_USER", null);
        Environment.SetEnvironmentVariable("SMTP_APP_PASSWORD", null);
    }

    public const string AdminPassword = "Admin@2026!";
    public const string ManagerPassword = "Manager@2026!";
    public const string CashierPassword = "Caisse@2026!";
    public const string ClientPassword = "Client@2026!";

    public string Directory { get; }
    public string DbPath { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:Path", DbPath);
        builder.ConfigureLogging(logging => logging.ClearProviders());
    }

    /// <summary>Nouveau client typé (HttpClient en mémoire), sans jeton.</summary>
    public PressingApiClient NewApiClient() => new(CreateClient());

    /// <summary>Client typé authentifié en personnel (jeton mis en cache pour ne pas saturer la limitation de débit).</summary>
    public async Task<PressingApiClient> StaffAsync(string username, string password)
    {
        var api = NewApiClient();
        string? cached;
        lock (_tokenLock) _tokens.TryGetValue("staff:" + username, out cached);
        if (cached is not null)
        {
            api.Token = cached;
            return api;
        }

        var auth = await api.LoginStaffAsync(username, password);
        lock (_tokenLock) _tokens["staff:" + username] = auth!.Token;
        return api;
    }

    public Task<PressingApiClient> CashierAsync() => StaffAsync("caisse", CashierPassword);
    public Task<PressingApiClient> ManagerAsync() => StaffAsync("manager", ManagerPassword);
    public Task<PressingApiClient> AdminAsync() => StaffAsync("admin", AdminPassword);

    /// <summary>Client typé authentifié en tant que client du portail (identifiant = téléphone ou e-mail).</summary>
    public async Task<PressingApiClient> PortalClientAsync(string identifier, string password = ClientPassword)
    {
        var api = NewApiClient();
        string? cached;
        lock (_tokenLock) _tokens.TryGetValue("client:" + identifier, out cached);
        if (cached is not null)
        {
            api.Token = cached;
            return api;
        }

        var auth = await api.LoginClientAsync(identifier, password);
        lock (_tokenLock) _tokens["client:" + identifier] = auth!.Token;
        return api;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;

        SqliteConnection.ClearAllPools();
        try { System.IO.Directory.Delete(Directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        foreach ((string name, string? value) in _previous)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}

[CollectionDefinition("Api")]
public sealed class ApiCollection;
