using System.Net;
using System.Text;
using Pressing.Core.Assistant;

namespace Pressing.Tests.Unit.Assistant;

public class WhisperSpeechToTextTests
{
    [Fact]
    public async Task Transcrit_un_wav_via_le_serveur_local()
    {
        var handler = new StubHandler().EnqueueJson("{\"text\":\"dépôt pour trois chemises\"}");
        using var http = new HttpClient(handler);
        var client = new WhisperSpeechToText(http, new AssistantOptions { SttPrompt = "Chemise, Repassage" });
        using var wav = new MemoryStream(AssistantTestData.Wav(0.5));

        SpeechTranscription result = await client.TranscribeAsync(wav);

        Assert.Equal("dépôt pour trois chemises", result.Text);
        Assert.False(result.SilenceDetected);
        Assert.Equal("http://127.0.0.1:8082/inference", handler.Uris[0].ToString());
        // .NET peut écrire name=file ou name="file" selon la version : on normalise les guillemets.
        string body = handler.Bodies[0].Replace("\"", "");
        Assert.Contains("name=file", body);
        Assert.Contains("audio.wav", body);
        Assert.Contains("name=language", body);
        Assert.Contains("name=response_format", body);
        Assert.Contains("Chemise, Repassage", handler.Bodies[0]);
    }

    [Fact]
    public async Task Un_silence_ne_declenche_aucun_appel_au_serveur()
    {
        var handler = new StubHandler();
        using var http = new HttpClient(handler);
        var client = new WhisperSpeechToText(http, new AssistantOptions());
        using var wav = new MemoryStream(AssistantTestData.Wav(0.5, amplitude: 0));

        SpeechTranscription result = await client.TranscribeAsync(wav);

        Assert.True(result.SilenceDetected);
        Assert.Equal("", result.Text);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Un_moteur_arrete_renvoie_le_message_exact()
    {
        var handler = new StubHandler()
            .Throw(new HttpRequestException("refused"))
            .Throw(new HttpRequestException("refused"));
        using var http = new HttpClient(handler);
        var client = new WhisperSpeechToText(http, new AssistantOptions());
        using var wav = new MemoryStream(AssistantTestData.Wav(0.5));

        AssistantEngineUnavailableException error = await Assert.ThrowsAsync<AssistantEngineUnavailableException>(
            () => client.TranscribeAsync(wav));

        Assert.Equal(AssistantMessages.EngineNotStarted, error.Message);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Un_delai_depasse_renvoie_un_message_clair()
    {
        var handler = new StubHandler().Timeout().Timeout();
        using var http = new HttpClient(handler);
        var options = new AssistantOptions { SttTimeoutSeconds = 1 };
        var client = new WhisperSpeechToText(http, options);
        using var wav = new MemoryStream(AssistantTestData.Wav(0.5));

        AssistantEngineUnavailableException error = await Assert.ThrowsAsync<AssistantEngineUnavailableException>(
            () => client.TranscribeAsync(wav));

        Assert.Equal(AssistantMessages.EngineTimeout, error.Message);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Un_wav_invalide_est_refuse_avant_l_appel_http()
    {
        var handler = new StubHandler().EnqueueJson("{\"text\":\"ignoré\"}");
        using var http = new HttpClient(handler);
        var client = new WhisperSpeechToText(http, new AssistantOptions());
        using var wav = new MemoryStream(AssistantTestData.NonPcmWav());

        await Assert.ThrowsAsync<AssistantAudioException>(() => client.TranscribeAsync(wav));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Un_erreur_400_est_une_erreur_audio()
    {
        var handler = new StubHandler().Enqueue(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error\":\"bad audio\"}", Encoding.UTF8, "application/json")
        });
        using var http = new HttpClient(handler);
        var client = new WhisperSpeechToText(http, new AssistantOptions());
        using var wav = new MemoryStream(AssistantTestData.Wav(0.5));

        await Assert.ThrowsAsync<AssistantAudioException>(() => client.TranscribeAsync(wav));
    }
}
