using System.Buffers.Binary;

namespace Pressing.Core.Assistant;

public sealed record AudioInspection(double DurationSeconds, double Rms, bool SilenceDetected);

/// <summary>
/// Contrôle des WAV avant envoi au moteur STT : format PCM 16 bits, durée maximale et silence.
/// L'analyse se fait en mémoire ; aucun fichier temporaire n'est créé.
/// </summary>
public static class WavAudioInspector
{
    public static AudioInspection Inspect(byte[] wav, int maxAudioSeconds, long maxAudioBytes, int silenceRmsThreshold)
    {
        ArgumentNullException.ThrowIfNull(wav);
        if (wav.Length == 0)
        {
            throw new AssistantAudioException(AssistantMessages.InvalidAudio);
        }
        if (wav.Length > maxAudioBytes)
        {
            throw new AssistantAudioException($"Fichier audio trop volumineux ({wav.Length} octets).");
        }
        if (wav.Length < 44 || !HasHeader(wav, "RIFF") || !HasHeader(wav.AsSpan(8), "WAVE"))
        {
            throw new AssistantAudioException("Format WAV invalide.");
        }

        int fmtOffset = -1;
        int dataOffset = -1;
        int dataLength = -1;
        int cursor = 12;

        while (cursor + 8 <= wav.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(wav, cursor, 4);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(cursor + 4, 4));
            int payload = cursor + 8;
            long sizeLong = size;
            if (payload + sizeLong > wav.Length)
            {
                throw new AssistantAudioException("Chunk WAV tronqué.");
            }

            if (id == "fmt " && size >= 16) fmtOffset = payload;
            if (id == "data")
            {
                dataOffset = payload;
                dataLength = (int)size;
            }

            long next = payload + sizeLong + (sizeLong & 1);
            if (next > int.MaxValue) break;
            cursor = (int)next;
        }

        if (fmtOffset < 0 || dataOffset < 0 || dataLength <= 0)
        {
            throw new AssistantAudioException("En-tête WAV incomplet (fmt ou data absent).");
        }

        ushort format = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(fmtOffset, 2));
        ushort channels = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(fmtOffset + 2, 2));
        uint sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(fmtOffset + 4, 4));
        ushort bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(fmtOffset + 14, 2));
        ushort blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(fmtOffset + 12, 2));

        if (format != 1 || bitsPerSample != 16 || channels == 0 || sampleRate == 0)
        {
            throw new AssistantAudioException("WAV accepté : PCM 16 bits uniquement.");
        }
        if (blockAlign != channels * 2)
        {
            throw new AssistantAudioException("Bloc d'échantillons WAV incohérent.");
        }

        double duration = dataLength / (double)sampleRate / blockAlign;
        if (duration > maxAudioSeconds)
        {
            throw new AssistantAudioException($"Enregistrement trop long : {duration:F1} s (maximum {maxAudioSeconds} s).");
        }

        double sum = 0;
        long samples = 0;
        int end = dataOffset + dataLength;
        for (int i = dataOffset; i + 1 < end; i += 2)
        {
            short value = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(i, 2));
            sum += (double)value * value;
            samples++;
        }

        double rms = samples == 0 ? 0 : Math.Sqrt(sum / samples);
        return new AudioInspection(duration, rms, rms < silenceRmsThreshold);
    }

    private static bool HasHeader(byte[] bytes, string expected) =>
        System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == expected;

    private static bool HasHeader(ReadOnlySpan<byte> bytes, string expected) =>
        System.Text.Encoding.ASCII.GetString(bytes[..4]) == expected;
}
