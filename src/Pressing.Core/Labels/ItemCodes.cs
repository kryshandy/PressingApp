using System.Text.RegularExpressions;
using Pressing.Core.Entities;

namespace Pressing.Core.Labels;

/// <summary>Code d'étiquette décodé : ticket de la commande, rang de l'article (ligne) et pièce éventuelle.</summary>
/// <param name="TicketNumber">Numéro de ticket complet, ex. <c>PRESS-2026-00042</c>.</param>
/// <param name="Rank">Rang (à partir de 1) de la ligne d'article dans la commande, par ordre croissant d'identifiant.</param>
/// <param name="Unit">Pièce de la ligne (<c>a</c>, <c>b</c>…) ; <c>null</c> pour une ligne de quantité 1.</param>
public sealed record ItemCode(string TicketNumber, int Rank, string? Unit)
{
    /// <summary>Code de la ligne sans la pièce : <c>PRESS-AAAA-NNNNN-II</c>.</summary>
    public string LineCode => ItemCodes.Format(TicketNumber, Rank);

    /// <summary>Code imprimé dans le QR : <c>PRESS-AAAA-NNNNN-II</c> ou <c>PRESS-AAAA-NNNNN-II-a</c>.</summary>
    public string FullCode => ItemCodes.Format(TicketNumber, Rank, Unit);

    /// <summary>
    /// Vrai pour l'étiquette qui fait avancer la ligne en atelier : celle sans pièce (quantité 1) ou la pièce « a ».
    /// Le statut est porté par la ligne (pas par la pièce) : les autres pièces sont des étiquettes de suivi.
    /// </summary>
    public bool IsPrimary => Unit is null || Unit == "a";
}

/// <summary>
/// <b>Format des codes d'étiquette</b>, dérivé du ticket SANS colonne supplémentaire en base :
/// <c>PRESS-AAAA-NNNNN-II</c> (ticket + rang de la ligne sur 2 chiffres), et pour une ligne de quantité &gt; 1
/// un suffixe de pièce en minuscules : <c>PRESS-AAAA-NNNNN-II-a</c>, <c>-b</c>, <c>-c</c>… (<c>-aa</c> après <c>-z</c>).
/// Le rang est la position de la ligne parmi les lignes de la commande triées par identifiant : stable, car une
/// commande ne perd jamais de ligne (une ligne annulée garde son rang).
/// </summary>
public static partial class ItemCodes
{
    [GeneratedRegex(@"^PRESS-(?<year>\d{4})-(?<num>\d{5,})-(?<rank>\d{2,3})(?:-(?<unit>[a-z]{1,3}))?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    /// <summary>Assemble un code ; <paramref name="unit"/> null pour le code de ligne.</summary>
    public static string Format(string ticketNumber, int rank, string? unit = null) =>
        $"{ticketNumber}-{rank:00}" + (string.IsNullOrEmpty(unit) ? string.Empty : "-" + unit);

    /// <summary>Lettres de la pièce n° <paramref name="index"/> (à partir de 1) : a, b… z, aa, ab…</summary>
    public static string UnitSuffix(int index)
    {
        if (index < 1) throw new ArgumentOutOfRangeException(nameof(index));
        var letters = new Stack<char>();
        for (int n = index; n > 0; n = (n - 1) / 26)
        {
            letters.Push((char)('a' + (n - 1) % 26));
        }

        return new string(letters.ToArray());
    }

    /// <summary>Rang (à partir de 1) d'une ligne dans la commande.</summary>
    public static int RankOf(Order order, OrderItem item)
    {
        int rank = order.Items.OrderBy(i => i.Id).ToList().FindIndex(i => i.Id == item.Id) + 1;
        return rank > 0 ? rank : throw new ArgumentException("Cet article n'appartient pas à la commande.", nameof(item));
    }

    /// <summary>
    /// Décode un code saisi ou scanné. Tolère les espaces, la casse et les tirets typographiques ; retourne
    /// <c>false</c> pour toute valeur qui n'est pas un code d'étiquette (jamais d'exception).
    /// </summary>
    public static bool TryParseItemCode(string? text, out ItemCode? code)
    {
        code = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string normalized = text.Trim()
            .Replace('‐', '-').Replace('‑', '-').Replace('–', '-').Replace('—', '-');
        string[] parts = normalized.Split('-');
        // Tout est normalisé en majuscules sauf la pièce (dernier segment alphabétique), écrite en minuscules.
        for (int i = 0; i < parts.Length; i++)
        {
            bool isUnit = i == 4 && parts.Length == 5;
            parts[i] = isUnit ? parts[i].ToLowerInvariant() : parts[i].ToUpperInvariant();
        }

        Match match = CodePattern().Match(string.Join('-', parts));
        if (!match.Success) return false;
        if (!int.TryParse(match.Groups["rank"].Value, out int rank) || rank < 1) return false;

        string ticket = $"PRESS-{match.Groups["year"].Value}-{match.Groups["num"].Value}";
        string? unit = match.Groups["unit"].Success ? match.Groups["unit"].Value : null;
        code = new ItemCode(ticket, rank, unit);
        return true;
    }
}
