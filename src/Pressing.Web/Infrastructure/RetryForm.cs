using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Pressing.Web.Infrastructure;

/// <summary>
/// Conserve les valeurs d'un formulaire après une erreur service (PRG) pour éviter qu'un « Réessayer »
/// ne rejoue un POST. Les champs sensibles (mot de passe, jeton, anti-falsification) ne sont jamais conservés.
/// </summary>
public static class RetryForm
{
    public const string Key = "Pressing.RetryForm";

    private const int MaxValueLength = 2000;
    private const int MaxTotalLength = 3000; // un message de contact complet (2000) + les autres champs ; le cookie TempData est découpé au besoin.
    private static readonly string[] SensitiveParts = ["password", "token", "antiforgery", "verification"];

    /// <summary>Ce que <see cref="Save"/> a pu conserver, pour annoncer au visiteur la vérité.</summary>
    public readonly record struct SaveResult(int Kept, bool SensitiveSkipped, bool TooLongSkipped);

    public static SaveResult Save(ITempDataDictionary tempData, HttpContext http)
    {
        if (!http.Request.HasFormContentType) return default;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int total = 0;
        bool sensitive = false, tooLong = false;
        foreach (var field in http.Request.Form)
        {
            string name = field.Key ?? string.Empty;
            if (name.Length == 0) continue;
            if (SensitiveParts.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase)))
            {
                // Seul un mot de passe saisi est une perte pour le visiteur : les jetons sont régénérés
                // par la page ou repassés dans l'URL (voir SafeGetPath).
                sensitive |= name.Contains("password", StringComparison.OrdinalIgnoreCase) &&
                             !string.IsNullOrEmpty(field.Value.ToString());
                continue;
            }

            string value = field.Value.ToString();
            if (value.Length > MaxValueLength || total + value.Length > MaxTotalLength)
            {
                tooLong = true;
                continue;
            }
            values[name] = value;
            total += value.Length;
        }

        if (values.Count > 0) tempData[Key] = values;
        return new SaveResult(values.Count, sensitive, tooLong);
    }

    /// <summary>Message affiché après la redirection : il n'annonce que ce qui a réellement été conservé.</summary>
    public static string UnavailableMessage(SaveResult saved)
    {
        const string Down = "Le service est momentanément indisponible.";
        if (saved.TooLongSkipped)
            return Down + " Une partie de votre saisie, trop longue, n'a pas pu être conservée : vérifiez le formulaire avant de réessayer.";
        if (saved.Kept > 0 && saved.SensitiveSkipped)
            return Down + " Vos informations sont conservées, sauf le mot de passe à ressaisir : réessayez dans un instant.";
        if (saved.Kept > 0)
            return Down + " Vos informations sont conservées : réessayez dans un instant.";
        if (saved.SensitiveSkipped)
            return Down + " Par sécurité, les mots de passe ne sont pas conservés : ressaisissez-les dans un instant.";
        return Down + " Réessayez dans un instant.";
    }

    public static T Restore<T>(ITempDataDictionary tempData, T model)
    {
        object? saved = tempData[Key];
        if (saved is null) return model;
        tempData.Remove(Key);

        Dictionary<string, string>? values = saved switch
        {
            Dictionary<string, string> direct => direct,
            JsonElement json when json.ValueKind == JsonValueKind.Object => json.EnumerateObject()
                .Where(p => p.Value.ValueKind == JsonValueKind.String)
                .ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            _ => null
        };
        if (values is null) return model;

        ApplyValues(model!, values, new HashSet<Type> { typeof(T) });
        return model;
    }

    /// <summary>
    /// Applique les valeurs aux propriétés de même nom. Les sous-modèles (ex. ContactViewModel.Form) sont
    /// parcourus ; les objets d'un autre assembly (ex. ShopInfo) sont laissés intacts.
    /// </summary>
    private static void ApplyValues(object model, Dictionary<string, string> values, HashSet<Type> visited)
    {
        foreach (PropertyInfo property in model.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanWrite || property.GetIndexParameters().Length > 0) continue;

            if (property.PropertyType == typeof(string))
            {
                if (values.TryGetValue(property.Name, out string? value)) property.SetValue(model, value);
                continue;
            }

            Type childType = property.PropertyType;
            if (!childType.IsClass || childType.Assembly != model.GetType().Assembly ||
                childType.GetConstructor(Type.EmptyTypes) is null || !visited.Add(childType)) continue;

            object? child = property.GetValue(model) ?? Activator.CreateInstance(childType);
            if (child is null) continue;
            ApplyValues(child, values, visited);
            property.SetValue(model, child);
        }
    }

    /// <summary>
    /// URL GET sûre pour le bouton « Réessayer » ; un POST connu revient à sa page formulaire, avec les paramètres
    /// d'URL dont cette page a besoin (jeton de réinitialisation, adresse de retour après connexion).
    /// </summary>
    public static string SafeGetPath(HttpContext http)
    {
        if (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method))
        {
            string current = (http.Request.Path + http.Request.QueryString).ToString();
            return IsLocalPath(current) ? current : "/";
        }

        string path = (http.Request.Path.Value ?? "/").TrimEnd('/').ToLowerInvariant();
        switch (path)
        {
            case "/compte/reinitialiser":
            {
                string? token = FormValue(http, "Token");
                return string.IsNullOrWhiteSpace(token) ? path : path + "?token=" + Uri.EscapeDataString(token);
            }
            case "/compte/connexion":
            {
                string? returnUrl = FormValue(http, "ReturnUrl");
                return returnUrl is not null && IsLocalPath(returnUrl)
                    ? path + "?returnUrl=" + Uri.EscapeDataString(returnUrl)
                    : path;
            }
            case "/contact" or "/compte/mot-de-passe-oublie" or "/compte/profil" or "/compte/mot-de-passe":
                return path;
            default:
                return "/";
        }
    }

    private static string? FormValue(HttpContext http, string name) =>
        http.Request.HasFormContentType && http.Request.Form.TryGetValue(name, out var v) ? v.ToString() : null;

    /// <summary>Chemin interne au site : refuse « //autre-site » et « /utre-site » (redirection ouverte).</summary>
    private static bool IsLocalPath(string url) =>
        url.Length > 0 && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));
}
