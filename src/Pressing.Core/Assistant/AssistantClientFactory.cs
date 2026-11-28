namespace Pressing.Core.Assistant;

/// <summary>Point d'entrée simple pour le Membre 2 : construit les clients avec la configuration locale.</summary>
public static class AssistantClientFactory
{
    public static AssistantOptions Options => AssistantOptions.FromEnvironment();

    public static ISpeechToText CreateSpeechToText(AssistantOptions? options = null)
    {
        options ??= Options;
        return new WhisperSpeechToText(CreateHttpClient(options), options);
    }

    public static IIntentExtractor CreateIntentExtractor(AssistantOptions? options = null)
    {
        options ??= Options;
        return new LlmIntentExtractor(CreateHttpClient(options), options);
    }

    /// <summary>Façade M2 : extraction avec le DTO officiel <c>AssistantIntentDto</c>.</summary>
    public static IM2IntentExtractor CreateM2IntentExtractor(AssistantOptions? options = null) =>
        (IM2IntentExtractor)CreateIntentExtractor(options);

    /// <summary>Façade M2 : transcription textualisée (<c>string</c>).</summary>
    public static IM2SpeechToText CreateM2SpeechToText(AssistantOptions? options = null) =>
        (IM2SpeechToText)CreateSpeechToText(options);

    public static LocalAssistantPipeline CreatePipeline(AssistantOptions? options = null)
    {
        options ??= Options;
        return new LocalAssistantPipeline(
            CreateSpeechToText(options), CreateIntentExtractor(options), options);
    }

    private static HttpClient CreateHttpClient(AssistantOptions options)
    {
        int timeout = Math.Max(options.LlmTimeoutSeconds, options.SttTimeoutSeconds) + 5;
        return new HttpClient { Timeout = TimeSpan.FromSeconds(timeout) };
    }
}
