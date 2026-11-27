using Microsoft.EntityFrameworkCore;
using System.Text;
using Pressing.Core.Messaging;
using Pressing.Core.Entities;
using Pressing.Core.Data;
using System.Text.Json;
using System.Security.Cryptography;
using System.Net;
using System.Reflection;
using Pressing.Core.Dtos;
using Pressing.Core.Email;
using Pressing.Core.Services;
using Pressing.Core.Security;

namespace Pressing.Api.Endpoints;

public static class PublicEndpoints
{
    private sealed record ContactRequest(string Name, string Email, string? Phone, string Message);

    public static IEndpointRouteBuilder MapPublicEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", async (
            SettingsService settings,
            EmailService email,
            CancellationToken ct) =>
        {
            var shop = await settings.GetShopInfoAsync(ct);
            var emailStatus = email.GetConfigStatus();
            return Results.Ok(new HealthInfoDto(
                "ok",
                Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "2.0.0",
                shop.Name,
                DateTime.Now,
                emailStatus.Provider != "simulated",
                emailStatus.Provider));
        }).WithTags("SantÃ©");

        var group = app.MapGroup("/api/public").WithTags("Public");

        group.MapGet("/info", async (
            SettingsService settings,
            CatalogService catalog,
            PromotionService promotions,
            CancellationToken ct) =>
        {
            var shop = await settings.GetShopInfoAsync(ct);
            var categories = await catalog.GetAsync(false, ct);
            var activePromotions = (await promotions.GetAsync(false, ct))
                .Where(p => p.ValidFrom <= DateTime.Now && p.ValidTo >= DateTime.Now)
                .Take(3)
                .ToList();

            return Results.Ok(new
            {
                shop,
                categories,
                promotions = activePromotions,
                features = new[]
                {
                    "Suivi de commande en temps rÃ©el",
                    "Tickets et reÃ§us numÃ©riques",
                    "Paiement au dÃ©pÃ´t ou au retrait",
                    "Service express",
                    "Programme de fidÃ©litÃ©",
                    "Espace client sÃ©curisÃ©"
                }
            });
        });

        group.MapGet("/catalog", async (CatalogService catalog, CancellationToken ct) =>
            Results.Ok(await catalog.GetAsync(false, ct)));

        group.MapGet("/tracking/{ticket}", async (
            string ticket,
            string? phone,
            OrderService orders,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(phone))
            {
                return Results.BadRequest(new { message = "Le numÃ©ro de tÃ©lÃ©phone est requis pour consulter une commande." });
            }

            TrackingDto? tracking = await orders.TrackAsync(ticket, phone, ct);
            return tracking is null
                ? Results.NotFound(new { message = "Aucune commande ne correspond Ã  ce ticket et ce tÃ©lÃ©phone." })
                : Results.Ok(tracking);
        }).RequireRateLimiting("tracking");

        group.MapPost("/contact", async (
            ContactRequest request,
            SettingsService settings,
            EmailService email,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name) ||
                string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.Message))
            {
                return Results.BadRequest(new { message = "Nom, e-mail et message sont obligatoires." });
            }

            if (!request.Email.Contains('@'))
            {
                return Results.BadRequest(new { message = "L'adresse e-mail n'est pas valide." });
            }

            var shop = await settings.GetShopInfoAsync(ct);
            string html = $"""
                <h2>Nouveau message depuis le site</h2>
                <p><strong>Nom :</strong> {WebUtility.HtmlEncode(request.Name)}</p>
                <p><strong>E-mail :</strong> {WebUtility.HtmlEncode(request.Email)}</p>
                <p><strong>TÃ©lÃ©phone :</strong> {WebUtility.HtmlEncode(request.Phone ?? "non renseignÃ©")}</p>
                <p style="white-space:pre-wrap">{WebUtility.HtmlEncode(request.Message)}</p>
                """;

            await email.QueueRawAsync(
                shop.Email,
                shop.Name,
                $"Contact site â€” {request.Name}",
                html,
                request.Message,
                ct: ct);

            return Results.Ok(new { message = "Votre message a bien Ã©tÃ© transmis. Nous vous rÃ©pondrons rapidement." });
        }).RequireRateLimiting("tracking");

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
        group.MapGet("/unsubscribe", async (
            string? token,
            TokenService tokens,
            PressingDbContext db,
            CancellationToken ct) =>
        {
            var principal = string.IsNullOrWhiteSpace(token)
                ? null
                : tokens.Validate(token);

            if (principal?.FindFirst("token_type")?.Value != "unsubscribe" ||
                !int.TryParse(principal.FindFirst("client_id")?.Value, out int clientId))
                return Results.Unauthorized();

            var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == clientId, ct);

            if (client is null)
                return Results.NotFound();

            if (client.NotificationPreference != ClientNotificationPreference.Email ||
                !client.ConsentGivenAt.HasValue ||
                client.ConsentRevokedAt.HasValue)
                return Results.Content(
                    "<h2>Vos rappels e-mail sont dÃ©jÃ  dÃ©sactivÃ©s.</h2>",
                    "text/html; charset=utf-8");

            string safeToken = WebUtility.HtmlEncode(token ?? string.Empty);

            string html = $"""
                <!doctype html>
                <html lang="fr">
                <meta charset="utf-8">
                <title>DÃ©sabonnement</title>
                <body style="font-family:Arial,sans-serif;max-width:520px;margin:60px auto;padding:20px">
                <h2>Confirmer votre dÃ©sabonnement</h2>
                <p>Souhaitez-vous ne plus recevoir de rappels par e-mail de Pressing Ã‰clat ?</p>
                <form method="post" action="/api/public/unsubscribe/confirm">
                <input type="hidden" name="token" value="{safeToken}">
                <button type="submit">Confirmer mon dÃ©sabonnement</button>
                </form>
                </body>
                </html>
                """;

            return Results.Content(html, "text/html; charset=utf-8");
        });

        group.MapPost("/unsubscribe/confirm", async (
            HttpRequest request,
            TokenService tokens,
            PressingDbContext db,
            CancellationToken ct) =>
        {
            var form = await request.ReadFormAsync(ct);
            string token = form["token"].ToString();

            var principal = string.IsNullOrWhiteSpace(token)
                ? null
                : tokens.Validate(token);

            if (principal?.FindFirst("token_type")?.Value != "unsubscribe" ||
                !int.TryParse(principal.FindFirst("client_id")?.Value, out int clientId))
                return Results.Unauthorized();

            var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == clientId, ct);

            if (client is null)
                return Results.NotFound();

            if (client.NotificationPreference == ClientNotificationPreference.Email &&
                client.ConsentGivenAt.HasValue &&
                !client.ConsentRevokedAt.HasValue)
            {
                client.NotificationPreference = ClientNotificationPreference.None;
                client.ConsentRevokedAt = DateTime.Now;
                await db.SaveChangesAsync(ct);
            }

            return Results.Content(
                "<h2>DÃ©sabonnement confirmÃ©</h2><p>Vous ne recevrez plus de rappels par e-mail.</p>",
                "text/html; charset=utf-8");
        });
        return app;
    }
}
