namespace Pressing.Core.Assistant;

/// <summary>Transcription locale. Le flux est un WAV 16 kHz mono PCM 16 bits ; il n'est jamais écrit sur le disque.</summary>
public interface ISpeechToText
{
    Task<SpeechTranscription> TranscribeAsync(Stream wav, CancellationToken cancellationToken = default);
    Task<bool> IsReadyAsync(CancellationToken cancellationToken = default);
}
