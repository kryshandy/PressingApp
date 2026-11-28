using System.Reflection;

namespace Pressing.Core.Assistant;

/// <summary>Ressources embarquées afin que le code compilé n'ait pas besoin du dossier tools/assistant.</summary>
public static class AssistantResources
{
    public static string SystemPrompt => Read("Pressing.Core.Assistant.prompt-systeme.txt");

    public static string Grammar => Read("Pressing.Core.Assistant.grammar.gbnf");

    public static string IntentSchema => Read("Pressing.Core.Assistant.intent-schema.json");

    public static string DefaultVocabulary => Read("Pressing.Core.Assistant.vocab-default.txt");

    private static string Read(string name)
    {
        using Stream? stream = typeof(AssistantResources).Assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            throw new InvalidOperationException($"Ressource IA introuvable : {name}");
        }
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
