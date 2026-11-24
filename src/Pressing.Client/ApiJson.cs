using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pressing.Client;

/// <summary>Options JSON partagées avec l'API (camelCase, énumérations en texte).</summary>
public static class ApiJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
