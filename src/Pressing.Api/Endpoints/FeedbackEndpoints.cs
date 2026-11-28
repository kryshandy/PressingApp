using Pressing.Core.Dtos;
using Pressing.Core.Services;

namespace Pressing.Api.Endpoints;

/// <summary>
/// Enquête de satisfaction : routes publiques protégées par le jeton signé du lien (type « feedback », 14 jours,
/// refusé par toute autre route) et consultation des avis par le responsable ou l'administrateur.
/// </summary>
public static class FeedbackEndpoints
{
    public static IEndpointRouteBuilder MapFeedbackEndpoints(this IEndpointRouteBuilder app)
    {
        var publicGroup = app.MapGroup("/api/public/feedback").WithTags("Public");

        publicGroup.MapGet("/", async (string? token, FeedbackService service, CancellationToken ct) =>
            Results.Ok(await service.GetFormAsync(token, ct))).RequireRateLimiting("tracking");

        publicGroup.MapPost("/", async (SubmitFeedbackRequest request, FeedbackService service, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await service.SubmitAsync(request.Token, request.Note, request.Comment, ct));
            }
            catch (FeedbackAlreadyAnsweredException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status409Conflict);
            }
        }).RequireRateLimiting("tracking");

        app.MapGet("/api/feedback", async (
            FeedbackService service,
            int? page,
            int? pageSize,
            int? days,
            CancellationToken ct) =>
            Results.Ok(await service.GetPageAsync(page ?? 1, pageSize ?? 25, days, ct)))
            .WithTags("Satisfaction")
            .RequireAuthorization("ManagerOrAdmin");

        return app;
    }
}
