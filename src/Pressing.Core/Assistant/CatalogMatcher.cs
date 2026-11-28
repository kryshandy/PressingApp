using System.Text.RegularExpressions;

namespace Pressing.Core.Assistant;

/// <summary>Résultat d'une résolution catalogue : valeur retenue et raison d'un éventuel rejet.</summary>
public sealed record CatalogMatch(string? Value, string? Reason);

/// <summary>Prestation explicitement prononcée et position de son indice dans la transcription.</summary>
public sealed record ServiceMention(string Label, int Index, int Length);

/// <summary>
/// Rapproche une valeur proposée par le modèle du catalogue dynamique fourni par l'appelant.
/// Aucune valeur n'est inventée : une correspondance ambiguë ou non prouvée par la transcription
/// est remplacée par null et accompagnée d'un avertissement.
/// </summary>
public static class CatalogMatcher
{
    private static readonly (string Phrase, string Label)[] ServicePatterns =
    [
        ("lavage et repassage", "Lavage + repassage"),
        ("lavage repassage", "Lavage + repassage"),
        ("nettoyage a sec", "Nettoyage à sec"),
        ("detachage", "Détachage"),
        ("repassage", "Repassage"),
        ("repasser", "Repassage"),
        ("reparse", "Repassage"),
        ("repasse", "Repassage"),
        ("a laver", "Lavage"),
        ("laver", "Lavage"),
        ("lavage", "Lavage")
    ];

    /// <summary>Résout une catégorie et impose le libellé exact du catalogue.</summary>
    public static CatalogMatch ResolveCategory(CatalogHints catalog, string? value, string transcript) =>
        Resolve(catalog.Categories, value, transcript, service: false);

    /// <summary>Résout une prestation et impose le libellé exact du catalogue.</summary>
    public static CatalogMatch ResolveService(CatalogHints catalog, string? value, string transcript) =>
        Resolve(catalog.Services, value, transcript, service: true);

    /// <summary>
    /// Résout la prestation d'un article en utilisant l'indice de service le plus proche
    /// de sa catégorie. Une phrase peut mentionner plusieurs prestations
    /// (« chemises à laver, pantalons à repasser ») : appliquer le premier service
    /// prononcé à tous les articles inventerait une prestation pour les suivants.
    /// </summary>
    public static CatalogMatch ResolveServiceForItem(
        CatalogHints catalog,
        string? value,
        string transcript,
        string? category)
    {
        string text = CatalogHints.NormalizeWords(transcript);
        IReadOnlyList<ServiceMention> mentions = FindServiceMentions(text);
        if (mentions.Count > 0)
        {
            int? categoryIndex = FindCategoryIndex(catalog.Categories, text, category);
            ServiceMention? chosen = null;
            if (categoryIndex is int index)
            {
                chosen = mentions
                    .OrderBy(m => Math.Abs(m.Index - index))
                    .ThenBy(m => m.Index)
                    .FirstOrDefault();
            }
            else
            {
                string? proposed = CanonicalServiceLabel(catalog.Services, value);
                chosen = proposed is not null
                    ? mentions.FirstOrDefault(m => string.Equals(m.Label, proposed, StringComparison.Ordinal))
                    : (mentions.Count == 1 ? mentions[0] : null);
            }

            if (chosen is not null)
            {
                string canonical = CanonicalLabel(catalog.Services, chosen.Label);
                return string.Equals(value, canonical, StringComparison.Ordinal)
                    ? new CatalogMatch(canonical, null)
                    : new CatalogMatch(canonical, $"Prestation normalisée depuis la transcription : {canonical}");
            }

            if (mentions.Count > 1)
            {
                return new CatalogMatch(null, "Plusieurs prestations prononcées, aucun choix arbitraire sans catégorie prouvée.");
            }
        }

        return Resolve(catalog.Services, value, transcript, service: true);
    }

