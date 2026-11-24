using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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

    public ApiExceptionFilter(IModelMetadataProvider metadata, ILogger<ApiExceptionFilter> logger)
    {
        _metadata = metadata;
        _logger = logger;
    }

    public async Task OnExceptionAsync(ExceptionContext context)
    {
        if (context.Exception is not ApiException ex) return;
        var http = context.HttpContext;

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
            _logger.LogWarning("API injoignable : {Detail}", ex.Detail ?? ex.Message);
            context.Result = View(context, "Unavailable", null, StatusCodes.Status503ServiceUnavailable);
            context.ExceptionHandled = true;
            return;
        }

        _logger.LogWarning("Erreur API {Status} : {Message}", (int)ex.StatusCode, ex.Message);
        var status = ex.IsForbidden ? StatusCodes.Status403Forbidden : StatusCodes.Status500InternalServerError;
        context.Result = View(context, "Error", new ErrorViewModel(status, ex.Message), status);
        context.ExceptionHandled = true;
    }

    private ViewResult View(ExceptionContext context, string name, object? model, int status) => new()
    {
        ViewName = name,
        StatusCode = status,
        ViewData = new ViewDataDictionary(_metadata, context.ModelState) { Model = model }
    };
}
