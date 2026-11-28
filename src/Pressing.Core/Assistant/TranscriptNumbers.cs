using System.Globalization;
using System.Text.RegularExpressions;

namespace Pressing.Core.Assistant;

/// <summary>Nombre trouvé dans une transcription, avec sa position pour rattachement contextuel.</summary>
public sealed record NumberMatch(int Index, int Length, decimal Value);

/// <summary>
/// Lecture déterministe des nombres chiffrés et des nombres en toutes lettres.
/// Aucune valeur n'est déduite d'un simple voisinage sans marqueur explicite.
/// </summary>
public static class TranscriptNumbers
{
    private static readonly Dictionary<string, decimal> Units = new(StringComparer.Ordinal)
    {
        ["un"] = 1, ["une"] = 1, ["deux"] = 2, ["trois"] = 3, ["quatre"] = 4,
        ["cinq"] = 5, ["six"] = 6, ["sept"] = 7, ["huit"] = 8, ["neuf"] = 9,
        ["zero"] = 0
    };

    private static readonly Dictionary<string, decimal> Tens = new(StringComparer.Ordinal)
    {
        ["dix"] = 10, ["vingt"] = 20, ["trente"] = 30, ["quarante"] = 40,
        ["cinquante"] = 50, ["soixante"] = 60, ["soixante dix"] = 70,
        ["quatre vingt dix"] = 90
    };

    /// <summary>Tous les nombres explicitement prononcés, chiffres ou toutes lettres.</summary>
    public static IReadOnlyList<NumberMatch> Find(string text)
    {
        string normalized = CatalogHints.Normalize(text);
        var results = new List<NumberMatch>();
        decimal current = 0;
        int start = -1;
        int end = -1;
        bool inSequence = false;

        void Flush()
        {
            if (inSequence && start >= 0)
            {
                results.Add(new NumberMatch(start, Math.Max(1, end - start), current));
            }

            current = 0;
            start = -1;
            end = -1;
            inSequence = false;
        }

        foreach (Match match in Regex.Matches(normalized, @"\d+(?:[.,]\d{1,2})?|[a-z]+"))
        {
            string token = match.Value;
            if (decimal.TryParse(token.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal number))
            {
                Flush();
                results.Add(new NumberMatch(match.Index, match.Length, number));
                continue;
            }

            decimal value;
            if (Units.TryGetValue(token, out value))
            {
                if (!inSequence)
                {
                    start = match.Index;
                }

                current = current == 0 ? value : current + value;
                end = match.Index + match.Length;
                inSequence = true;
                continue;
            }

            if (token == "vingt" && inSequence && current > 0 && current < 10)
            {
                current *= 20;
                end = match.Index + match.Length;
                continue;
            }

            if (Tens.TryGetValue(token, out value))
            {
                if (!inSequence)
                {
                    start = match.Index;
                }

                current += value;
                end = match.Index + match.Length;
                inSequence = true;
                continue;
            }

            if (token == "cent" || token == "mille")
            {
                if (!inSequence)
                {
                    start = match.Index;
                }

                current = current == 0 ? (token == "cent" ? 100 : 1000)
                    : current * (token == "cent" ? 100 : 1000);
                end = match.Index + match.Length;
                inSequence = true;
                continue;
            }

            Flush();
        }

        Flush();
        return results.OrderBy(x => x.Index).ToArray();
    }

    /// <summary>Première valeur numérique associée à l'un des marqueurs fournis.</summary>
    public static decimal? WithMarker(string text, params string[] markers)
    {
        string normalized = CatalogHints.Normalize(text);
        foreach (NumberMatch match in Find(normalized))
        {
            int from = Math.Max(0, match.Index - 35);
            int to = Math.Min(normalized.Length, match.Index + match.Length + 20);
            string context = normalized[from..to];
            if (markers.Any(marker => context.Contains(CatalogHints.Normalize(marker), StringComparison.Ordinal)))
            {
                return match.Value;
            }
        }

        return null;
    }

    /// <summary>Nombre le plus proche d'un terme du catalogue, utilisé pour les quantités.</summary>
    public static decimal? Near(string text, string term, int before = 30, int after = 12) =>
        NearCore(CatalogHints.Normalize(text), term, before, after, excludeContextualNumbers: false);

    /// <summary>
    /// Quantité rattachée à un article, en ignorant les nombres qui appartiennent
    /// à un ticket, un montant, une date ou un numéro de téléphone.
    /// </summary>
    public static decimal? NearSafe(
        string text,
        string term,
        int before = 30,
        int after = 12,
        bool excludeNumberInsideTerm = false) =>
        NearCore(CatalogHints.Normalize(text), term, before, after, excludeContextualNumbers: true, excludeNumberInsideTerm);

    private static decimal? NearCore(
        string normalized,
        string term,
        int before,
        int after,
        bool excludeContextualNumbers,
        bool excludeNumberInsideTerm = false)
    {
        int index = FindTermIndex(normalized, term);
        if (index < 0)
        {
            return null;
        }

        int termEnd = index + CatalogHints.Normalize(term).Length;
        NumberMatch[] numbers = Find(normalized)
            .Where(x => !excludeContextualNumbers || !IsContextualNumber(normalized, x))
            .Where(x => !excludeNumberInsideTerm || x.Index + x.Length <= index || x.Index >= termEnd)
            .ToArray();
        NumberMatch? preceding = numbers.LastOrDefault(x => x.Index + x.Length <= index && index - (x.Index + x.Length) <= before);
        if (preceding is not null)
        {
            return preceding.Value;
        }

        NumberMatch? following = numbers.FirstOrDefault(x => x.Index >= index && x.Index - index <= after);
        return following?.Value;
    }

    private static bool IsContextualNumber(string text, NumberMatch match)
    {
        int from = Math.Max(0, match.Index - 24);
        int to = Math.Min(text.Length, match.Index + match.Length + 24);
        string context = text[from..to];
        string[] exclusions =
        [
            "ticket", "commande", "numero", "reference", " ref ", "acompte", "avance",
            "solde", "total", "montant", "prix", "telephone", "tel ", "demain",
            "aujourd hui", "apres demain", "lundi", "mardi", "mercredi", "jeudi",
            "vendredi", "samedi", "dimanche"
        ];
        return exclusions.Any(word => context.Contains(word, StringComparison.Ordinal));
    }

    private static int FindTermIndex(string normalized, string term)
    {
        string needle = CatalogHints.Normalize(term);
        int index = IndexOfPhrase(normalized, needle);
        if (index >= 0)
        {
            return index;
        }

        string singularText = SingularizeWords(normalized);
        string singularNeedle = SingularizeWords(needle);
        return IndexOfPhrase(singularText, singularNeedle);
    }

    private static int IndexOfPhrase(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(needle))
        {
            return -1;
        }

        int index = haystack.IndexOf(needle, StringComparison.Ordinal);
        return index;
    }

    private static string SingularizeWords(string value) =>
        string.Join(" ", value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word =>
            word switch
            {
                "manteaux" => "manteau",
                "rideaux" => "rideau",
                _ when word.Length > 3 && word.EndsWith("s", StringComparison.Ordinal) &&
                       !word.EndsWith("ss", StringComparison.Ordinal) => word[..^1],
                _ => word
            }));
}
