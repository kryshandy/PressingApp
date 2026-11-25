using System.Diagnostics;
using Pressing.Client;

namespace Pressing.Desktop.Services;

/// <summary>Téléchargement et ouverture des reçus PDF.</summary>
public static class ReceiptService
{
    /// <summary>Enregistre le reçu dans le dossier temporaire et retourne son chemin.</summary>
    public static async Task<string> DownloadAsync(PressingApiClient api, int orderId, string ticket)
    {
        byte[] pdf = await api.GetReceiptPdfAsync(orderId);
        if (pdf.Length < 100 || pdf[0] != (byte)'%' || pdf[1] != (byte)'P')
        {
            throw new InvalidOperationException("Le serveur n'a pas renvoyé un reçu PDF valide.");
        }

        string safe = string.Concat(ticket.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_'));
        string path = Path.Combine(Path.GetTempPath(), $"recu-{safe}.pdf");
        await File.WriteAllBytesAsync(path, pdf);
        return path;
    }

    /// <summary>Télécharge puis ouvre le reçu avec le lecteur PDF par défaut.</summary>
    public static async Task<string> OpenAsync(PressingApiClient api, int orderId, string ticket)
    {
        string path = await DownloadAsync(api, orderId, ticket);
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Le reçu a été enregistré ({path}) mais ne peut pas être ouvert : {ex.Message}");
        }

        return path;
    }
}
