namespace Pressing.Core.Assistant;

// ============================================================================================
// TEMPORAIRE (T2.3) — signatures validées avec M1 (membre 1), qui possède ces types (T1.2).
// Ce fichier n'existe que pour que les routes /api/assistant/* compilent et soient testées avant
// la PR de M1. DÈS QUE LA PR DE M1 EST FUSIONNÉE : supprimer ce fichier (M1 fournit les mêmes types).
// ============================================================================================

/// <summary>Reconnaissance vocale locale (whisper.cpp) : WAV → texte français.</summary>
public interface ISpeechToText
{
    Task<string> TranscribeAsync(Stream wav, CancellationToken ct = default);

    Task<bool> IsReadyAsync(CancellationToken ct = default);
}

/// <summary>Extraction d'intention locale (llama.cpp) : texte → <see cref="Dtos.AssistantIntentDto"/> sous contrainte de grammaire.</summary>
public interface IIntentExtractor
{
    Task<Dtos.AssistantIntentDto> ExtractAsync(string text, CatalogHints hints, CancellationToken ct = default);

    Task<bool> IsReadyAsync(CancellationToken ct = default);
}

/// <summary>Catalogue injecté dans le prompt (jamais appris par le modèle).</summary>
public record CatalogHints(IReadOnlyList<string> Categories, IReadOnlyList<string> Services);

/// <summary>Moteur arrêté ou introuvable ; le message est affiché tel quel au caissier.</summary>
public class AssistantUnavailableException : Exception
{
    public AssistantUnavailableException() : base("L'assistant vocal n'est pas démarré")
    {
    }

    public AssistantUnavailableException(string message) : base(message)
    {
    }

    public AssistantUnavailableException(string message, Exception inner) : base(message, inner)
    {
    }
}
