namespace Pressing.E2E;

/// <summary>Détection de l'environnement : racine du dépôt, binaires compilés, Chromium installé, mode CI.</summary>
public static class E2EEnvironment
{
    public const int ApiPort = 5321;
    public const int WebPort = 5322;
    public static string ApiUrl => $"http://127.0.0.1:{ApiPort}";
    public static string WebUrl => $"http://127.0.0.1:{WebPort}";

    public const string InstallHelp =
        "Installez Chromium : dotnet build tests/Pressing.E2E -c Release puis " +
        "powershell -File tests\\Pressing.E2E\\bin\\Release\\net10.0\\playwright.ps1 install chromium";

    /// <summary>Vrai en intégration continue (variable CI=true) : l'absence de Chromium fait alors échouer les tests.</summary>
    public static bool IsCi =>
        string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);

    public static string? RepoRoot { get; } = FindRepoRoot();

    /// <summary>Dossier de configuration (Release ou Debug) dans lequel ce test a été compilé.</summary>
    public static string Configuration { get; } =
        new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).Parent?.Name ?? "Release";

    public static string ProjectDir(string project) => Path.Combine(RepoRoot ?? "", "src", project);

    public static string DllPath(string project) =>
        Path.Combine(ProjectDir(project), "bin", Configuration, "net10.0", project + ".dll");

    public static string BrowsersPath
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH");
            if (!string.IsNullOrWhiteSpace(custom) && custom != "0") return custom;
            if (OperatingSystem.IsWindows())
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ms-playwright");
            if (OperatingSystem.IsMacOS())
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Caches", "ms-playwright");
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "ms-playwright");
        }
    }

    public static bool ChromiumInstalled =>
        Directory.Exists(BrowsersPath) &&
        Directory.EnumerateDirectories(BrowsersPath, "chromium*").Any();

    /// <summary>Raison de l'ignorance des tests (null = ils s'exécutent). Jamais ignorés en CI.</summary>
    public static string? SkipReason
    {
        get
        {
            if (IsCi) return null;
            if (RepoRoot is null) return "Tests E2E ignorés : racine du dépôt (PressingApp.sln) introuvable.";
            if (!File.Exists(DllPath("Pressing.Api")) || !File.Exists(DllPath("Pressing.Web")))
                return "Tests E2E ignorés : API et site non compilés (dotnet build PressingApp.sln -c Release).";
            if (!ChromiumInstalled) return "Tests E2E ignorés : Chromium n'est pas installé. " + InstallHelp;
            return null;
        }
    }

    private static string? FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "PressingApp.sln"))) return dir.FullName;
        return null;
    }
}

/// <summary>[Fact] ignoré (avec un message clair) si Chromium ou les binaires manquent, sauf en CI où ils sont exigés.</summary>
public sealed class E2EFactAttribute : FactAttribute
{
    public E2EFactAttribute() => Skip = E2EEnvironment.SkipReason;
}