    /// <summary>Indices de toutes les prestations explicitement prononcées, dans l'ordre du texte.</summary>
    public static IReadOnlyList<ServiceMention> FindServiceMentions(string transcript)
    {
        string text = CatalogHints.NormalizeWords(transcript);
        var occupied = new bool[text.Length];
        var found = new List<ServiceMention>();
        foreach ((string phrase, string label) in ServicePatterns.OrderByDescending(p => p.Phrase.Length))
        {
            int start = 0;
            while ((start = text.IndexOf(phrase, start, StringComparison.Ordinal)) >= 0)
            {
                int end = start + phrase.Length;
                bool free = true;
                for (int i = start; i < end; i++)
                {
                    if (occupied[i]) { free = false; break; }
                }

                if (free)
                {
                    for (int i = start; i < end; i++) occupied[i] = true;
                    found.Add(new ServiceMention(label, start, phrase.Length));
                }

                start = end;
            }
        }

        return found.OrderBy(m => m.Index).ToArray();
    }

    /// <summary>Prestation explicitement prononcée, déduite de règles déterministes documentées.</summary>
    public static string? ExplicitService(string transcript)
    {
        string text = CatalogHints.NormalizeWords(transcript);
        if (Contains(text, "lavage et repassage") || Contains(text, "lavage repassage"))
        {
            return "Lavage + repassage";
        }

        if (Contains(text, "nettoyage a sec"))
        {
            return "Nettoyage à sec";
        }

        if (Contains(text, "detachage"))
        {
            return "Détachage";
        }

        if (Contains(text, "repassage") || Contains(text, "repasser") || Contains(text, "reparse"))
        {
            return "Repassage";
        }

        if (Contains(text, "lavage") || Contains(text, "laver"))
        {
            return "Lavage";
        }

        return null;
    }

    /// <summary>Terme réellement prononcé pour une prestation (« laver » pour « Lavage »).</summary>
    public static string? SpokenService(string transcript, string? canonicalLabel)
    {
        string text = CatalogHints.NormalizeWords(transcript);
        foreach ((string phrase, string label) in ServicePatterns.OrderByDescending(p => p.Phrase.Length))
        {
            if (Contains(text, phrase) &&
                (canonicalLabel is null || string.Equals(label, canonicalLabel, StringComparison.Ordinal)))
            {
                return phrase;
            }
        }

        return null;
    }

    /// <summary>Libellés de catalogue explicitement mentionnés dans la transcription.</summary>
    public static IReadOnlyList<string> MentionedCategories(CatalogHints catalog, string transcript) =>
        Mentioned(catalog.Categories, transcript);

    /// <summary>Terme réellement prononcé pour une catégorie, même si l'alias est ambigu.</summary>
    public static string? SpokenAlias(CatalogHints catalog, string? value, string transcript) =>
        SpokenTerm(catalog.Categories, value, transcript);

    private static string? SpokenTerm(
        IReadOnlyList<CatalogTerm> terms,
        string? value,
        string transcript)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string needleValue = CatalogHints.Normalize(value);
        CatalogTerm? term = terms.FirstOrDefault(t =>
            string.Equals(CatalogHints.Normalize(t.Label), needleValue, StringComparison.Ordinal) ||
            t.Aliases.Any(alias => string.Equals(CatalogHints.Normalize(alias), needleValue, StringComparison.Ordinal)));
        if (term is null) return null;

        string rawText = CatalogHints.NormalizeWords(transcript);
        string text = SingularizeWords(Canonicalize(rawText));
        foreach (string alias in new[] { term.Label }.Concat(term.Aliases))
        {
            string needle = SingularizeWords(Canonicalize(alias));
            if (needle.Length == 0 || !Contains(text, needle)) continue;

            // Retourne le texte réellement prononcé, sans convertir « deux » en « 2 » :
            // cette conversion détruirait le nombre à rattacher à la quantité.
            string spoken = CatalogHints.NormalizeWords(alias);
            string spokenWords = DigitsToWords(spoken);
            if (Contains(rawText, spoken)) return spoken;
            if (Contains(rawText, spokenWords)) return spokenWords;
            return spoken;
        }

