using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;
using Pressing.Core.Options;

namespace Pressing.Core.Email;

public record EmailAttachment(string FileName, string ContentType, byte[] Content);

public record EmailEnvelope(
    string To,
    string? ToName,
    string Subject,
    string Html,
    string? Text,
    IReadOnlyList<EmailAttachment>? Attachments = null);

public record EmailSendResult(bool Success, bool Simulated, string? ProviderId, string? Error);

public interface IEmailTransport
{
    string Provider { get; }
    Task<EmailSendResult> SendAsync(EmailEnvelope envelope, CancellationToken ct = default);
}

public sealed class SimulatedEmailTransport : IEmailTransport
{
    public string Provider => "simulated";

    public Task<EmailSendResult> SendAsync(EmailEnvelope envelope, CancellationToken ct = default) =>
        Task.FromResult(new EmailSendResult(true, true, "simulated", null));
}

public sealed class ResendEmailTransport : IEmailTransport
{
    private readonly HttpClient _http;
    private readonly EmailOptions _options;

    public ResendEmailTransport(HttpClient http, EmailOptions options)
    {
        _http = http;
        _options = options;
    }

    public string Provider => "resend";

    public async Task<EmailSendResult> SendAsync(EmailEnvelope envelope, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ResendApiKey);

            var payload = new Dictionary<string, object?>
            {
                ["from"] = $"{_options.ResendFromName} <{_options.ResendFromEmail}>",
                ["to"] = new[] { envelope.To },
                ["subject"] = envelope.Subject,
                ["html"] = envelope.Html,
                ["text"] = envelope.Text
            };

            if (!string.IsNullOrWhiteSpace(_options.ReplyTo))
            {
                payload["reply_to"] = _options.ReplyTo;
            }

            if (envelope.Attachments is { Count: > 0 })
            {
                payload["attachments"] = envelope.Attachments.Select(a => new
                {
                    filename = a.FileName,
                    content = Convert.ToBase64String(a.Content)
                }).ToArray();
            }

            request.Content = JsonContent.Create(payload);
            using HttpResponseMessage response = await _http.SendAsync(request, ct);
            string body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return new EmailSendResult(false, false, null, $"Resend {(int)response.StatusCode}: {body}");
            }

            string? id = null;
            try
            {
                using JsonDocument document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("id", out JsonElement idElement))
                {
                    id = idElement.GetString();
                }
            }
            catch (JsonException)
            {
                // La réponse reste un succès même si l'identifiant n'est pas exploitable.
            }

            return new EmailSendResult(true, false, id, null);
        }
        catch (Exception ex)
        {
            return new EmailSendResult(false, false, null, ex.Message);
        }
    }
}

public sealed class SmtpEmailTransport : IEmailTransport
{
    private readonly EmailOptions _options;

    public SmtpEmailTransport(EmailOptions options)
    {
        _options = options;
    }

    public string Provider => "smtp";

    public async Task<EmailSendResult> SendAsync(EmailEnvelope envelope, CancellationToken ct = default)
    {
        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(_options.FromEmail, _options.FromName),
                Subject = envelope.Subject,
                Body = envelope.Html,
                IsBodyHtml = true
            };

            message.To.Add(new MailAddress(envelope.To, envelope.ToName));
            if (!string.IsNullOrWhiteSpace(_options.ReplyTo))
            {
                message.ReplyToList.Add(_options.ReplyTo);
            }

            if (envelope.Attachments is { Count: > 0 })
            {
                foreach (EmailAttachment attachment in envelope.Attachments)
                {
                    var stream = new MemoryStream(attachment.Content, writable: false);
                    message.Attachments.Add(new Attachment(stream, attachment.FileName, attachment.ContentType));
                }
            }

            using var client = new SmtpClient(_options.SmtpHost, _options.SmtpPort)
            {
                EnableSsl = _options.SmtpUseSsl,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(_options.SmtpUser, _options.SmtpAppPassword),
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 20_000
            };

            await client.SendMailAsync(message, ct);
            return new EmailSendResult(true, false, null, null);
        }
        catch (Exception ex)
        {
            return new EmailSendResult(false, false, null, ex.Message);
        }
    }
}
