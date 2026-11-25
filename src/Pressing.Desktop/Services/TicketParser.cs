using System.Text.RegularExpressions;

namespace Pressing.Desktop.Services;

/// <summary>Normalise la saisie d'un ticket : « 12 », « press-2026-12 » → « PRESS-2026-00012 ».</summary>
public static partial class TicketParser
{
    [GeneratedRegex(@"^(?:PRESS-(\d{4})-(\d{1,5})|(\d{1,5}))$", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();

    /// <summary>Retourne le ticket complet, ou null si la saisie ne ressemble pas à un ticket.</summary>
    public static string? Normalize(string? text)
    {
        Match match = Pattern().Match((text ?? string.Empty).Trim());
        if (!match.Success)
        {
            return null;
        }

        return match.Groups[3].Success
            ? $"PRESS-{DateTime.Now.Year}-{int.Parse(match.Groups[3].Value):00000}"
            : $"PRESS-{match.Groups[1].Value}-{int.Parse(match.Groups[2].Value):00000}";
    }
}
