using Pressing.Desktop.Forms;

namespace Pressing.Desktop;

static class Program
{
    /// <summary>
    /// Point d'entrée de l'application Pressing Desktop.
    /// </summary>
    [STAThread]
    static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new FrmPrincipal());
    }
}
