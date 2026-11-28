using Pressing.Core.Dtos;

namespace Pressing.Client;

// Enquête de satisfaction : page publique /avis (jeton du lien) et liste des avis (responsable ou administrateur).
public sealed partial class PressingApiClient
{
    /// <summary>Contenu de la page /avis ; 400 si le lien est invalide ou expiré.</summary>
    public Task<FeedbackFormDto?> GetFeedbackFormAsync(string token, CancellationToken ct = default) =>
        GetAsync<FeedbackFormDto>("api/public/feedback" + Query(("token", token)), ct);

    /// <summary>Enregistre l'avis (note 1 à 5) ; 409 si la commande a déjà reçu une réponse.</summary>
    public async Task<string> SubmitFeedbackAsync(string token, int note, string? comment, CancellationToken ct = default)
    {
        var r = await PostAsync<MessageResponse>("api/public/feedback", new SubmitFeedbackRequest(token, note, comment), ct, anonymous: true);
        return r?.Message ?? string.Empty;
    }

    /// <summary>Avis récents, moyenne et nombre sur les <paramref name="days"/> derniers jours (tout l'historique si null).</summary>
    public Task<FeedbackPageDto?> GetFeedbackAsync(int page = 1, int pageSize = 25, int? days = null, CancellationToken ct = default) =>
        GetAsync<FeedbackPageDto>("api/feedback" + Query(("page", page), ("pageSize", pageSize), ("days", days)), ct);
}
