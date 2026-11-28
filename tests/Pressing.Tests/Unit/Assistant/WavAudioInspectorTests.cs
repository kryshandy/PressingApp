using Pressing.Core.Assistant;

namespace Pressing.Tests.Unit.Assistant;

public class WavAudioInspectorTests
{
    [Fact]
    public void Inspect_detecte_le_silence_sans_appel_reseau()
    {
        byte[] wav = AssistantTestData.Wav(1.0, amplitude: 0);

        AudioInspection result = WavAudioInspector.Inspect(wav, 20, 2 * 1024 * 1024, 250);

        Assert.True(result.SilenceDetected);
        Assert.Equal(0, result.Rms);
        Assert.Equal(1.0, result.DurationSeconds, 2);
    }

    [Fact]
    public void Inspect_reconnait_une_parole_synthetique()
    {
        byte[] wav = AssistantTestData.Wav(1.0, amplitude: 800);

        AudioInspection result = WavAudioInspector.Inspect(wav, 20, 2 * 1024 * 1024, 250);

        Assert.False(result.SilenceDetected);
        Assert.InRange(result.Rms, 700, 900);
    }

    [Fact]
    public void Inspect_refuse_un_enregistrement_de_plus_de_20_secondes()
    {
        byte[] wav = AssistantTestData.Wav(20.1);

        Assert.Throws<AssistantAudioException>(() =>
            WavAudioInspector.Inspect(wav, 20, 4 * 1024 * 1024, 250));
    }

    [Fact]
    public void Inspect_refuse_un_wav_non_pcm()
    {
        byte[] wav = AssistantTestData.NonPcmWav();

        Assert.Throws<AssistantAudioException>(() =>
            WavAudioInspector.Inspect(wav, 20, 2 * 1024 * 1024, 250));
    }

    [Fact]
    public void Inspect_refuse_un_fichier_trop_volumineux()
    {
        byte[] wav = AssistantTestData.Wav(1.0);

        Assert.Throws<AssistantAudioException>(() =>
            WavAudioInspector.Inspect(wav, 20, 100, 250));
    }
}
