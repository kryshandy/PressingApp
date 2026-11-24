using Microsoft.AspNetCore.Html;
using Pressing.Core;

namespace Pressing.Web.Infrastructure;

/// <summary>Icônes SVG inline, trait 1,5 px, grille 24 (aucune bibliothèque externe).</summary>
public static class Icons
{
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["logo"] = "<path d=\"M12 3s6 6.2 6 10.5A6 6 0 0 1 6 13.5C6 9.2 12 3 12 3z\"/><path d=\"M9.3 14.2a3 3 0 0 0 2.4 2.6\"/>",
        ["arrow-right"] = "<path d=\"M5 12h14M13 6l6 6-6 6\"/>",
        ["arrow-left"] = "<path d=\"M19 12H5M11 6l-6 6 6 6\"/>",
        ["check"] = "<path d=\"M5 12.5l4.5 4.5L19 7.5\"/>",
        ["clock"] = "<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M12 7v5l3 2\"/>",
        ["map-pin"] = "<path d=\"M12 21s7-6.2 7-11.5A7 7 0 0 0 5 9.5C5 14.8 12 21 12 21z\"/><circle cx=\"12\" cy=\"9.5\" r=\"2.5\"/>",
        ["phone"] = "<path d=\"M5 4h3.5l1.8 4.5-2.2 1.4a11 11 0 0 0 5.5 5.5l1.4-2.2L20 15v3.5a1.5 1.5 0 0 1-1.6 1.5C10.6 19.5 4.5 13.4 4 5.6A1.5 1.5 0 0 1 5 4z\"/>",
        ["mail"] = "<rect x=\"3\" y=\"5\" width=\"18\" height=\"14\" rx=\"2\"/><path d=\"M3.5 7l8.5 6 8.5-6\"/>",
        ["message"] = "<path d=\"M4 20l1.3-4.2A8 8 0 1 1 8.4 19z\"/><path d=\"M9 10.5c.4 1.6 1.9 3.1 3.5 3.5l1-1.2 1.8.8v1\"/>",
        ["shirt"] = "<path d=\"M8.5 4L3 7l2 4 2.5-1V20h9V10l2.5 1 2-4-5.5-3a3.5 3.5 0 0 1-7 0z\"/>",
        ["droplet"] = "<path d=\"M12 3s6 6.2 6 10.5A6 6 0 0 1 6 13.5C6 9.2 12 3 12 3z\"/>",
        ["iron"] = "<path d=\"M3 17h16a2 2 0 0 0 2-2c0-3.5-3-6-7-6H9a6 6 0 0 0-6 6z\"/><path d=\"M3 20.5h18M8 13.5h6\"/>",
        ["hanger"] = "<path d=\"M12 8a2 2 0 1 0-2-2\"/><path d=\"M12 8v2l8.5 5.5a1 1 0 0 1-.6 1.8H4.1a1 1 0 0 1-.6-1.8L12 10\"/>",
        ["sparkle"] = "<path d=\"M11 3l1.8 5.2L18 10l-5.2 1.8L11 17l-1.8-5.2L4 10l5.2-1.8z\"/><path d=\"M19 16v4M17 18h4\"/>",
        ["bolt"] = "<path d=\"M13 3L5 13.5h6L10 21l8-10.5h-6z\"/>",
        ["package"] = "<path d=\"M3.5 8L12 4l8.5 4v8L12 20l-8.5-4z\"/><path d=\"M3.5 8L12 12l8.5-4M12 12v8\"/>",
        ["user"] = "<circle cx=\"12\" cy=\"8\" r=\"4\"/><path d=\"M4.5 20a7.5 7.5 0 0 1 15 0\"/>",
        ["home"] = "<path d=\"M4 11l8-7 8 7v8.5a.5.5 0 0 1-.5.5H15v-6H9v6H4.5a.5.5 0 0 1-.5-.5z\"/>",
        ["list"] = "<path d=\"M9 6h11M9 12h11M9 18h11M4.5 6h.01M4.5 12h.01M4.5 18h.01\"/>",
        ["star"] = "<path d=\"M12 3.5l2.6 5.3 5.9.9-4.3 4.1 1 5.8L12 16.8l-5.2 2.8 1-5.8L3.5 9.7l5.9-.9z\"/>",
        ["lock"] = "<rect x=\"5\" y=\"11\" width=\"14\" height=\"9\" rx=\"2\"/><path d=\"M8 11V8a4 4 0 0 1 8 0v3\"/>",
        ["logout"] = "<path d=\"M9 4H5.5A1.5 1.5 0 0 0 4 5.5v13A1.5 1.5 0 0 0 5.5 20H9M15 8l4 4-4 4M19 12H9\"/>",
        ["download"] = "<path d=\"M12 4v11M7.5 11L12 15.5 16.5 11M5 20h14\"/>",
        ["menu"] = "<path d=\"M4 7h16M4 12h16M4 17h16\"/>",
        ["close"] = "<path d=\"M6 6l12 12M18 6L6 18\"/>",
        ["search"] = "<circle cx=\"11\" cy=\"11\" r=\"6.5\"/><path d=\"M20 20l-4.2-4.2\"/>",
        ["calendar"] = "<rect x=\"4\" y=\"5.5\" width=\"16\" height=\"14\" rx=\"2\"/><path d=\"M4 10h16M8.5 3.5v4M15.5 3.5v4\"/>",
        ["tag"] = "<path d=\"M3.5 12.5V4.5a1 1 0 0 1 1-1h8l8 8-9 9z\"/><circle cx=\"8.5\" cy=\"8.5\" r=\"1.2\"/>",
        ["shield"] = "<path d=\"M12 3l7.5 3v5.5c0 4.5-3.2 8-7.5 9.5-4.3-1.5-7.5-5-7.5-9.5V6z\"/><path d=\"M9 12l2.2 2.2L15.5 10\"/>",
        ["truck"] = "<path d=\"M3 6.5h10v9H3zM13 9.5h4.5L21 13v2.5h-8\"/><circle cx=\"7\" cy=\"17.5\" r=\"1.8\"/><circle cx=\"17\" cy=\"17.5\" r=\"1.8\"/>",
        ["alert"] = "<path d=\"M12 4l9 15.5H3z\"/><path d=\"M12 10v4.5M12 17.2h.01\"/>",
        ["wallet"] = "<path d=\"M4 7.5A2.5 2.5 0 0 1 6.5 5H18v3\"/><path d=\"M4 7.5V17a2 2 0 0 0 2 2h13a1 1 0 0 0 1-1v-8a1 1 0 0 0-1-1H6.5A2.5 2.5 0 0 1 4 7.5z\"/><circle cx=\"16\" cy=\"13.5\" r=\"1\"/>",
        ["info"] = "<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M12 11v5M12 8h.01\"/>",
        ["eye"] = "<path d=\"M2.5 12S6 5.5 12 5.5 21.5 12 21.5 12 18 18.5 12 18.5 2.5 12 2.5 12z\"/><circle cx=\"12\" cy=\"12\" r=\"3\"/>",
        ["eye-off"] = "<path d=\"M4 4l16 16M9.9 6A9 9 0 0 1 12 5.5c6 0 9.5 6.5 9.5 6.5a16 16 0 0 1-3 3.8M6.4 7.6A15.5 15.5 0 0 0 2.5 12S6 18.5 12 18.5a8.7 8.7 0 0 0 3.6-.8\"/><path d=\"M9.9 10A3 3 0 0 0 14 14.1\"/>",
        ["receipt"] = "<path d=\"M6 3.5h12v17l-3-2-3 2-3-2-3 2z\"/><path d=\"M9 8.5h6M9 12h6\"/>",
        ["gift"] = "<rect x=\"4\" y=\"9\" width=\"16\" height=\"11\" rx=\"1.5\"/><path d=\"M3 9h18V6.5H3zM12 6.5V20M12 6.5C12 4.5 9.5 3 8.5 4.5S9.5 6.5 12 6.5zM12 6.5c0-2 2.5-3.5 3.5-2s-1 2-3.5 2z\"/>",
        ["dashboard"] = "<rect x=\"4\" y=\"4\" width=\"7\" height=\"8\" rx=\"1.5\"/><rect x=\"13\" y=\"4\" width=\"7\" height=\"5\" rx=\"1.5\"/><rect x=\"13\" y=\"11\" width=\"7\" height=\"9\" rx=\"1.5\"/><rect x=\"4\" y=\"14\" width=\"7\" height=\"6\" rx=\"1.5\"/>"
    };

    public static IHtmlContent Svg(string name, string? cssClass = null, int size = 24)
    {
        if (!Paths.TryGetValue(name, out var inner)) inner = Paths["info"];
        var cls = "icon" + (string.IsNullOrEmpty(cssClass) ? "" : " " + cssClass);
        return new HtmlString(
            $"<svg class=\"{cls}\" width=\"{size}\" height=\"{size}\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" " +
            $"stroke-width=\"1.5\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\">{inner}</svg>");
    }

    public static string ForService(ServiceType type) => type switch
    {
        ServiceType.Washing => "droplet",
        ServiceType.WashAndIron => "shirt",
        ServiceType.Ironing => "iron",
        ServiceType.DryCleaning => "hanger",
        ServiceType.StainRemoval => "sparkle",
        _ => "shirt"
    };
}
