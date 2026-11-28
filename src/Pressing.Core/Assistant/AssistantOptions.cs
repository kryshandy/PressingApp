using System.Globalization;

namespace Pressing.Core.Assistant;

/// <summary>Configuration IA lue depuis l'environnement (ou le .env chargé par l'application).</summary>
public sealed record AssistantOptions
{
    public string LlmUrl { get; init; } = "http://127.0.0.1:8081";
    public string SttUrl { get; init; } = "http://127.0.0.1:8082";
    public int LlmTimeoutSeconds { get; init; } = 60;
    public int SttTimeoutSeconds { get; init; } = 30;
    public double ConfidenceThreshold { get; init; } = 0.6;
    public int MaxAudioSeconds { get; init; } = 20;
    public long MaxAudioBytes { get; init; } = 2 * 1024 * 1024;
    public int SilenceRmsThreshold { get; init; } = 250;
    public string LlmModelAlias { get; init; } = "pressing-local";
    public double LlmTemperature { get; init; } = 0.1;
    public string SttPrompt { get; init; } = "";

    public static AssistantOptions FromEnvironment()
    {
        string promptFile = Expand(Environment.GetEnvironmentVariable("ASSISTANT_STT_PROMPT_FILE") ?? "");
        if (string.IsNullOrWhiteSpace(promptFile))
        {
            // update-vocab.ps1 ecrit cet indice depuis le catalogue public (lecture seule).
            // Le module IA ne lit jamais SQLite : il consomme uniquement ce fichier genere.
            promptFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Pressing", "ai", "stt-vocab.txt");
        }
        string prompt = ReadPromptFile(promptFile) ?? AssistantResources.DefaultVocabulary;

        var options = new AssistantOptions
        {
            LlmUrl = Get("ASSISTANT_LLM_URL", "http://127.0.0.1:8081"),
            SttUrl = Get("ASSISTANT_STT_URL", "http://127.0.0.1:8082"),
            LlmTimeoutSeconds = GetInt("ASSISTANT_LLM_TIMEOUT_SECONDS", 60, 1, 600),
            SttTimeoutSeconds = GetInt("ASSISTANT_STT_TIMEOUT_SECONDS", 30, 1, 600),
            ConfidenceThreshold = GetDouble("ASSISTANT_CONFIDENCE_THRESHOLD", 0.6, 0.0, 1.0),
            MaxAudioSeconds = GetInt("ASSISTANT_MAX_AUDIO_SECONDS", 20, 1, 60),
            MaxAudioBytes = GetLong("ASSISTANT_MAX_AUDIO_BYTES", 2 * 1024 * 1024, 1024, 64L * 1024 * 1024),
            SilenceRmsThreshold = GetInt("ASSISTANT_SILENCE_RMS_THRESHOLD", 250, 0, 32767),
            LlmModelAlias = Get("ASSISTANT_LLM_MODEL_ALIAS", "pressing-local"),
            LlmTemperature = GetDouble("ASSISTANT_LLM_TEMPERATURE", 0.1, 0.0, 0.2),
            SttPrompt = prompt
        };

        options.EnsureLoopbackUrls();
        return options;
    }

    public void EnsureLoopbackUrls()
    {
        EnsureLoopback(LlmUrl, nameof(LlmUrl));
        EnsureLoopback(SttUrl, nameof(SttUrl));
    }

    private static void EnsureLoopback(string url, string name)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"{name} doit être une URL HTTP valide : {url}");
        }

        bool loopback = uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                        uri.Host.Equals("127.0.0.1", StringComparison.Ordinal) ||
                        uri.Host.Equals("::1", StringComparison.Ordinal) ||
                        System.Net.IPAddress.TryParse(uri.Host, out System.Net.IPAddress? address) && address is not null && System.Net.IPAddress.IsLoopback(address);
        if (!loopback)
        {
            throw new InvalidOperationException($"{name} doit rester sur la boucle locale (127.0.0.1) : {url}");
        }
    }

    private static string Get(string name, string fallback)
    {
        string value = Environment.GetEnvironmentVariable(name) ?? "";
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static int GetInt(string name, int fallback, int min, int max)
    {
        string value = Environment.GetEnvironmentVariable(name) ?? "";
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed < min || parsed > max)
        {
            throw new InvalidOperationException($"{name} doit être un entier entre {min} et {max} : {value}");
        }
        return parsed;
    }

    private static long GetLong(string name, long fallback, long min, long max)
    {
        string value = Environment.GetEnvironmentVariable(name) ?? "";
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) || parsed < min || parsed > max)
        {
            throw new InvalidOperationException($"{name} doit être un entier entre {min} et {max} : {value}");
        }
        return parsed;
    }

    private static double GetDouble(string name, double fallback, double min, double max)
    {
        string value = Environment.GetEnvironmentVariable(name) ?? "";
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) || parsed < min || parsed > max)
        {
            throw new InvalidOperationException($"{name} doit être un nombre entre {min:F1} et {max:F1} : {value}");
        }
        return parsed;
    }

    private static string? ReadPromptFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            string content = File.ReadAllText(path, System.Text.Encoding.UTF8);
            return string.IsNullOrWhiteSpace(content) ? null : content;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static string Expand(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        string expanded = Environment.ExpandEnvironmentVariables(value);
        return Path.GetFullPath(expanded);
    }
}
