using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Pressing.Desktop.Services;

/// <summary>
/// Envoi d'octets bruts (type RAW) à une imprimante Windows nommée, sans pilote de mise en page : c'est ce qu'attend
/// une imprimante de tickets ESC/POS. Isolé ici (P/Invoke <c>winspool.drv</c>) pour que le reste de l'application
/// ne dépende pas de l'API Win32.
/// </summary>
public static class RawPrinter
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DocInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string DocName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string DataType;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool OpenPrinter(string printerName, out IntPtr printer, IntPtr defaults);

    [DllImport("winspool.drv", SetLastError = true, ExactSpelling = true)]
    private static extern bool ClosePrinter(IntPtr printer);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StartDocPrinter(IntPtr printer, int level, ref DocInfo docInfo);

    [DllImport("winspool.drv", SetLastError = true, ExactSpelling = true)]
    private static extern bool EndDocPrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true, ExactSpelling = true)]
    private static extern bool StartPagePrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true, ExactSpelling = true)]
    private static extern bool EndPagePrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true, ExactSpelling = true)]
    private static extern bool WritePrinter(IntPtr printer, IntPtr bytes, int count, out int written);

    /// <summary>Envoie <paramref name="data"/> tel quel à l'imprimante ; lève une <see cref="InvalidOperationException"/> lisible en cas d'échec.</summary>
    public static Task SendAsync(string printerName, byte[] data, string documentName) =>
        Task.Run(() => Send(printerName, data, documentName));

    private static void Send(string printerName, byte[] data, string documentName)
    {
        if (string.IsNullOrWhiteSpace(printerName))
        {
            throw new InvalidOperationException("Aucune imprimante de tickets n'est configurée.");
        }

        if (!OpenPrinter(printerName.Trim(), out IntPtr printer, IntPtr.Zero))
        {
            throw Failure($"Imprimante « {printerName.Trim()} » introuvable ou inaccessible. Vérifiez son nom exact (Windows › Imprimantes et scanners).");
        }

        try
        {
            DocInfo doc = new() { DocName = documentName, OutputFile = null, DataType = "RAW" };
            if (StartDocPrinter(printer, 1, ref doc) == 0)
            {
                throw Failure("Le spouleur d'impression a refusé le document.");
            }

            try
            {
                if (!StartPagePrinter(printer))
                {
                    throw Failure("Le spouleur d'impression a refusé la page.");
                }

                IntPtr buffer = Marshal.AllocCoTaskMem(data.Length);
                try
                {
                    Marshal.Copy(data, 0, buffer, data.Length);
                    if (!WritePrinter(printer, buffer, data.Length, out int written) || written != data.Length)
                    {
                        throw Failure("L'envoi des données à l'imprimante a échoué.");
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(buffer);
                    EndPagePrinter(printer);
                }
            }
            finally
            {
                EndDocPrinter(printer);
            }
        }
        finally
        {
            ClosePrinter(printer);
        }
    }

    private static InvalidOperationException Failure(string message) =>
        new($"{message} (détail Windows : {new Win32Exception(Marshal.GetLastWin32Error()).Message})");
}
