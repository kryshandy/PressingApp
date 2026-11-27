namespace Pressing.Core.Messaging;

public record WhatsAppMessage(
    string To,
    string Body);

public record WhatsAppSendResult(
    bool Success,
    bool Simulated,
    string? ProviderMessageId,
    string? Error);

public interface IWhatsAppTransport
{
    string Provider { get; }

    Task<WhatsAppSendResult> SendAsync(
        WhatsAppMessage message,
        CancellationToken ct = default);
}

public sealed class SimulatedWhatsAppTransport : IWhatsAppTransport
{
    public string Provider => "simulated";

    public Task<WhatsAppSendResult> SendAsync(
        WhatsAppMessage message,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(message.To))
        {
            return Task.FromResult(
                new WhatsAppSendResult(false, true, null, "Destinataire manquant."));
        }

        if (string.IsNullOrWhiteSpace(message.Body))
        {
            return Task.FromResult(
                new WhatsAppSendResult(false, true, null, "Message vide."));
        }

        return Task.FromResult(
            new WhatsAppSendResult(true, true, "simulated", null));
    }
}
