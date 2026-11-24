using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Pressing.Api.Infrastructure;

public sealed class ApiExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionMiddleware> _logger;

    public ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            if (context.Response.HasStarted)
            {
                throw;
            }

            (int status, string title) = exception switch
            {
                BadHttpRequestException => (StatusCodes.Status400BadRequest, "Requête invalide"),
                KeyNotFoundException => (StatusCodes.Status404NotFound, "Élément introuvable"),
                UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Authentification requise"),
                InvalidOperationException => (StatusCodes.Status400BadRequest, "Opération impossible"),
                ArgumentException => (StatusCodes.Status400BadRequest, "Données invalides"),
                DbUpdateException => (StatusCodes.Status409Conflict, "Conflit de données"),
                _ => (StatusCodes.Status500InternalServerError, "Erreur interne")
            };

            if (status >= 500)
            {
                _logger.LogError(exception, "Erreur non gérée sur {Method} {Path}", context.Request.Method, context.Request.Path);
            }
            else
            {
                _logger.LogWarning(exception, "Requête refusée sur {Method} {Path}", context.Request.Method, context.Request.Path);
            }

            context.Response.Clear();
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/problem+json";

            var problem = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = status == 500
                    ? "Une erreur interne est survenue. Consultez les journaux du serveur."
                    : exception is BadHttpRequestException
                        ? "Corps de requête ou paramètres invalides (JSON mal formé ou valeur d'énumération inconnue)."
                        : exception.Message,
                Instance = context.Request.Path
            };

            await context.Response.WriteAsJsonAsync(problem);
        }
    }
}
