using Microsoft.EntityFrameworkCore;
using Pressing.Core.Data;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Core.Options;
using Pressing.Core.Reports;
using Pressing.Core.Services;

namespace Pressing.Core.Email;

public class EmailService
{
    private readonly PressingDbContext _db;
    private readonly SettingsService _settings;
    private readonly EmailTemplateService _templates;
    private readonly IEmailTransport _transport;
    private readonly EmailOptions _options;
    private readonly ReceiptService _receipts;
    private readonly SemaphoreSlim _processing = new(1, 1);

    public EmailService(
        PressingDbContext db,
        SettingsService settings,
        EmailTemplateService templates,
        IEmailTransport transport,
        EmailOptions options,
        ReceiptService receipts)
    {
        _db = db;
        _settings = settings;
        _templates = templates;
        _transport = transport;
        _options = options;
        _receipts = receipts;
    }

    public async Task<EmailMessage?> QueueOrderConfirmationAsync(Order order, CancellationToken ct = default)
    {
        if (!await _settings.GetBoolAsync(SettingsKeys.EmailOrderConfirmation, true, ct))
        {
            return null;
        }

        EmailContent content = await _templates.OrderConfirmationAsync(order, ct);
        return await QueueAsync(order.Client, content, "order-confirmation", order.Id, ct: ct);
    }

    public async Task<EmailMessage?> QueueOrderReadyAsync(Order order, CancellationToken ct = default)
    {
        if (!await _settings.GetBoolAsync(SettingsKeys.EmailOrderReady, true, ct))
        {
            return null;
        }

        EmailContent content = await _templates.OrderReadyAsync(order, ct);
        return await QueueAsync(order.Client, content, "order-ready", order.Id, ct: ct);
    }

    public async Task<EmailMessage?> QueueOrderCancelledAsync(Order order, string reason, CancellationToken ct = default)
    {
        EmailContent content = await _templates.OrderCancelledAsync(order, reason, ct);
        return await QueueAsync(order.Client, content, "order-cancelled", order.Id, ct: ct);
    }

    public async Task<EmailMessage?> QueueWelcomeAsync(Client client, string? temporaryPassword, CancellationToken ct = default)
    {
        EmailContent content = await _templates.WelcomeAsync(client, temporaryPassword, ct);
        return await QueueAsync(client, content, "welcome", ct: ct);
    }

    public async Task<EmailMessage?> QueuePasswordResetAsync(Client client, string token, CancellationToken ct = default)
    {
        EmailContent content = await _templates.PasswordResetAsync(client, token, ct);
        return await QueueAsync(client, content, "password-reset", ct: ct);
    }

    public async Task<EmailMessage?> QueueReceiptAsync(Order order, CancellationToken ct = default)
    {
        if (!await _settings.GetBoolAsync(SettingsKeys.EmailReceipt, true, ct))
        {
            return null;
        }

        EmailContent content = await _templates.ReceiptAsync(order, ct);
        byte[] pdf = await _receipts.GeneratePdfAsync(order, ct);
        return await QueueAsync(
            order.Client,
            content,
            "receipt",
            order.Id,
            new EmailAttachment($"recu-{order.TicketNumber}.pdf", "application/pdf", pdf),
            ct);
    }

    public async Task<EmailMessage> QueueRawAsync(
        string to,
        string? toName,
        string subject,
        string html,
        string? text = null,
        int? clientId = null,
        int? orderId = null,
        CancellationToken ct = default,
        string template = "generic")
    {
        var message = new EmailMessage
        {
            ToEmail = to,
            ToName = toName,
            Subject = subject,
            HtmlBody = html,
            TextBody = text,
            Template = template,
            Status = EmailStatus.Pending,
            CreatedAt = DateTime.Now,
            RelatedClientId = clientId,
            RelatedOrderId = orderId
        };

        _db.EmailMessages.Add(message);
        await _db.SaveChangesAsync(ct);
        return message;
    }

    public async Task<EmailMessageDto?> SendTestAsync(string to, CancellationToken ct = default)
    {
        var content = await _templates.GenericAsync(
            "Test de configuration e-mail",
            "Si vous recevez ce message, la configuration e-mail de Pressing Éclat fonctionne correctement.",
            ct);

        EmailMessage message = await QueueRawAsync(to, null, content.Subject, content.Html, content.Text, ct: ct);
        await ProcessMessageAsync(message, ct);
        return ToDto(message);
    }

    public async Task<EmailMessageDto?> RetryAsync(int id, CancellationToken ct = default)
    {
        EmailMessage? message = await _db.EmailMessages.FindAsync([id], ct);
        if (message is null) return null;

        message.Status = EmailStatus.Pending;
        message.Error = null;
        await _db.SaveChangesAsync(ct);
        await ProcessMessageAsync(message, ct);
        return ToDto(message);
    }

