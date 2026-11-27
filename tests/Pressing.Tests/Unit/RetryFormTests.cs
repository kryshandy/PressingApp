extern alias WebApp;

using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Pressing.Client;
using ApiExceptionFilter = WebApp::Pressing.Web.Infrastructure.ApiExceptionFilter;
using RetryForm = WebApp::Pressing.Web.Infrastructure.RetryForm;

namespace Pressing.Tests.Unit;

/// <summary>
/// Site : un formulaire envoyé pendant une panne de l'API (503) revient sur sa page sans perdre ce dont elle
/// a besoin (jeton de réinitialisation, adresse de retour) et le message n'annonce que ce qui est conservé.
/// </summary>
public class RetryFormTests
{
    private const string Kept = "Vos informations sont conservées : réessayez";

    [Fact]
    public async Task Reinitialisation_garde_le_jeton_dans_l_url_apres_une_panne()
    {
        var (result, error) = await PostDuringOutage("/compte/reinitialiser", new()
        {
            ["Token"] = "abc+/=123",
            ["NewPassword"] = "Nouveau@2026",
            ["ConfirmPassword"] = "Nouveau@2026",
            ["__RequestVerificationToken"] = "xyz"
        });

        Assert.Equal("/compte/reinitialiser?token=abc%2B%2F%3D123", result.Url);
        Assert.DoesNotContain(Kept, error);
        Assert.Contains("mots de passe ne sont pas conservés", error);
    }

    [Fact]
    public async Task Reinitialisation_sans_jeton_revient_simplement_sur_la_page()
    {
        var (result, _) = await PostDuringOutage("/compte/reinitialiser", new() { ["NewPassword"] = "x" });
        Assert.Equal("/compte/reinitialiser", result.Url);
    }

    [Fact]
    public async Task Connexion_garde_l_adresse_de_retour_interne()
    {
        var (result, error) = await PostDuringOutage("/compte/connexion", new()
        {
            ["Identifier"] = "699112233",
            ["Password"] = "Client@2026!",
            ["ReturnUrl"] = "/compte/commandes/3"
        });

        Assert.Equal("/compte/connexion?returnUrl=%2Fcompte%2Fcommandes%2F3", result.Url);
        Assert.Contains("sauf le mot de passe", error);
    }

    [Theory]
    [InlineData("//autre-site.example")]
    [InlineData("/\\autre-site.example")]
    [InlineData("https://autre-site.example")]
    public async Task Connexion_ignore_une_adresse_de_retour_externe(string returnUrl)
    {
        var (result, _) = await PostDuringOutage("/compte/connexion", new()
        {
            ["Identifier"] = "699112233",
            ["ReturnUrl"] = returnUrl
        });
        Assert.Equal("/compte/connexion", result.Url);
    }

    [Fact]
    public async Task Contact_conserve_un_message_de_2000_caracteres()
    {
        var tempData = NewTempData();
        var (result, error) = await PostDuringOutage("/contact", new()
        {
            ["Name"] = "Marie Ngo",
            ["Email"] = "marie@example.com",
            ["Phone"] = "699112233",
            ["Subject"] = "Question",
            ["Message"] = new string('a', 2000)
        }, tempData);

        Assert.Equal("/contact", result.Url);
        Assert.Contains(Kept, error);
        var saved = Assert.IsType<Dictionary<string, string>>(tempData[RetryForm.Key]);
        Assert.Equal(2000, saved["Message"].Length);
    }

    [Fact]
    public async Task Changement_de_mot_de_passe_n_annonce_pas_de_conservation()
    {
        var (_, error) = await PostDuringOutage("/compte/mot-de-passe", new()
        {
            ["CurrentPassword"] = "Ancien@2026",
            ["NewPassword"] = "Nouveau@2026",
            ["ConfirmPassword"] = "Nouveau@2026"
        });

        Assert.DoesNotContain(Kept, error);
        Assert.Contains("mots de passe ne sont pas conservés", error);
    }

    [Fact]
    public void Saisie_trop_longue_est_signalee()
    {
        var http = FormContext("/contact", new() { ["Message"] = new string('a', 2001) });
        var saved = RetryForm.Save(NewTempData(), http);

        Assert.True(saved.TooLongSkipped);
        Assert.Contains("n'a pas pu être conservée", RetryForm.UnavailableMessage(saved));
    }

    [Theory]
    [InlineData("//autre-site.example", "/")]
    [InlineData("/suivi", "/suivi")]
    public void Bouton_reessayer_d_une_page_get_reste_sur_le_site(string path, string expected)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "GET";
        http.Request.Path = path;
        Assert.Equal(expected, RetryForm.SafeGetPath(http));
    }

    // ------------------------------------------------------------------ outils

    private static async Task<(RedirectResult Result, string Error)> PostDuringOutage(
        string path, Dictionary<string, StringValues> form, ITempDataDictionary? tempData = null)
    {
        tempData ??= NewTempData();
        var http = FormContext(path, form);
        var context = new ExceptionContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>())
        {
            Exception = new ApiException(HttpStatusCode.ServiceUnavailable, "API injoignable")
        };

        var filter = new ApiExceptionFilter(new EmptyModelMetadataProvider(), NullLogger<ApiExceptionFilter>.Instance,
            new FixedTempDataFactory(tempData));
        await filter.OnExceptionAsync(context);

        Assert.True(context.ExceptionHandled);
        var redirect = Assert.IsType<RedirectResult>(context.Result);
        return (redirect, Assert.IsType<string>(tempData["Error"]));
    }

    private static DefaultHttpContext FormContext(string path, Dictionary<string, StringValues> form)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "POST";
        http.Request.Path = path;
        http.Request.ContentType = "application/x-www-form-urlencoded";
        http.Request.Form = new FormCollection(form);
        return http;
    }

    private static TempDataDictionary NewTempData() => new(new DefaultHttpContext(), new MemoryTempDataProvider());

    private sealed class MemoryTempDataProvider : ITempDataProvider
    {
        private IDictionary<string, object> _values = new Dictionary<string, object>();
        public IDictionary<string, object> LoadTempData(HttpContext context) => _values;
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) => _values = values;
    }

    private sealed class FixedTempDataFactory(ITempDataDictionary tempData) : ITempDataDictionaryFactory
    {
        public ITempDataDictionary GetTempData(HttpContext context) => tempData;
    }
}



