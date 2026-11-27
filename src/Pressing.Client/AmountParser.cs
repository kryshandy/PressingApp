using System.Globalization;

namespace Pressing.Client;

/// <summary>
/// Lecture tolérante des montants saisis (« 1.500,50 », « 1500,50 », « 1 500,50 », « 1500.50 »).
/// Toute saisie contenant des lettres est refusée, sauf un suffixe de devise (« FCFA », « F CFA », « F »).
/// Le format de sortie reste un <see cref="decimal"/> invariant, identique à celui envoyé à l'API.
/// </summary>
public static class AmountParser
{
    private static readonly string[] CurrencySuffixes = { "F CFA", "FCFA", "XAF", "XOF", "CFA", "F" };

    /// <summary>Essaie de lire un montant ; renvoie false plutôt que de deviner.</summary>
    public static bool TryParseAmount(string? text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string input = text.Trim();
        bool negative = input.StartsWith('-');
        if (negative) input = input[1..].TrimStart();

        input = StripCurrencySuffix(input);
        if (input.Length == 0) return false;

        // Seuls les chiffres, les séparateurs et les espaces sont acceptés.
        foreach (char c in input)
        {
            bool ok = c is >= '0' and <= '9' or ',' or '.' or ' ' or '\u00A0' or '\u202F' or '\u2009';
            if (!ok) return false;
        }

        string s = new string(input.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (s.Length == 0) return false;

        int lastComma = s.LastIndexOf(',');
        int lastDot = s.LastIndexOf('.');
        string integer;
        string fraction = "";

        if (lastComma < 0 && lastDot < 0)
        {
            integer = s;
        }
        else if (lastComma >= 0 && lastDot >= 0)
        {
            // Les deux : le dernier est la décimale, l'autre sépare les milliers.
            int lastSep = Math.Max(lastComma, lastDot);
            char groupSep = s[lastSep] == ',' ? '.' : ',';
            if (!TryUngroup(s[..lastSep], groupSep, out integer)) return false;
            fraction = s[(lastSep + 1)..];
        }
        else
        {
            char sep = lastComma >= 0 ? ',' : '.';
            if (s.Count(c => c == sep) > 1)
            {
                // « 1.500.000 » : uniquement des milliers.
                if (!TryUngroup(s, sep, out integer)) return false;
            }
            else
            {
                int i = s.IndexOf(sep);
                string before = s[..i];
                string after = s[(i + 1)..];
                bool thousands = after.Length == 3 && before.Length is >= 1 and <= 3 && before[0] != '0';
                if (thousands) integer = before + after;   // « 1,500 » ou « 1.500 » => 1500
                else { integer = before; fraction = after; } // « 0.250 » => 0,25 ; « 1500,5 » => 1500,5
            }
        }

        if (integer.Length == 0) integer = "0";

        string number = fraction.Length > 0 ? $"{integer}.{fraction}" : integer;
        if (!decimal.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))
        {
            value = 0m;
            return false;
        }

        if (negative) value = -value;
        return true;
    }

    /// <summary>Retire un suffixe de devise éventuel (« 12 500 FCFA » => « 12 500 »).</summary>
    private static string StripCurrencySuffix(string input)
    {
        foreach (string suffix in CurrencySuffixes)
        {
            if (input.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return input[..^suffix.Length].TrimEnd();
        }
        return input;
    }

    /// <summary>Retire les séparateurs de milliers en vérifiant les groupes (1 à 3 chiffres, puis 3 chiffres).</summary>
    private static bool TryUngroup(string text, char separator, out string digits)
    {
        digits = "";
        string[] groups = text.Split(separator);
        if (groups[0].Length is < 1 or > 3) return false;
        for (int i = 1; i < groups.Length; i++)
            if (groups[i].Length != 3) return false;
        digits = string.Concat(groups);
        return true;
    }
}