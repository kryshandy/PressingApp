using System.Net.Http.Headers;
using System.Text.Json;

namespace Pressing.Core.Assistant;

/// <summary>Client HTTP du whisper.cpp server (/inference). Aucun audio n'est écrit sur le disque.</summary>
public sealed class WhisperSpeechToText : HttpAssistantClientBase, ISpeechToText, IM2SpeechToText
{
    private readonly AssistantOptions _options;
    private readonly string _endpoint;

    public WhisperSpeechToText(HttpClient httpClient, AssistantOptions? options = null)
        : base(httpClient)
    {
        _options = options ?? AssistantOptions.FromEnvironment();
        _options.EnsureLoopbackUrls();
        _endpoint = _options.SttUrl.TrimEnd('/') + "/inference";
    }

    public async Task<SpeechTranscription> TranscribeAsync(Stream wav, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wav);
        byte[] bytes = await ReadBoundedAsync(wav, _options.MaxAudioBytes, cancellationToken);

        AudioInspection audio = WavAudioInspector.Inspect(
            bytes, _options.MaxAudioSeconds, _options.MaxAudioBytes, _options.SilenceRmsThreshold);

        if (audio.SilenceDetected)
        {
            return new SpeechTranscription(string.Empty, audio.DurationSeconds, audio.Rms, true);
        }

        MultipartFormDataContent BuildForm()
        {
            var form = new MultipartFormDataContent("pressing-stt");
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            form.Add(file, "file", "audio.wav");
            form.Add(new StringContent("fr"), "language");
            form.Add(new StringContent("json"), "response_format");
            form.Add(new StringContent("0.0"), "temperature");
            if (!string.IsNullOrWhiteSpace(_options.SttPrompt))
            {
                form.Add(new StringContent(_options.SttPrompt), "prompt");
            }
            return form;
        }

        using HttpResponseMessage response = await SendWithOneRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Post, _endpoint) { Content = BuildForm() },
            _options.SttTimeoutSeconds,
            cancellationToken);

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
        {
            throw new AssistantAudioException("Le moteur STT a refusé l'audio transmis.");
        }
        if (!response.IsSuccessStatusCode)
        {
            throw new AssistantEngineUnavailableException(
                $"Le moteur STT a répondu {(int)response.StatusCode}. {body}");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("text", out JsonElement text) ||
                text.ValueKind != JsonValueKind.String)
            {
                throw new AssistantContractException("Réponse STT sans champ text.");
            }
            return new SpeechTranscription(
                text.GetString() ?? "", audio.DurationSeconds, audio.Rms, false);
        }
        catch (JsonException ex)
        {
            throw new AssistantContractException("Réponse STT illisible.", ex);
        }
    }

    /// <summary>Contrat M2 : seule la transcription textualisée est exposée.</summary>
    async Task<string> IM2SpeechToText.TranscribeAsync(Stream wav, CancellationToken ct) =>
        (await TranscribeAsync(wav, ct)).Text;

    /// <summary>Vérifie que le serveur STT local répond, sans audio ni effet de bord.</summary>
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response = await SendWithOneRetryAsync(
                () => new HttpRequestMessage(HttpMethod.Get, _options.SttUrl.TrimEnd('/') + "/health"),
                _options.SttTimeoutSeconds,
                cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (AssistantEngineUnavailableException)
        {
            return false;
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, long maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        while (true)
        {
            int read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > maxBytes)
            {
                throw new AssistantAudioException(AssistantMessages.InvalidAudio);
            }
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
