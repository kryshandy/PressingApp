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

            request.Content = JsonContent.Create(new
            {
                messaging_product = "whatsapp",
                recipient_type = "individual",
                to = message.To,
                type = "text",
                text = new { body = message.Body }
            });

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
