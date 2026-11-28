using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Pressing.Core.Assistant;

/// <summary>Résultat d'un rapprochement : aucun candidat, un seul, ou plusieurs (ambigu).</summary>
public enum MatchKind
{
    None = 0,
    Single = 1,
    Ambiguous = 2
}

/// <summary>Catégorie du catalogue, réduite à ce qu'il faut pour la rapprocher d'un mot entendu.</summary>
public sealed record CatalogEntry(int Id, string Name);

/// <summary>Client de la base, réduit à ce qu'il faut pour le rapprocher d'un nom entendu.</summary>
public sealed record ClientEntry(int Id, string FullName, string? Phone);

/// <summary>Résultat de rapprochement. <see cref="Approximate"/> : trouvé malgré une faute de frappe ou de reconnaissance.</summary>
public sealed record MatchResult<T>(MatchKind Kind, T? Match, IReadOnlyList<T> Candidates, bool Approximate) where T : class;

/// <summary>Numéro de ticket cité à l'oral : complet (« PRESS-2026-00142 ») ou court (« 142 »).</summary>
public sealed record TicketReference(string? FullTicket, int? Number)
{
    /// <summary>Texte affichable dans un avertissement.</summary>
    public string Display => FullTicket ?? $"n° {Number}";
}

/// <summary>
/// Rapprochement du texte entendu avec le catalogue et les clients. Logique pure, sans base de données :
/// insensible à la casse, aux accents et à la ponctuation ; tolère le pluriel, les synonymes courants et les fautes.
/// Règle d'or : en cas de doute (aucun candidat ou plusieurs), on ne devine jamais — l'appelant signale l'incertitude.
/// </summary>
public static class AssistantMatching
{
    // Mots vides retirés avant de comparer (« robe de soirée » ↔ « robe soirée »).
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "de", "du", "des", "d", "la", "le", "les", "un", "une", "l", "pour", "et", "a"
    };

    // Synonymes courants du comptoir → mot du catalogue (déjà normalisés et au singulier).
    private static readonly Dictionary<string, string> Synonyms = new(StringComparer.Ordinal)
    {
        ["costard"] = "costume",
        ["costar"] = "costume",
        ["pullover"] = "pull",
        ["chemisette"] = "chemise",
        ["deux"] = "2",
        ["trois"] = "3"
    };

    // Titres de civilité qui ne font pas partie du nom.
    private static readonly HashSet<string> Civilities = new(StringComparer.Ordinal)
    {
        "mme", "madame", "mlle", "mademoiselle", "mr", "monsieur", "m", "dr", "docteur", "mrs", "mister", "client", "cliente"
    };

    private static readonly Regex FullTicketRegex =
        new(@"PRESS\s*-?\s*(\d{4})\s*-?\s*(\d{1,5})", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ShortTicketRegex =
        new(@"^\D*(\d{1,5})\D*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // ------------------------------------------------------------------ texte

    /// <summary>Minuscules, sans accents, ponctuation remplacée par des espaces, espaces compactés.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        string decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        bool lastWasSpace = true;
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    private static string[] Tokens(string? text) =>
        Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static string Singular(string token) =>
        token.Length > 3 && (token.EndsWith('s') || token.EndsWith('x')) ? token[..^1] : token;

    /// <summary>Mots comparables : normalisés, sans mots vides, au singulier, synonymes appliqués.</summary>
    private static List<string> CanonicalTokens(string? text) =>
        Tokens(text)
            .Where(t => !StopWords.Contains(t))
            // Synonyme avant le pluriel (« deux » finit par « x » mais n'est pas un pluriel), puis encore après (« costards »).
            .Select(t => Synonyms.TryGetValue(t, out string? direct) ? direct : Singular(t))
            .Select(t => Synonyms.TryGetValue(t, out string? canonical) ? canonical : t)
            .ToList();

    /// <summary>Nombre de fautes tolérées selon la longueur du mot (aucune pour les mots courts).</summary>
    private static int Tolerance(string token) => token.Length switch
    {
        <= 4 => 0,
        <= 8 => 1,
        _ => 2
    };

    /// <summary>Distance d'édition (Levenshtein) entre deux mots, avec arrêt anticipé au-delà de <paramref name="max"/>.</summary>
    public static int Distance(string a, string b, int max = int.MaxValue)
    {
        if (a == b) return 0;
        if (Math.Abs(a.Length - b.Length) > max) return max + 1;

        int[] previous = new int[b.Length + 1];
        int[] current = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) previous[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            int rowMin = current[0];
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                rowMin = Math.Min(rowMin, current[j]);
            }

            if (rowMin > max) return max + 1;
            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    // ------------------------------------------------------------------ classement générique

    /// <summary>
    /// Chaque mot entendu doit retrouver un mot distinct de l'élément. Exact d'abord, avec tolérance aux fautes ensuite.
    /// Renvoie le score (100 − mots en trop ; 50 − mots en trop si faute tolérée), ou 0 si tout n'est pas retrouvé.
    /// </summary>
    private static (int Score, bool Approximate) Score(IReadOnlyList<string> wanted, IReadOnlyList<string> have)
    {
        if (wanted.Count == 0 || wanted.Count > have.Count) return (0, false);
        int extra = have.Count - wanted.Count;

        if (Covers(wanted, have, fuzzy: false)) return (100 - extra, false);
        if (Covers(wanted, have, fuzzy: true)) return (50 - extra, true);
        return (0, false);
    }

    private static bool Covers(IReadOnlyList<string> wanted, IReadOnlyList<string> have, bool fuzzy)
    {
        bool[] used = new bool[have.Count];
        foreach (string word in wanted)
        {
            int found = -1;
            for (int i = 0; i < have.Count && found < 0; i++)
            {
                if (used[i]) continue;
                int tolerance = fuzzy ? Tolerance(word) : 0;
                if (have[i] == word || (tolerance > 0 && Distance(word, have[i], tolerance) <= tolerance)) found = i;
            }

            if (found < 0) return false;
            used[found] = true;
        }

        return true;
    }

    private static MatchResult<T> Rank<T>(
        IReadOnlyList<string> wanted,
        IEnumerable<T> items,
        Func<T, IReadOnlyList<string>> tokensOf,
        Func<T, string> sortKey) where T : class
    {
        var scored = items
            .Select(item =>
            {
                (int score, bool approximate) = Score(wanted, tokensOf(item));
                return (Item: item, Score: score, Approximate: approximate);
            })
            .Where(x => x.Score > 0)
            .ToList();

        if (scored.Count == 0) return new MatchResult<T>(MatchKind.None, null, [], false);

        int best = scored.Max(x => x.Score);
        var top = scored.Where(x => x.Score == best).OrderBy(x => sortKey(x.Item), StringComparer.Ordinal).ToList();
        bool approximateBest = top[0].Approximate;

        return top.Count == 1
            ? new MatchResult<T>(MatchKind.Single, top[0].Item, [top[0].Item], approximateBest)
            : new MatchResult<T>(MatchKind.Ambiguous, null, top.Select(x => x.Item).Take(5).ToList(), approximateBest);
    }

    // ------------------------------------------------------------------ catégories, services, clients

    /// <summary>Rapproche un nom de vêtement entendu (« costard », « chemises ») d'une catégorie du catalogue.</summary>
    public static MatchResult<CatalogEntry> MatchCategory(string? heard, IEnumerable<CatalogEntry> catalog)
    {
        List<string> wanted = CanonicalTokens(heard);
        if (wanted.Count == 0) return new MatchResult<CatalogEntry>(MatchKind.None, null, [], false);

        return Rank(wanted, catalog, entry => CanonicalTokens(entry.Name), entry => entry.Name);
    }

    /// <summary>Rapproche un nom entendu (« Mme Ngo ») des clients. Plusieurs clients à égalité = homonymes (ambigu).</summary>
    public static MatchResult<ClientEntry> MatchClient(string? heard, IEnumerable<ClientEntry> clients)
    {
        List<string> wanted = Tokens(heard).Where(t => !Civilities.Contains(t) && !StopWords.Contains(t)).ToList();
        if (wanted.Count == 0) return new MatchResult<ClientEntry>(MatchKind.None, null, [], false);

        return Rank(wanted, clients, client => Tokens(client.FullName).ToList(), client => client.FullName);
    }

    /// <summary>
    /// Service entendu (« lavage repassage », « repassé », « nettoyage à sec ») → énumération métier.
    /// <c>null</c> si rien n'est reconnu : l'appelant le signale au lieu de deviner.
    /// </summary>
    public static ServiceType? MatchService(string? heard)
    {
        string[] tokens = Tokens(heard).Where(t => !StopWords.Contains(t)).ToArray();
        if (tokens.Length == 0) return null;

        bool Like(string stem, string canonical) =>
            tokens.Any(t => t.StartsWith(stem, StringComparison.Ordinal) || (t.Length >= 5 && Distance(t, canonical, 2) <= 2));

        bool dry = tokens.Contains("sec") || Like("nettoy", "nettoyage");
        bool stain = Like("detach", "detachage");
        bool wash = Like("lav", "lavage");
        bool iron = Like("repas", "repassage");

        if (dry) return ServiceType.DryCleaning;
        if (stain) return ServiceType.StainRemoval;
        if (wash && iron) return ServiceType.WashAndIron;
        if (iron) return ServiceType.Ironing;
        if (wash) return ServiceType.Washing;
        return null;
    }

    // ------------------------------------------------------------------ ticket

    /// <summary>
    /// « PRESS-2026-00142 », « press 2026 142 » → ticket complet ; « 142 », « ticket 142 » → numéro court
    /// (le backend retrouve l'année). <c>null</c> si rien d'exploitable.
    /// </summary>
    public static TicketReference? ParseTicket(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string upper = raw.Trim().ToUpperInvariant();

        Match full = FullTicketRegex.Match(upper);
        if (full.Success)
        {
            int number = int.Parse(full.Groups[2].Value, CultureInfo.InvariantCulture);
            return new TicketReference($"PRESS-{full.Groups[1].Value}-{number:00000}", null);
        }

        Match shortForm = ShortTicketRegex.Match(upper);
        if (shortForm.Success)
        {
            int number = int.Parse(shortForm.Groups[1].Value, CultureInfo.InvariantCulture);
            return number > 0 ? new TicketReference(null, number) : null;
        }

        return null;
    }
}
