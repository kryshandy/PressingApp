namespace Pressing.Core.Assistant;

/// <summary>Implémentation factice pour les tests : aucun moteur, aucun fichier, aucun réseau.</summary>
public sealed class FakeSpeechToText : ISpeechToText, IM2SpeechToText
{
    private readonly Queue<SpeechTranscription> _results = new();

    public SpeechTranscription? Default { get; set; }
    public bool Ready { get; set; } = true;
    public bool ReadyAfterFailure { get; set; }
    public int CallCount { get; private set; }

    public void Enqueue(string text, bool silence = false) =>
        _results.Enqueue(new SpeechTranscription(text, 1.0, silence ? 0 : 800, silence));

    public void Enqueue(SpeechTranscription transcription) => _results.Enqueue(transcription);

    public Task<SpeechTranscription> TranscribeAsync(Stream wav, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wav);
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;

        if (_results.Count > 0) return Task.FromResult(_results.Dequeue());
        if (Default is not null) return Task.FromResult(Default);

        throw new InvalidOperationException("Aucune transcription factice configurée.");
    }

    async Task<string> IM2SpeechToText.TranscribeAsync(Stream wav, CancellationToken ct) =>
        (await TranscribeAsync(wav, ct)).Text;

    public Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ReadyAfterFailure)
        {
            ReadyAfterFailure = false;
            return Task.FromResult(false);
        }
        return Task.FromResult(Ready);
    }
}
