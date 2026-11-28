using System.Diagnostics;
using Pressing.Client;

namespace Pressing.Desktop.Services;

/// <summary>Téléchargement et ouverture de la planche d'étiquettes PDF d'une commande.</summary>
public static class LabelFileService
{
    /// <summary>Enregistre la planche dans le dossier temporaire et retourne son chemin.</summary>
    public static async Task<string> DownloadAsync(PressingApiClient api, int orderId, string ticket)
    {
        byte[] pdf = await api.GetLabelsPdfAsync(orderId);
        if (pdf.Length < 100 || pdf[0] != (byte)'%' || pdf[1] != (byte)'P')
        {
            throw new InvalidOperationException("Le serveur n'a pas renvoyé une planche d'étiquettes PDF valide.");
        }

        string safe = string.Concat(ticket.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_'));
        string path = Path.Combine(Path.GetTempPath(), $"etiquettes-{safe}.pdf");
        await File.WriteAllBytesAsync(path, pdf);
        return path;
    }

    /// <summary>Télécharge puis ouvre la planche avec le lecteur PDF par défaut (impression depuis le lecteur).</summary>
    public static async Task<string> OpenAsync(PressingApiClient api, int orderId, string ticket)
    {
        string path = await DownloadAsync(api, orderId, ticket);
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Les étiquettes ont été enregistrées ({path}) mais ne peuvent pas être ouvertes : {ex.Message}");
        }

        return path;
    }
}
