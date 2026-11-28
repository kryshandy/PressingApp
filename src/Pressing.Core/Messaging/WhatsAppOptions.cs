namespace Pressing.Core.Messaging;

public sealed class WhatsAppOptions
{
    public string Provider { get; set; } = "simulated";
    public string AccessToken { get; set; } = string.Empty;
    public string PhoneNumberId { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = "v23.0";
    public string WebhookVerifyToken { get; set; } = string.Empty;
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>
    /// Noms des modèles WhatsApp approuvés par Meta, un par type de rappel (variables .env WHATSAPP_TEMPLATE_*).
    /// Hors de la fenêtre de 24 h après un message du client, Meta n'accepte que des modèles : tant qu'un nom est
    /// vide, le rappel correspondant part en texte libre.
    /// </summary>
    public string TemplateReady { get; set; } = string.Empty;
    public string TemplateUncollected { get; set; } = string.Empty;
    public string TemplateBalance { get; set; } = string.Empty;
    public string TemplateDue { get; set; } = string.Empty;
    public string TemplateFeedback { get; set; } = string.Empty;

    /// <summary>Code de langue des modèles (WHATSAPP_TEMPLATE_LANGUAGE), « fr » par défaut.</summary>
    public string TemplateLanguage { get; set; } = "fr";

    /// <summary>Nom du modèle configuré pour un type de rappel (« ready », « uncollected-j7 »…), ou null (texte libre).</summary>
    public string? TemplateFor(string? reminderType)
    {
        string name = reminderType switch
        {
            "ready" => TemplateReady,
            "uncollected-j3" or "uncollected-j7" or "uncollected-j14" => TemplateUncollected,
            "unpaid-balance" => TemplateBalance,
            "due-tomorrow" => TemplateDue,
            "feedback" => TemplateFeedback,
            _ => string.Empty
        };

        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccessToken) &&
        !string.IsNullOrWhiteSpace(PhoneNumberId);
}
