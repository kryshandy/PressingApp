namespace Pressing.Core.Assistant;

/// <summary>
/// Enchaîne transcription et extraction. Aucun accès base de données, aucune création,
/// aucune modification : le résultat est un brouillon que le Membre 2 valide.
/// </summary>
public sealed class LocalAssistantPipeline
{
    private readonly ISpeechToText _speechToText;
    private readonly IIntentExtractor _intentExtractor;
    private readonly AssistantOptions _options;

    public LocalAssistantPipeline(
        ISpeechToText speechToText,
        IIntentExtractor intentExtractor,
        AssistantOptions? options = null)
    {
        _speechToText = speechToText ?? throw new ArgumentNullException(nameof(speechToText));
        _intentExtractor = intentExtractor ?? throw new ArgumentNullException(nameof(intentExtractor));
        _options = options ?? AssistantOptions.FromEnvironment();
    }

    public async Task<PipelineResult> ProcessAsync(
        Stream wav,
        CatalogHints catalog,
        CancellationToken cancellationToken = default)
    {
        try
        {
            SpeechTranscription transcription = await _speechToText.TranscribeAsync(wav, cancellationToken);
            if (transcription.SilenceDetected || string.IsNullOrWhiteSpace(transcription.Text))
            {
                return new PipelineResult(
                    PipelineStatus.Silence, transcription.Text, AssistantMessages.Silence, null, []);
            }

            return await InterpretAsync(transcription.Text, catalog, cancellationToken);
        }
        catch (AssistantAudioException ex)
        {
            return new PipelineResult(PipelineStatus.InvalidAudio, "", ex.Message, null, []);
        }
        catch (Exception ex) when (ex is AssistantEngineUnavailableException or AssistantUnavailableException)
        {
            return new PipelineResult(PipelineStatus.EngineUnavailable, "", ex.Message, null, []);
        }
        catch (AssistantContractException ex)
        {
            return new PipelineResult(
                PipelineStatus.NotUnderstood, "", AssistantMessages.NotUnderstood, null, [ex.Message]);
        }
    }

    public async Task<PipelineResult> InterpretAsync(
        string texte,
        CatalogHints catalog,
        CancellationToken cancellationToken = default)
    {
        try
        {
            IntentExtraction extraction = await _intentExtractor.ExtractAsync(texte, catalog, cancellationToken);
            // Le statut dépend de l'état final, pas de l'historique des avertissements :
            // une valeur hors catalogue corrigée depuis la transcription est exploitable.
            bool rejectedValue = extraction.Payload.Items.Any(item =>
                item.Category is not null && !catalog.ContainsCategory(item.Category) ||
                item.Service is not null && !catalog.ContainsService(item.Service));
            bool unclear = extraction.Payload.Intent == AssistantIntentions.Unknown ||
                           extraction.Payload.Confidence < _options.ConfidenceThreshold ||
                           rejectedValue;

            return unclear
                ? new PipelineResult(
                    PipelineStatus.NotUnderstood,
                    texte,
                    AssistantMessages.NotUnderstood,
                    extraction,
                    extraction.Warnings)
                : new PipelineResult(PipelineStatus.Ok, texte, "Brouillon prêt", extraction, extraction.Warnings);
        }
        catch (Exception ex) when (ex is AssistantEngineUnavailableException or AssistantUnavailableException)
        {
            return new PipelineResult(PipelineStatus.EngineUnavailable, texte, ex.Message, null, []);
        }
        catch (AssistantContractException ex)
        {
            return new PipelineResult(
                PipelineStatus.NotUnderstood, texte, AssistantMessages.NotUnderstood, null, [ex.Message]);
        }
    }
}
