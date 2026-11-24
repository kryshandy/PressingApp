using System.Text.RegularExpressions;
using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;

namespace Pressing.Web.Infrastructure;

/// <summary>Petites aides d'affichage partagées par les vues.</summary>
public static class Ui
{
    private static readonly Dictionary<string, ItemStatus> ItemByLabel =
        Enum.GetValues<ItemStatus>().ToDictionary(s => s.Label(), s => s, StringComparer.OrdinalIgnoreCase);

    /// <summary>Couleur de pastille d'après le libellé d'un statut d'article (le suivi public ne donne que le libellé).</summary>
    public static string ColorForItemLabel(string label) =>
        ItemByLabel.TryGetValue(label, out var s) ? Fmt.StatusColor(s) : "#64748B";

    public static string ColorForOrderName(string statusName) =>
        Enum.TryParse<OrderStatus>(statusName, out var s) ? Fmt.StatusColor(s) : "#64748B";

    /// <summary>Étape courante de la timeline (0 dépôt, 1 traitement, 2 prêt, 3 retiré). -1 si annulée.</summary>
    public static int StepOf(OrderStatus status) => status switch
    {
        OrderStatus.Deposited => 0,
        OrderStatus.InProgress => 1,
        OrderStatus.Ready => 2,
        OrderStatus.PartiallyPickedUp => 2,
        OrderStatus.Completed => 3,
        _ => -1
    };

    public static int StepOf(string statusName) =>
        Enum.TryParse<OrderStatus>(statusName, out var s) ? StepOf(s) : 0;

    public static int Percent(OrderStatus status) => status switch
    {
        OrderStatus.Deposited => 12,
        OrderStatus.InProgress => 45,
        OrderStatus.Ready => 78,
        OrderStatus.PartiallyPickedUp => 90,
        OrderStatus.Completed => 100,
        _ => 0
    };

    public static bool IsActive(OrderStatus status) =>
        status is OrderStatus.Deposited or OrderStatus.InProgress or OrderStatus.Ready or OrderStatus.PartiallyPickedUp;

    public static string Greeting() => DateTime.Now.Hour >= 18 || DateTime.Now.Hour < 4 ? "Bonsoir" : "Bonjour";

    /// <summary>Découpe « Lun-Sam : 8h-19h | Dim : fermé » en lignes affichables.</summary>
    public static IReadOnlyList<string> HoursLines(string? hours)
    {
        if (string.IsNullOrWhiteSpace(hours)) return [];
        return Regex.Split(hours, @"\r?\n|\s*[|;]\s*")
            .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
    }

    public static string PhoneHref(string? phone) =>
        "tel:+" + new string((phone ?? "").Where(char.IsDigit).ToArray());

    public static string WhatsAppHref(string? phone)
    {
        var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        return string.IsNullOrEmpty(digits) ? "#" : "https://wa.me/" + digits;
    }

    public static string Initials(string? name)
    {
        var parts = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? "?" : string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
    }

    /// <summary>Prix « à partir de » pour un type de service, parmi les catégories actives.</summary>
    public static decimal? MinPrice(IEnumerable<CategoryDto> categories, ServiceType type)
    {
        var prices = categories.Where(c => c.IsActive)
            .SelectMany(c => c.Prices.Where(p => p.IsActive && p.ServiceType == type && p.Price > 0))
            .Select(p => p.Price).ToList();
        return prices.Count == 0 ? null : prices.Min();
    }
}