    public async Task<int> ProcessPendingAsync(int max = 25, CancellationToken ct = default)
    {
        if (!await _processing.WaitAsync(0, ct))
        {
            return 0;
        }

        try
        {
            var pending = await _db.EmailMessages
                .Where(m => (m.Status == EmailStatus.Pending || m.Status == EmailStatus.Failed) && m.Attempts < 5)
                .OrderBy(m => m.CreatedAt)
                .Take(max)
                .ToListAsync(ct);

            int sent = 0;
            foreach (EmailMessage message in pending)
            {
                bool success = await ProcessMessageAsync(message, ct);
                if (success) sent++;
            }

            return sent;
        }
        finally
        {
            _processing.Release();
        }
    }

    public async Task<PagedResult<EmailMessageDto>> GetOutboxAsync(
        int page = 1,
        int pageSize = 30,
        EmailStatus? status = null,
        CancellationToken ct = default)
    {
        IQueryable<EmailMessage> query = _db.EmailMessages.AsNoTracking();
        if (status.HasValue)
        {
            query = query.Where(m => m.Status == status.Value);
        }

        int total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((Math.Max(1, page) - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new EmailMessageDto(
                m.Id, m.ToEmail, m.ToName, m.Subject, m.Template, m.Status, m.Status.ToString(),
                m.CreatedAt, m.SentAt, m.Error, m.Attempts, m.RelatedOrderId, m.RelatedClientId))
            .ToListAsync(ct);

        return new PagedResult<EmailMessageDto>(items, total, page, pageSize);
    }

    public EmailConfigStatusDto GetConfigStatus() => _settings.GetEmailConfigStatus();

    private async Task<EmailMessage?> QueueAsync(
        Client client,
        EmailContent content,
        string template,
        int? orderId = null,
        EmailAttachment? attachment = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(client.Email) ||
            !await _settings.GetBoolAsync(SettingsKeys.EmailNotifications, true, ct))
        {
            return null;
        }

        var message = new EmailMessage
        {
            ToEmail = client.Email,
            ToName = client.FullName,
            Subject = content.Subject,
            HtmlBody = content.Html,
            TextBody = content.Text,
            Template = template,
            Status = EmailStatus.Pending,
            CreatedAt = DateTime.Now,
            RelatedOrderId = orderId,
            RelatedClientId = client.Id,
            AttachmentFileName = attachment?.FileName,
            AttachmentContentType = attachment?.ContentType,
            AttachmentData = attachment?.Content
        };

        _db.EmailMessages.Add(message);
        await _db.SaveChangesAsync(ct);
        return message;
    }

    private async Task<bool> ProcessMessageAsync(EmailMessage message, CancellationToken ct)
    {
        if (message.Template == "reminder")
        {
            var client = message.RelatedClientId.HasValue
                ? await _db.Clients.FirstOrDefaultAsync(
                    c => c.Id == message.RelatedClientId.Value, ct)
                : null;

            if (client is null ||
                client.NotificationPreference != ClientNotificationPreference.Email ||
                !client.ConsentGivenAt.HasValue ||
                client.ConsentRevokedAt.HasValue)
            {
                message.Status = EmailStatus.Cancelled;
                message.Error = "Rappel annulé : consentement e-mail absent ou révoqué.";
                await _db.SaveChangesAsync(ct);
                return false;
            }
        }

        message.Attempts++;
        try
        {
            var envelope = new EmailEnvelope(
                message.ToEmail,
                message.ToName,
                message.Subject,
                message.HtmlBody,
                message.TextBody,
                message.AttachmentData is { Length: > 0 } && !string.IsNullOrWhiteSpace(message.AttachmentFileName)
                    ? [new EmailAttachment(
                        message.AttachmentFileName,
                        message.AttachmentContentType ?? "application/octet-stream",
                        message.AttachmentData)]
                    : null);

            EmailSendResult result = await _transport.SendAsync(envelope, ct);
            if (result.Success)
            {
                message.Status = result.Simulated ? EmailStatus.Simulated : EmailStatus.Sent;
                message.SentAt = DateTime.Now;
                message.Error = null;
            }
            else
            {
                message.Status = EmailStatus.Failed;
                message.Error = Truncate(result.Error, 1000);
            }
        }
        catch (Exception ex)
        {
            message.Status = EmailStatus.Failed;
            message.Error = Truncate(ex.Message, 1000);
        }

        await _db.SaveChangesAsync(ct);
        return message.Status is EmailStatus.Sent or EmailStatus.Simulated;
    }

    private static EmailMessageDto ToDto(EmailMessage m) => new(
        m.Id, m.ToEmail, m.ToName, m.Subject, m.Template, m.Status, m.Status.ToString(),
        m.CreatedAt, m.SentAt, m.Error, m.Attempts, m.RelatedOrderId, m.RelatedClientId);

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];
}