        return null;
    }

    private static CatalogMatch Resolve(
        IReadOnlyList<CatalogTerm> terms,
        string? value,
        string transcript,
        bool service)
    {
        string text = CatalogHints.NormalizeWords(transcript);
        IReadOnlyList<string> mentioned = Mentioned(terms, text);
        IReadOnlyList<string> uniqueMentioned = UniquelyMentioned(terms, text);

        if (service)
        {
            string? spokenService = ExplicitService(text);
            if (spokenService is not null)
            {
                string canonical = CanonicalLabel(terms, spokenService);
                return string.Equals(value, canonical, StringComparison.Ordinal)
                    ? new CatalogMatch(canonical, null)
                    : new CatalogMatch(canonical, $"Prestation normalisée depuis la transcription : {canonical}");
            }
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            if (uniqueMentioned.Count == 1)
            {
                return new CatalogMatch(uniqueMentioned[0], $"Valeur absente du modèle, retrouvée dans la transcription : {uniqueMentioned[0]}");
            }

            return mentioned.Count > 0
                ? new CatalogMatch(null, "Alias ambigu, aucun choix arbitraire.")
                : new CatalogMatch(null, null);
        }

        string[] candidates = CandidatesForValue(terms, value);
        if (candidates.Length == 0)
        {
            // Une valeur hors catalogue est remplacée par le seul libellé réellement
            // prononcé : la transcription, jamais le modèle, fait foi.
            if (uniqueMentioned.Count == 1)
            {
                return new CatalogMatch(uniqueMentioned[0], $"Valeur corrigée depuis la transcription : {uniqueMentioned[0]}");
            }

            return mentioned.Count > 0
                ? new CatalogMatch(null, $"Alias ambigu, aucun choix arbitraire : {value}")
                : new CatalogMatch(null, $"Valeur absente du catalogue et non prouvée par la transcription : {value}");
        }

        if (candidates.Length > 1)
        {
            string[] disambiguated = candidates.Where(uniqueMentioned.Contains).ToArray();
            if (disambiguated.Length == 1)
            {
                return new CatalogMatch(disambiguated[0], $"Alias ambigu désambigué par la transcription : {disambiguated[0]}");
            }

            return new CatalogMatch(null, $"Alias ambigu, aucun choix arbitraire : {value}");
        }

        string candidate = candidates[0];
        if (uniqueMentioned.Contains(candidate))
        {
            return string.Equals(value, candidate, StringComparison.Ordinal)
                ? new CatalogMatch(candidate, null)
                : new CatalogMatch(candidate, $"Valeur normalisée vers le libellé exact : {candidate}");
        }

        if (mentioned.Contains(candidate))
        {
            return new CatalogMatch(null, $"Alias ambigu, aucun choix arbitraire : {value}");
        }

        if (uniqueMentioned.Count == 1)
        {
            return new CatalogMatch(uniqueMentioned[0], $"Valeur corrigée depuis la transcription : {uniqueMentioned[0]}");
        }

        if (mentioned.Count == 1)
        {
            return new CatalogMatch(null, $"Alias ambigu, aucun choix arbitraire : {mentioned[0]}");
        }

        if (mentioned.Count == 0)
        {
            return new CatalogMatch(null, $"Valeur non prouvée par la transcription : {value}");
        }

        return new CatalogMatch(null, $"Plusieurs valeurs possibles dans la transcription, aucun choix arbitraire : {value}");
    }

    private static string[] CandidatesForValue(IReadOnlyList<CatalogTerm> terms, string value)
    {
        string needle = SingularizeWords(Canonicalize(value));
        var results = new List<string>();
        foreach (CatalogTerm term in terms)
        {
            string label = SingularizeWords(Canonicalize(term.Label));
            if (label == needle || term.Aliases.Any(alias => SingularizeWords(Canonicalize(alias)) == needle))
            {
                if (!results.Contains(term.Label, StringComparer.Ordinal))
                {
                    results.Add(term.Label);
                }
            }
        }

        return results.ToArray();
    }

    private static string? CanonicalServiceLabel(IReadOnlyList<CatalogTerm> terms, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string[] candidates = CandidatesForValue(terms, value);
        return candidates.Length == 1 ? candidates[0] : null;
    }

    /// <summary>Position de la première occurrence de la catégorie d'un article dans la transcription.</summary>
    private static int? FindCategoryIndex(
        IReadOnlyList<CatalogTerm> terms,
        string normalizedText,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string[] candidates = CandidatesForValue(terms, value);
        if (candidates.Length == 0) return null;

        int? best = null;
        foreach (CatalogTerm term in terms.Where(t => candidates.Contains(t.Label, StringComparer.Ordinal)))
        {
            foreach (string alias in new[] { term.Label }.Concat(term.Aliases))
            {
                string needle = SingularizeWords(CatalogHints.NormalizeWords(alias));
                if (needle.Length == 0) continue;
                int index = normalizedText.IndexOf(needle, StringComparison.Ordinal);
                if (index >= 0 && (best is null || index < best.Value)) best = index;
            }
        }

        return best;
    }

    /// <summary>
    /// Règle déterministe documentée : les nombres en toutes lettres sont équivalents
    /// à leur chiffre dans un libellé (« costume deux pièces » = « costume 2 pièces »).
    /// </summary>
    private static string Canonicalize(string value)
    {
        string text = CatalogHints.Normalize(value);
        return Regex.Replace(
            text,
            @"\b(un|une|deux|trois|quatre|cinq|six|sept|huit|neuf|dix|onze|douze)\b",
            match => match.Value switch
            {
                "un" or "une" => "1",
                "deux" => "2",
                "trois" => "3",
                "quatre" => "4",
                "cinq" => "5",
                "six" => "6",
                "sept" => "7",
                "huit" => "8",
                "neuf" => "9",
                "dix" => "10",
                "onze" => "11",
                _ => "12"
            });
    }

    private static IReadOnlyList<string> Mentioned(IReadOnlyList<CatalogTerm> terms, string normalizedTranscript)
    {
        string text = SingularizeWords(Canonicalize(normalizedTranscript));
        var results = new List<string>();
        foreach (CatalogTerm term in terms)
        {
            bool found = Contains(text, SingularizeWords(Canonicalize(term.Label))) ||
                         term.Aliases.Any(alias => Contains(text, SingularizeWords(Canonicalize(alias))));
            if (found && !results.Contains(term.Label, StringComparer.Ordinal))
            {
                results.Add(term.Label);
            }
        }

        return results;
    }

    /// <summary>
    /// Libellés prouvés par un terme ou un alias qui n'appartient qu'à eux.
    /// Un alias partagé (« costard » pour 2 et 3 pièces) ne prouve aucun choix.
    /// </summary>
    private static IReadOnlyList<string> UniquelyMentioned(
        IReadOnlyList<CatalogTerm> terms,
        string normalizedTranscript)
    {
        string text = SingularizeWords(Canonicalize(normalizedTranscript));
        var results = new List<string>();
        foreach (CatalogTerm term in terms)
        {
            string[] own = new[] { term.Label }.Concat(term.Aliases)
                .Select(x => SingularizeWords(Canonicalize(x)))
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            bool unique = own.Any(needle =>
                Contains(text, needle) &&
                !terms.Any(other => !ReferenceEquals(other, term) &&
                    new[] { other.Label }.Concat(other.Aliases)
                        .Select(x => SingularizeWords(Canonicalize(x)))
                        .Contains(needle, StringComparer.Ordinal)));

            if (unique && !results.Contains(term.Label, StringComparer.Ordinal))
            {
                results.Add(term.Label);
            }
        }

        return results;
    }

    /// <summary>Convertit les chiffres d'un libellé en nombres en toutes lettres.</summary>
    private static string DigitsToWords(string value) =>
        Regex.Replace(value, @"\b\d+\b", match => match.Value switch
        {
            "1" => "un",
            "2" => "deux",
            "3" => "trois",
            "4" => "quatre",
            "5" => "cinq",
            "6" => "six",
            "7" => "sept",
            "8" => "huit",
            "9" => "neuf",
            "10" => "dix",
            _ => match.Value
        });

    private static string CanonicalLabel(IReadOnlyList<CatalogTerm> terms, string desired)
    {
        string needle = CatalogHints.Normalize(desired);
        CatalogTerm? match = terms.FirstOrDefault(term => CatalogHints.Normalize(term.Label) == needle);
        return match?.Label ?? desired;
    }

    private static bool Contains(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(needle))
        {
            return false;
        }

        string text = CatalogHints.NormalizeWords(haystack);
        return $" {text} ".Contains($" {needle} ", StringComparison.Ordinal);
    }

    private static string SingularizeWords(string value) =>
        string.Join(" ", value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Singularize));

    private static string Singularize(string word)
    {
        // Règles déterministes documentées pour les pluriels irréguliers du catalogue.
        if (word is "manteaux") return "manteau";
        if (word is "rideaux") return "rideau";
        if (word.Length > 3 && word.EndsWith("s", StringComparison.Ordinal) && !word.EndsWith("ss", StringComparison.Ordinal))
        {
            return word[..^1];
        }

        return word;
    }
}
