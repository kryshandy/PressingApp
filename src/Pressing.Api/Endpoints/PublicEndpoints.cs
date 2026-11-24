using System.Net;
using System.Reflection;
using Pressing.Core.Dtos;
using Pressing.Core.Email;
using Pressing.Core.Services;

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
        }).WithTags("Santé");

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
                    "Suivi de commande en temps réel",
                    "Tickets et reçus numériques",
                    "Paiement au dépôt ou au retrait",
                    "Service express",
                    "Programme de fidélité",
                    "Espace client sécurisé"
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
                return Results.BadRequest(new { message = "Le numéro de téléphone est requis pour consulter une commande." });
            }

            TrackingDto? tracking = await orders.TrackAsync(ticket, phone, ct);
            return tracking is null
                ? Results.NotFound(new { message = "Aucune commande ne correspond à ce ticket et ce téléphone." })
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
                <p><strong>Téléphone :</strong> {WebUtility.HtmlEncode(request.Phone ?? "non renseigné")}</p>
                <p style="white-space:pre-wrap">{WebUtility.HtmlEncode(request.Message)}</p>
                """;

            await email.QueueRawAsync(
                shop.Email,
                shop.Name,
                $"Contact site — {request.Name}",
                html,
                request.Message,
                ct: ct);

            return Results.Ok(new { message = "Votre message a bien été transmis. Nous vous répondrons rapidement." });
        }).RequireRateLimiting("tracking");

        return app;
    }
}
