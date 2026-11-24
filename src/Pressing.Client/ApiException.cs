using System.Net;
using System.Text.Json;

namespace Pressing.Client;

/// <summary>Erreur renvoyée par l'API ou par le réseau. <see cref="Exception.Message"/> est lisible par l'utilisateur.</summary>
public class ApiException : Exception
{
    public ApiException(HttpStatusCode statusCode, string message, string? detail = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        Detail = detail;
    }

    public HttpStatusCode StatusCode { get; }
    public string? Detail { get; }

    public bool IsUnauthorized => StatusCode == HttpStatusCode.Unauthorized;
    public bool IsForbidden => StatusCode == HttpStatusCode.Forbidden;
    public bool IsNotFound => StatusCode == HttpStatusCode.NotFound;

    /// <summary>Erreur réseau / délai dépassé (StatusCode = ServiceUnavailable).</summary>
    public static ApiException Unreachable(string baseUrl, Exception inner) =>
        new(HttpStatusCode.ServiceUnavailable,
            $"Impossible de joindre le serveur Pressing. Vérifiez que l'API est démarrée (adresse : {baseUrl}).",
            inner.Message, inner);

    /// <summary>Construit l'exception à partir d'une réponse d'erreur.</summary>
    public static ApiException FromResponse(HttpStatusCode status, string? body)
    {
        var text = ExtractMessage(body);
        var message = !string.IsNullOrWhiteSpace(text) ? text! : Generic(status);
        return new ApiException(status, message, string.IsNullOrWhiteSpace(text) ? body : text);
    }

    private static string Generic(HttpStatusCode status) => (int)status switch
    {
        401 => "Session expirée, reconnectez-vous.",
        403 => "Vous n'avez pas les droits nécessaires.",
        404 => "Élément introuvable.",
        429 => "Trop de tentatives, réessayez dans une minute.",
        >= 500 => "Le serveur a rencontré une erreur.",
        _ => "La requête a échoué (code " + (int)status + ")."
    };

    private static string? ExtractMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            foreach (var key in new[] { "detail", "message", "title" })
            {
                if (doc.RootElement.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(p.GetString()))
                    return p.GetString();
            }
        }
        catch (JsonException) { }
        return null;
    }
}
