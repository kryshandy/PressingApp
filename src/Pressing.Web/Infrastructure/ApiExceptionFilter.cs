using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Pressing.Client;
using Pressing.Web.Models;

namespace Pressing.Web.Infrastructure;

/// <summary>Traduit toute <see cref="ApiException"/> non gérée en page lisible (session expirée, service indisponible…).</summary>
public sealed class ApiExceptionFilter : IAsyncExceptionFilter
{
    private readonly IModelMetadataProvider _metadata;
    private readonly ILogger<ApiExceptionFilter> _logger;
    private readonly ITempDataDictionaryFactory _tempDataFactory;

    public ApiExceptionFilter(IModelMetadataProvider metadata, ILogger<ApiExceptionFilter> logger,
        ITempDataDictionaryFactory tempDataFactory)
    {
        _metadata = metadata;
        _logger = logger;
        _tempDataFactory = tempDataFactory;
    }

    public async Task OnExceptionAsync(ExceptionContext context)
    {
        if (context.Exception is not ApiException ex) return;
        var http = context.HttpContext;

        // La page d'erreur fournie par le filtre est déjà la bonne : éviter le ré-exécutage de /erreur/{code}.
        var statusPages = http.Features.Get<IStatusCodePagesFeature>();
        if (statusPages is not null) statusPages.Enabled = false;

        if (ex.IsUnauthorized)
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            var target = HttpMethods.IsGet(http.Request.Method)
                ? (http.Request.Path + http.Request.QueryString).ToString()
                : "/compte";
            context.Result = new RedirectResult(
                "/compte/connexion?returnUrl=" + Uri.EscapeDataString(target) + "&expiree=1");
            context.ExceptionHandled = true;
            return;
        }

        if (ex.IsNotFound)
        {
            context.Result = new NotFoundResult();
            context.ExceptionHandled = true;
            return;
        }

        if (ex.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            _logger.LogError(ex, "API injoignable sur {Method} {Path} : {Detail}",
                http.Request.Method, http.Request.Path, ex.Detail ?? ex.Message);

            if (!HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method))
            {
                // PRG : on n'affiche jamais la page « Réessayer » sur un POST, sinon « recharger » rejoue l'envoi.
                var tempData = _tempDataFactory.GetTempData(http);
                var saved = RetryForm.Save(tempData, http);
                tempData["Error"] = RetryForm.UnavailableMessage(saved);
                context.Result = new RedirectResult(RetryForm.SafeGetPath(http));
                context.ExceptionHandled = true;
                return;
            }

            var unavailable = View(context, "Unavailable", null, StatusCodes.Status503ServiceUnavailable);
            unavailable.ViewData["RetryUrl"] = RetryForm.SafeGetPath(http);
            context.Result = unavailable;
            context.ExceptionHandled = true;
            return;
        }

        _logger.LogWarning(ex, "Erreur API {Status} sur {Method} {Path} : {Message} | Détail : {Detail}",
            (int)ex.StatusCode, http.Request.Method, http.Request.Path, ex.Message, ex.Detail);

        int status = (int)ex.StatusCode is >= 400 and < 500 ? (int)ex.StatusCode : StatusCodes.Status500InternalServerError;
        context.Result = View(context, "Error", new ErrorViewModel(status, PublicMessage(ex)), status);
        context.ExceptionHandled = true;
    }

    /// <summary>
    /// Message grand public. Les erreurs 4xx gardent le texte lisible renvoyé par l'API ; les erreurs 5xx
    /// sont remplacées (chaque situation a son message), les détails restant uniquement dans les journaux.
    /// </summary>
    private static string PublicMessage(ApiException ex)
    {
        int status = (int)ex.StatusCode;
        if (status >= 500)
        {
            return status == 503
                ? "Le service est momentanément indisponible. Réessayez dans un instant."
                : "Un incident imprévu s'est produit de notre côté. Réessayez dans un instant.";
        }

        if (!string.IsNullOrWhiteSpace(ex.Message)) return ex.Message;
        return status switch
        {
            400 => "Votre demande n'a pas pu être comprise. Vérifiez les informations saisies.",
            403 => "Vous n'avez pas accès à cette page.",
            404 => "L'élément demandé est introuvable.",
            409 => "Cette modification entre en conflit avec des données déjà enregistrées. Réessayez.",
            422 => "Les informations envoyées ne sont pas valides.",
            429 => "Trop de tentatives. Réessayez dans une minute.",
            _ => "Votre demande n'a pas pu être traitée. Réessayez dans un instant."
        };
    }

    private ViewResult View(ExceptionContext context, string name, object? model, int status) => new()
    {
        ViewName = name,
        StatusCode = status,
        ViewData = new ViewDataDictionary(_metadata, context.ModelState) { Model = model }
    };
}
