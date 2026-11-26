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
    private const int MaxTotalLength = 1800; // reste largement sous la limite du cookie TempData après encodage.
    private static readonly string[] SensitiveParts = ["password", "token", "antiforgery", "verification"];

    public static void Save(ITempDataDictionary tempData, HttpContext http)
    {
        if (!http.Request.HasFormContentType) return;

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int total = 0;
        foreach (var field in http.Request.Form)
        {
            string name = field.Key ?? string.Empty;
            if (name.Length == 0 || SensitiveParts.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase))) continue;

            string value = field.Value.ToString();
            if (value.Length > MaxValueLength || total + value.Length > MaxTotalLength) continue;
            values[name] = value;
            total += value.Length;
        }

        if (values.Count > 0) tempData[Key] = values;
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

    /// <summary>URL GET sûre pour le bouton « Réessayer » ; un POST connu revient à sa page formulaire.</summary>
    public static string SafeGetPath(HttpContext http)
    {
        if (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method))
        {
            string current = (http.Request.Path + http.Request.QueryString).ToString();
            return string.IsNullOrWhiteSpace(current) ? "/" : current;
        }

        string path = (http.Request.Path.Value ?? "/").TrimEnd('/').ToLowerInvariant();
        return path switch
        {
            "/contact" or "/compte/connexion" or "/compte/mot-de-passe-oublie" or
            "/compte/reinitialiser" or "/compte/profil" or "/compte/mot-de-passe" => path,
            _ => "/"
        };
    }
}
