using System.Diagnostics;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Pressing.Core;
using Pressing.Core.Assistant;
using Pressing.Core.Dtos;
using Pressing.Core.Security;
using Pressing.Core.Services;

namespace Pressing.Api.Endpoints;

/// <summary>
/// Routes de l'assistant vocal. <b>Aucune de ces routes ne crée de commande, de paiement ni de retrait</b> : elles
/// renvoient un brouillon que le caissier valide par les routes existantes (droits, audit et calculs déjà testés).
/// </summary>
public static class AssistantEndpoints
{
    public const string RateLimitPolicy = "assistant";

    private const string NotInstalled = "L'assistant vocal n'est pas installé.";
    private const string Disabled = "L'assistant vocal est désactivé.";
    private const string NotStarted = "L'assistant vocal n'est pas démarré.";
    private const int MaxAuditTextLength = 300;

    /// <summary>Services de l'assistant et limitation de débit (30 requêtes par minute et par utilisateur).</summary>
    public static IServiceCollection AddAssistant(this IServiceCollection services)
    {
        services.AddSingleton(AssistantOptions.FromEnvironment());
        services.AddScoped<AssistantService>();
        services.AddRateLimiter(options => options.AddPolicy(RateLimitPolicy, context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } id
                    ? "staff:" + id
                    : context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                })));
        return services;
    }

    public static IEndpointRouteBuilder MapAssistantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/assistant").WithTags("Assistant");

        // État des moteurs : répond même quand l'assistant est désactivé (le Desktop affiche pourquoi).
        group.MapGet("/status", async (HttpContext http, AssistantOptions options, CancellationToken ct) =>
        {
            ISpeechToText? stt = http.RequestServices.GetService<ISpeechToText>();
            IIntentExtractor? extractor = http.RequestServices.GetService<IIntentExtractor>();

            bool sttReady = options.Enabled && stt is not null && await ProbeAsync(stt.IsReadyAsync, ct);
            bool llmReady = options.Enabled && extractor is not null && await ProbeAsync(extractor.IsReadyAsync, ct);

            string? message = !options.Enabled ? Disabled
                : stt is null || extractor is null ? NotInstalled
                : !sttReady || !llmReady ? NotStarted
                : null;

            return Results.Ok(new AssistantStatusDto(options.Enabled, sttReady, llmReady, options.ConfidenceThreshold, message));
        }).RequireAuthorization("StaffOnly").RequireRateLimiting(RateLimitPolicy);

        // WAV (corps brut, 2 Mo maximum) -> texte. L'audio n'est jamais stocké : seul le texte va dans l'audit.
        group.MapPost("/transcribe", async (
            HttpContext http,
            AssistantOptions options,
            AuditService audit,
            CurrentUser current,
            CancellationToken ct) =>
        {
            if (!options.Enabled) return Unavailable(Disabled);
            ISpeechToText? stt = http.RequestServices.GetService<ISpeechToText>();
            if (stt is null) return Unavailable(NotInstalled);

            byte[] wav = await ReadWavAsync(http.Request, ct);

            var timer = Stopwatch.StartNew();
            string text;
            try
            {
                using var stream = new MemoryStream(wav);
                text = (await stt.TranscribeAsync(stream, ct) ?? string.Empty).Trim();
            }
            catch (AssistantUnavailableException ex)
            {
                return Unavailable(ex.Message);
            }

            timer.Stop();
            await audit.LogAsync(current, "assistant", "Assistant", null, "Transcription : " + Truncate(text), ct: ct);
            return Results.Ok(new AssistantTranscriptionDto(text, (int)timer.ElapsedMilliseconds));
        }).RequireAuthorization("StaffOnly").RequireRateLimiting(RateLimitPolicy);

        // Texte -> brouillon validé par les règles métier (client, catalogue, prix, ticket).
        group.MapPost("/interpret", async (
            HttpContext http,
            AssistantInterpretRequest request,
            AssistantOptions options,
            AssistantService assistant,
            CatalogService catalog,
            AuditService audit,
            CurrentUser current,
            CancellationToken ct) =>
        {
            if (!options.Enabled) return Unavailable(Disabled);
            IIntentExtractor? extractor = http.RequestServices.GetService<IIntentExtractor>();
            if (extractor is null) return Unavailable(NotInstalled);

            string text = (request.Text ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                throw new ArgumentException("Le texte à interpréter est vide.");
            }

            if (text.Length > AssistantContract.MaxTextLength)
            {
                throw new ArgumentException(
                    $"Le texte est trop long ({AssistantContract.MaxTextLength} caractères au maximum).");
            }

            AssistantIntentDto intent;
            try
            {
                CatalogHints hints = await BuildHintsAsync(catalog, ct);
                intent = await extractor.ExtractAsync(text, hints, ct);
            }
            catch (AssistantUnavailableException ex)
            {
                return Unavailable(ex.Message);
            }

            if (string.IsNullOrWhiteSpace(intent.Transcript))
            {
                intent = intent with { Transcript = text };
            }

            OrderDraftDto draft = await assistant.BuildDraftAsync(intent, options.ConfidenceThreshold, ct);
            await audit.LogAsync(
                current, "assistant", "Assistant", null,
                $"« {Truncate(text)} » → {draft.Intent} ({draft.Target})", ct: ct);
            return Results.Ok(draft);
        }).RequireAuthorization("StaffOnly").RequireRateLimiting(RateLimitPolicy);

        return app;
    }

    // ------------------------------------------------------------------ utilitaires

    private static IResult Unavailable(string message) =>
        Results.Json(new { message }, statusCode: StatusCodes.Status503ServiceUnavailable);

    private static async Task<bool> ProbeAsync(Func<CancellationToken, Task<bool>> probe, CancellationToken ct)
    {
        try
        {
            return await probe(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    private static async Task<CatalogHints> BuildHintsAsync(CatalogService catalog, CancellationToken ct)
    {
        IReadOnlyList<CategoryDto> categories = await catalog.GetAsync(includeInactive: false, ct);
        return new CatalogHints(
            categories.Select(c => c.Name).ToList(),
            Enum.GetValues<ServiceType>().Select(s => s.Label()).ToList());
    }

    /// <summary>Lit le corps brut sans jamais dépasser la limite (la taille déclarée n'est pas fiable).</summary>
    private static async Task<byte[]> ReadWavAsync(HttpRequest request, CancellationToken ct)
    {
        const string tooBig = "Enregistrement trop volumineux (2 Mo au maximum).";
        if (request.ContentLength > AssistantContract.MaxAudioBytes)
        {
            throw new ArgumentException(tooBig);
        }

        using var buffer = new MemoryStream();
        byte[] chunk = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > AssistantContract.MaxAudioBytes)
            {
                throw new ArgumentException(tooBig);
            }

            buffer.Write(chunk, 0, read);
        }

        byte[] wav = buffer.ToArray();
        if (wav.Length == 0)
        {
            throw new ArgumentException("Enregistrement vide.");
        }

        // En-tête RIFF....WAVE : refuse d'emblée tout autre type de fichier.
        bool isWav = wav.Length >= 12
                     && wav[0] == (byte)'R' && wav[1] == (byte)'I' && wav[2] == (byte)'F' && wav[3] == (byte)'F'
                     && wav[8] == (byte)'W' && wav[9] == (byte)'A' && wav[10] == (byte)'V' && wav[11] == (byte)'E';
        if (!isWav)
        {
            throw new ArgumentException("Un enregistrement au format WAV est attendu.");
        }

        return wav;
    }

    private static string Truncate(string text) =>
        text.Length <= MaxAuditTextLength ? text : text[..MaxAuditTextLength] + "…";
}
