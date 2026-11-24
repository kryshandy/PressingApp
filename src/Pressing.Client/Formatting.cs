using System.Globalization;
using Pressing.Core;

namespace Pressing.Client;

/// <summary>Helpers d'affichage partagés Desktop / Web (culture fr-FR).</summary>
public static class Fmt
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private static readonly NumberFormatInfo MoneyFormat = new()
    {
        NumberGroupSeparator = "\u00A0",
        NumberDecimalDigits = 0,
        NegativeSign = "-"
    };

    /// <summary>« 12 500 FCFA » (espace insécable, sans décimales).</summary>
    public static string Money(decimal amount) =>
        Math.Round(amount, 0, MidpointRounding.AwayFromZero).ToString("N0", MoneyFormat) + "\u00A0FCFA";

    public static string Date(DateTime value) => value.ToString("dd/MM/yyyy", Fr);

    public static string DateTime(DateTime value) => value.ToString("dd/MM/yyyy HH:mm", Fr);

    /// <summary>« il y a 5 min », « hier », « dans 2 h »…</summary>
    public static string Relative(DateTime value)
    {
        var now = System.DateTime.Now;
        var diff = now - value;
        if (diff < TimeSpan.Zero)
        {
            var ahead = -diff;
            if (ahead.TotalMinutes < 1) return "dans un instant";
            if (ahead.TotalMinutes < 60) return $"dans {(int)ahead.TotalMinutes} min";
            if (ahead.TotalHours < 24) return $"dans {(int)ahead.TotalHours} h";
            if (value.Date == now.Date.AddDays(1)) return "demain";
            return $"dans {(int)ahead.TotalDays} j";
        }
        if (diff.TotalSeconds < 45) return "à l'instant";
        if (diff.TotalMinutes < 60) return $"il y a {Math.Max(1, (int)diff.TotalMinutes)} min";
        if (value.Date == now.Date) return $"il y a {(int)diff.TotalHours} h";
        if (value.Date == now.Date.AddDays(-1)) return "hier";
        if (diff.TotalDays < 30) return $"il y a {(int)diff.TotalDays} j";
        if (diff.TotalDays < 365) return $"il y a {(int)(diff.TotalDays / 30)} mois";
        return $"il y a {(int)(diff.TotalDays / 365)} an(s)";
    }

    public static string StatusColor(OrderStatus status) => status switch
    {
        OrderStatus.Deposited => "#3B82F6",
        OrderStatus.InProgress => "#F59E0B",
        OrderStatus.Ready => "#10B981",
        OrderStatus.PartiallyPickedUp => "#8B5CF6",
        OrderStatus.Completed => "#64748B",
        OrderStatus.Cancelled => "#EF4444",
        _ => "#64748B"
    };

    public static string StatusColor(ItemStatus status) => status switch
    {
        ItemStatus.Deposited => "#3B82F6",
        ItemStatus.InProgress => "#F59E0B",
        ItemStatus.Ready => "#10B981",
        ItemStatus.PickedUp => "#64748B",
        ItemStatus.Cancelled => "#EF4444",
        _ => "#64748B"
    };
}
