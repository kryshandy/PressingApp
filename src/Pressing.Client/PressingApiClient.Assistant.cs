using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Pressing.Core.Dtos;

namespace Pressing.Client;

// Assistant vocal (lot M2) : une méthode par route de docs/API.md, section « Assistant vocal ».
public sealed partial class PressingApiClient
{
    /// <summary>État de l'assistant : activé ? moteurs prêts ? message à afficher (ex. « n'est pas démarré »).</summary>
    public Task<AssistantStatusDto?> GetAssistantStatusAsync(CancellationToken ct = default) =>
        GetAsync<AssistantStatusDto>("api/assistant/status", ct);

    /// <summary>Envoie un enregistrement WAV (2 Mo maximum) et reçoit le texte reconnu. L'audio n'est jamais stocké.</summary>
    public async Task<AssistantTranscriptionDto?> TranscribeAssistantAsync(byte[] wav, CancellationToken ct = default)
    {
        byte[] bytes = await SendWavAsync("api/assistant/transcribe", wav, ct).ConfigureAwait(false);
        return Deserialize<AssistantTranscriptionDto>(bytes);
    }

    /// <summary>Transforme un texte en brouillon validé (jamais enregistré) : le caissier vérifie puis valide.</summary>
    public Task<OrderDraftDto?> InterpretAssistantAsync(string text, CancellationToken ct = default) =>
        PostAsync<OrderDraftDto>("api/assistant/interpret", new AssistantInterpretRequest(text), ct);

    /// <summary>POST d'un corps brut <c>audio/wav</c> (SendAsync ne sait envoyer que du JSON).</summary>
    private async Task<byte[]> SendWavAsync(string path, byte[] wav, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        string? token = Token;
        bool authenticated = !string.IsNullOrEmpty(token);
        if (authenticated) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        string? forwardedFor = ForwardedFor;
        if (!string.IsNullOrWhiteSpace(forwardedFor)) request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor.Trim());
        request.Content = new ByteArrayContent(wav);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            byte[] bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return bytes;

            ApiException error = ApiException.FromResponse(
                response.StatusCode, bytes.Length == 0 ? null : Encoding.UTF8.GetString(bytes));
            if (authenticated && error.IsUnauthorized) Unauthorized?.Invoke(this, EventArgs.Empty);
            throw error;
        }
        catch (HttpRequestException ex)
        {
            throw ApiException.Unreachable(BaseUrl, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw ApiException.Unreachable(BaseUrl, ex);
        }
    }
}
