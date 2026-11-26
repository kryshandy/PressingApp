using System.Globalization;
using System.Text;

namespace Pressing.Client;

/// <summary>
/// Lecture tolérante des montants saisis (« 1.500,50 », « 1500,50 », « 1 500,50 », « 1500.50 »).
/// Le format de sortie reste un <see cref="decimal"/> invariant, identique à celui envoyé à l'API.
/// </summary>
public static class AmountParser
{
    /// <summary>Essaye de lire un montant. La virgule ou le dernier point est la décimale ; les autres séparateurs sont des groupes.</summary>
    public static bool TryParseAmount(string? text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string input = text.Trim();
        bool negative = input.StartsWith('-');
        if (negative) input = input[1..].TrimStart();
        if (input.Length == 0) return false;

        var cleaned = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            if (char.IsDigit(c) || c is ',' or '.' or ' ' or '\u00A0' or '\u202F' or '\u2009')
            {
                cleaned.Append(c);
            }
        }

        string s = cleaned.ToString().Trim();
        if (s.Length == 0) return false;

        int lastComma = s.LastIndexOf(',');
        int lastDot = s.LastIndexOf('.');
        char? decimalSeparator = (lastComma, lastDot) switch
        {
            _ when lastComma >= 0 && lastDot >= 0 => lastComma > lastDot ? ',' : '.',
            _ when lastComma >= 0 => ',',
            _ when lastDot >= 0 && s.Length - lastDot - 1 != 3 => '.', // 1500.50 => décimal ; 1.500 => milliers
            _ => null
        };

        var normalized = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (c == decimalSeparator) normalized.Append('.');
            else if (c is ',' or '.') continue; // séparateur de milliers
            else if (char.IsWhiteSpace(c)) continue; // espace (ordinaire, insécable, fine)
            else normalized.Append(c);
        }

        string number = normalized.ToString().Trim();
        if (number.Length == 0) return false;
        if (!decimal.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)) return false;

        if (negative) value = -value;
        return true;
    }
}
