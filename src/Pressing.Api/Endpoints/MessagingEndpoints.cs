using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pressing.Core.Data;
using Pressing.Core.Dtos;
using Pressing.Core.Entities;
using Pressing.Core.Messaging;
using Pressing.Core.Security;

namespace Pressing.Api.Endpoints;

/// <summary>Messagerie : webhook WhatsApp (Meta) et désinscription des rappels e-mail (routes publiques).</summary>
public static class MessagingEndpoints
{
    public static IEndpointRouteBuilder MapMessagingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/public").WithTags("Public");

        group.MapGet("/whatsapp/webhook", (HttpRequest request, WhatsAppOptions options) =>
        {
            string mode = request.Query["hub.mode"].ToString();
            string token = request.Query["hub.verify_token"].ToString();
            string challenge = request.Query["hub.challenge"].ToString();

            if (mode != "subscribe" ||
                string.IsNullOrWhiteSpace(options.WebhookVerifyToken) ||
                !string.Equals(token, options.WebhookVerifyToken, StringComparison.Ordinal))
            {
                return Results.Unauthorized();
            }

            return Results.Text(challenge);
        });

        group.MapPost("/whatsapp/webhook", async (
            HttpRequest request,
            WhatsAppOptions options,
            PressingDbContext db,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(options.AppSecret))
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

            string signature = request.Headers["X-Hub-Signature-256"].ToString();

            if (!signature.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
                return Results.Unauthorized();

            using var memory = new MemoryStream();
            await request.Body.CopyToAsync(memory, ct);
            byte[] body = memory.ToArray();

            byte[] suppliedHash;
            try
            {
                suppliedHash = Convert.FromHexString(signature["sha256=".Length..]);
            }
            catch (FormatException)
            {
                return Results.Unauthorized();
            }

            byte[] expectedHash = HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(options.AppSecret), body);

            if (suppliedHash.Length != expectedHash.Length ||
                !CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash))
            {
                return Results.Unauthorized();
            }

            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;

            if (!root.TryGetProperty("entry", out JsonElement entries))
                return Results.Ok();

            foreach (JsonElement entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("changes", out JsonElement changes))
                    continue;

                foreach (JsonElement change in changes.EnumerateArray())
                {
                    if (!change.TryGetProperty("value", out JsonElement value) ||
                        !value.TryGetProperty("messages", out JsonElement messages))
                        continue;

                    foreach (JsonElement message in messages.EnumerateArray())
                    {
                        if (!message.TryGetProperty("from", out JsonElement fromElement) ||
                            !message.TryGetProperty("text", out JsonElement textElement) ||
                            !textElement.TryGetProperty("body", out JsonElement bodyElement))
                            continue;

                        string sender = new(fromElement.GetString()?.Where(char.IsDigit).ToArray() ?? []);
                        string content = bodyElement.GetString()?.Trim() ?? string.Empty;

                        if (content.Equals("STOP", StringComparison.OrdinalIgnoreCase) &&
                            sender.Length > 0)
                        {
                            var clients = await db.Clients
                                .Where(c => c.NotificationPreference == ClientNotificationPreference.WhatsApp)
                                .ToListAsync(ct);

                            var client = clients.FirstOrDefault(c =>
                                new string(c.WhatsApp.Where(char.IsDigit).ToArray()) == sender);

                            if (client is not null)
                            {
                                client.NotificationPreference = ClientNotificationPreference.None;
                                client.ConsentRevokedAt = DateTime.Now;
                                await db.SaveChangesAsync(ct);
                            }
                        }
                    }
                }
            }

            return Results.Ok();
        });
        // Désinscription des rappels e-mail : le lien des e-mails ouvre la page /desabonnement du site, qui appelle
        // ces deux routes. Le jeton (type « unsubscribe ») ne donne aucun autre droit (TokenRevalidation le refuse).
        group.MapGet("/unsubscribe", async (string? token, TokenService tokens, PressingDbContext db, CancellationToken ct) =>
        {
            Client? client = await FindUnsubscribeClientAsync(token, tokens, db, ct);
            return client is null
                ? Results.BadRequest(new { message = UnsubscribeInvalid })
                : Results.Ok(new UnsubscribeStatusDto(IsEmailReminderActive(client)));
        }).RequireRateLimiting("tracking");

        group.MapPost("/unsubscribe", async (UnsubscribeRequest request, TokenService tokens, PressingDbContext db, CancellationToken ct) =>
        {
            Client? client = await FindUnsubscribeClientAsync(request.Token, tokens, db, ct);
            if (client is null) return Results.BadRequest(new { message = UnsubscribeInvalid });

            if (IsEmailReminderActive(client))
            {
                client.NotificationPreference = ClientNotificationPreference.None;
                client.ConsentRevokedAt = DateTime.Now;
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(new MessageResponse("C'est fait : vous ne recevrez plus de rappels par e-mail."));
        }).RequireRateLimiting("tracking");

        return app;
    }

    private const string UnsubscribeInvalid = "Ce lien de désinscription est invalide ou a expiré.";

    private static bool IsEmailReminderActive(Client client) =>
        client.NotificationPreference == ClientNotificationPreference.Email &&
        client.ConsentGivenAt.HasValue &&
        !client.ConsentRevokedAt.HasValue;

    private static async Task<Client?> FindUnsubscribeClientAsync(string? token, TokenService tokens, PressingDbContext db, CancellationToken ct)
    {
        var principal = string.IsNullOrWhiteSpace(token) ? null : tokens.Validate(token);
        if (principal?.FindFirst("token_type")?.Value != "unsubscribe" ||
            !int.TryParse(principal.FindFirst("client_id")?.Value, out int clientId))
        {
            return null;
        }

        return await db.Clients.FirstOrDefaultAsync(c => c.Id == clientId, ct);
    }
}
