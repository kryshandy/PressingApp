using Pressing.Client;
using Pressing.Core;
using Pressing.Desktop.Forms;
using Pressing.Desktop.Services;
using Pressing.Desktop.Ui;

namespace Pressing.Desktop;

static class Program
{
    public const string DefaultApiUrl = "http://localhost:5180";

    private static bool _fatalShown;

    /// <summary>Point d'entrée : connexion → fenêtre principale ; retour à la connexion si la session expire.</summary>
    [STAThread]
    static int Main(string[] args)
    {
        EnvFile.Load(AppContext.BaseDirectory);
        EnvFile.Load();

        // Contrôle visuel (auto-test) : rendu à 96 dpi pour vérifier la mise en page à 100 % (ou à 150 % avec
        // PRESSING_UI_SCALE=1.5) quel que soit le réglage d'affichage de Windows.
        if (Environment.GetEnvironmentVariable("PRESSING_DPI_UNAWARE") == "1")
        {
            Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
        }

        ApplicationConfiguration.Initialize();
        // Le mode d'exceptions doit être fixé AVANT le mode sombre : SetColorMode crée déjà une fenêtre
        // et SetUnhandledExceptionMode lève alors InvalidOperationException (l'application ne démarrait plus).
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.SetColorMode(SystemColorMode.Dark);
        Application.ThreadException += (_, e) => ShowUnhandled(e.Exception, recoverable: true);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ShowUnhandled(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()), recoverable: false);
        Theme.InitializeDpi();

        string apiUrl = ResolveApiUrl();

        if (args.Any(a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            string? screens = args.SkipWhile(a => !string.Equals(a, "--screens", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
            return SelfTest.Run(apiUrl, screens);
        }

        string? notice = null;
        while (true)
        {
            using PressingApiClient api = new(apiUrl);
            AppSession? session;
            using (FrmLogin login = new(api, apiUrl, notice))
            {
                Application.Run(login);
                session = login.Session;
            }

            notice = null;
            if (session is null)
            {
                return 0; // fenêtre de connexion fermée : on quitte
            }

            using FrmMain main = new(session);
            Application.Run(main);
            switch (main.ExitReason)
            {
                case MainExitReason.SessionExpired:
                    notice = "Votre session a expiré. Veuillez vous reconnecter.";
                    break;
                case MainExitReason.LoggedOut:
                    break;
                default:
                    return 0;
            }
        }
    }

    public static string ResolveApiUrl()
    {
        string? url = Environment.GetEnvironmentVariable("PRESSING_PUBLIC_API_URL");
        return string.IsNullOrWhiteSpace(url) ? DefaultApiUrl : url.Trim().TrimEnd('/');
    }

    private static string LogPath
    {
        get
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PressingEclat");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "desktop.log");
        }
    }

    private static void ShowUnhandled(Exception exception, bool recoverable)
    {
        try
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Journal indisponible : sans importance.
        }

        if (exception is ApiException { IsUnauthorized: true })
        {
            return; // géré par la coque (retour à la connexion)
        }

        if (_fatalShown)
        {
            return;
        }

        try
        {
            if (!recoverable)
            {
                _fatalShown = true;
            }

            string message = exception is ApiException api
                ? api.Message
                : "Une erreur inattendue s'est produite. Si le problème persiste, redémarrez l'application." +
                  Environment.NewLine + Environment.NewLine + exception.Message;
            AppDialogs.Error(null, message, recoverable ? "Erreur" : "Erreur fatale");
        }
        catch
        {
            MessageBox.Show(exception.Message, "Pressing Éclat", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        if (!recoverable)
        {
            Environment.Exit(1);
        }
    }
}
