using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Pressing.Core.Messaging;

public sealed class MetaWhatsAppTransport : IWhatsAppTransport
{
    private readonly HttpClient _http;
    private readonly WhatsAppOptions _options;

    public MetaWhatsAppTransport(HttpClient http, WhatsAppOptions options)
    {
        _http = http;
        _options = options;
    }

    public string Provider => "meta";

    /// <summary>
    /// Message de type « template » (obligatoire hors fenêtre de 24 h) : nom du modèle, langue et variables du corps.
    /// Sans variable, le composant « body » est omis (Meta refuse un composant sans paramètre).
    /// </summary>
    private object BuildTemplatePayload(string to, string template, IReadOnlyList<string>? variables)
    {
        object[] components = variables is { Count: > 0 }
            ? [new
            {
                type = "body",
                // Meta refuse les retours à la ligne et les tabulations dans une variable de modèle.
                parameters = variables
                    .Select(v => new { type = "text", text = Clean(v) })
                    .ToArray()
            }]
            : [];

        return new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to,
            type = "template",
            template = new
            {
                name = template,
                language = new
                {
                    code = string.IsNullOrWhiteSpace(_options.TemplateLanguage) ? "fr" : _options.TemplateLanguage.Trim()
                },
                components
            }
        };
    }

    private static string Clean(string value)
    {
        string text = string.Join(' ', value.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return text.Length == 0 ? "-" : text;
    }

    public async Task<WhatsAppSendResult> SendAsync(
        WhatsAppMessage message,
        CancellationToken ct = default)
    {
        if (!_options.IsConfigured)
            return new(false, false, null, "Configuration WhatsApp Meta incomplète.");

        if (string.IsNullOrWhiteSpace(message.To))
            return new(false, false, null, "Destinataire manquant.");

        if (string.IsNullOrWhiteSpace(message.Body))
            return new(false, false, null, "Message vide.");

        try
        {
            string version = _options.ApiVersion.Trim('/');
            string url = $"https://graph.facebook.com/{version}/{_options.PhoneNumberId}/messages";

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.AccessToken);

            // Meta attend l'indicatif international en chiffres seuls (« 2250700112233 ») :
            // les espaces, « + » et tirets saisis dans la fiche client sont retirés.
            string to = new string(message.To.Where(char.IsDigit).ToArray());
            string? template = _options.TemplateFor(message.ReminderType);

            request.Content = template is null
                ? JsonContent.Create(new
                {
                    messaging_product = "whatsapp",
                    recipient_type = "individual",
                    to,
                    type = "text",
                    text = new { body = message.Body }
                })
                : JsonContent.Create(BuildTemplatePayload(to, template, message.Variables));

            using HttpResponseMessage response = await _http.SendAsync(request, ct);
            string content = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                return new(false, false, null,
                    $"Meta API {(int)response.StatusCode}: {content}");

            using JsonDocument json = JsonDocument.Parse(content);
            string? id = null;

            if (json.RootElement.TryGetProperty("messages", out var messages) &&
                messages.ValueKind == JsonValueKind.Array &&
                messages.GetArrayLength() > 0 &&
                messages[0].TryGetProperty("id", out var idElement))
            {
                id = idElement.GetString();
            }

            return new(true, false, id, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new(false, false, null, ex.Message);
        }
    }
}
