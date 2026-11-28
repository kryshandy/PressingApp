using System.Globalization;

namespace Pressing.Core.Assistant;

/// <summary>
/// Réglages de l'assistant lus dans l'environnement (<c>ASSISTANT_ENABLED</c>, <c>ASSISTANT_CONFIDENCE_THRESHOLD</c>).
/// L'écran Paramètres (T2.6) les rendra modifiables depuis le Desktop.
/// </summary>
public sealed class AssistantOptions
{
    /// <summary>Interrupteur général : désactivé, les routes de traitement répondent 503 et rien n'est appelé.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Confiance minimale (0,0 à 1,0) en dessous de laquelle l'assistant « n'a pas compris ».</summary>
    public double ConfidenceThreshold { get; set; } = Dtos.AssistantContract.DefaultConfidenceThreshold;

    public static AssistantOptions FromEnvironment()
    {
        var options = new AssistantOptions();

        string? enabled = Environment.GetEnvironmentVariable("ASSISTANT_ENABLED");
        if (!string.IsNullOrWhiteSpace(enabled))
        {
            options.Enabled = !enabled.Trim().ToLowerInvariant().Equals("false")
                              && !enabled.Trim().Equals("0")
                              && !enabled.Trim().ToLowerInvariant().Equals("non")
                              && !enabled.Trim().ToLowerInvariant().Equals("no");
        }

        string? threshold = Environment.GetEnvironmentVariable("ASSISTANT_CONFIDENCE_THRESHOLD");
        if (!string.IsNullOrWhiteSpace(threshold)
            && double.TryParse(threshold.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            && value is > 0 and <= 1)
        {
            options.ConfidenceThreshold = value;
        }

        return options;
    }
}
