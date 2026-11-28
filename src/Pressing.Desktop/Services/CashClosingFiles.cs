using System.Diagnostics;
using Pressing.Client;

namespace Pressing.Desktop.Services;

/// <summary>Téléchargement et ouverture du PDF d'une clôture de caisse.</summary>
public static class CashClosingFiles
{
    /// <summary>Enregistre le PDF dans le dossier temporaire et retourne son chemin.</summary>
    public static async Task<string> DownloadAsync(PressingApiClient api, int closingId, DateTime date)
    {
        byte[] pdf = await api.GetCashClosingPdfAsync(closingId);
        if (pdf.Length < 100 || pdf[0] != (byte)'%' || pdf[1] != (byte)'P')
        {
            throw new InvalidOperationException("Le serveur n'a pas renvoyé un PDF valide.");
        }

        string path = Path.Combine(Path.GetTempPath(), $"cloture-caisse-{date:yyyyMMdd}.pdf");
        await File.WriteAllBytesAsync(path, pdf);
        return path;
    }

    /// <summary>Télécharge puis ouvre le PDF avec le lecteur par défaut.</summary>
    public static async Task<string> OpenAsync(PressingApiClient api, int closingId, DateTime date)
    {
        string path = await DownloadAsync(api, closingId, date);
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Le PDF a été enregistré ({path}) mais ne peut pas être ouvert : {ex.Message}");
        }

        return path;
    }
}
