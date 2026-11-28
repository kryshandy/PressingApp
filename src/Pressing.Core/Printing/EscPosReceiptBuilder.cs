using System.Globalization;
using System.Text;
using Pressing.Core.Entities;
using Pressing.Core.Options;

namespace Pressing.Core.Printing;

/// <summary>Réglages du ticket thermique 80 mm.</summary>
public sealed class EscPosOptions
{
    /// <summary>Largeur en caractères (police A d'une imprimante 80 mm : 48).</summary>
    public int Columns { get; init; } = 48;

    /// <summary>
    /// Page de codes : 858 (défaut, = CP850 + le signe €) ou 850. Les accents français et « € » y existent ;
    /// les caractères hors page sont remplacés par « ? » plutôt que d'imprimer n'importe quoi.
    /// </summary>
    public int CodePage { get; init; } = 858;

    /// <summary>Imprime le QR du ticket (commande GS ( k) ; sinon le numéro seul.</summary>
    public bool PrintQr { get; init; } = true;

    /// <summary>Envoie l'impulsion d'ouverture du tiroir-caisse (ESC p) à la fin du ticket.</summary>
    public bool OpenCashDrawer { get; init; }
}

/// <summary>
/// Octets ESC/POS bruts d'une imprimante de tickets (80 mm, compatible Epson) : écrits sans pilote, tels quels,
/// dans la file d'impression (type RAW). Choix d'encodage : <b>CP858</b> (<c>ESC t 19</c>) car c'est la page « Europe
/// de l'Ouest » d'une imprimante Epson avec « € » ; <b>CP850</b> (<c>ESC t 2</c>) en repli pour les modèles qui n'ont pas
/// la 858. L'UTF-8 n'est pas utilisé : la plupart des imprimantes d'entrée de gamme ne le comprennent pas.
/// </summary>
public static class EscPosReceiptBuilder
{
    private const byte Esc = 0x1B;
    private const byte Gs = 0x1D;
    private const byte Lf = 0x0A;

    static EscPosReceiptBuilder()
    {
        // Fournit CP850/CP858 (absentes de l'encodage .NET de base).
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>Numéro de page de codes ESC t pour une imprimante Epson.</summary>
    public static byte CodePageId(int codePage) => codePage switch
    {
        850 => 2,
        858 => 19,
        _ => throw new ArgumentOutOfRangeException(nameof(codePage), "Page de codes prise en charge : 850 ou 858.")
    };

    /// <summary>Impulsion d'ouverture du tiroir-caisse seule : <c>ESC p 0 25 250</c> (broche 2, 50 ms / 500 ms).</summary>
    public static byte[] DrawerPulse() => [Esc, (byte)'p', 0, 25, 250];

    /// <summary>Construit le ticket complet (initialisation → corps → QR → coupe).</summary>
    public static byte[] Build(Order order, ShopInfo shop, EscPosOptions? options = null)
    {
        options ??= new EscPosOptions();
        var w = new EscPosWriter(options.Columns, options.CodePage);
        string currency = shop.Currency;

        w.Initialize();
        w.Align(EscPosAlign.Center);
        w.Bold(true);
        w.Size(doubleHeight: true, doubleWidth: false);
        w.Line(shop.Name);
        w.Size(false, false);
        w.Bold(false);
        if (!string.IsNullOrWhiteSpace(shop.Slogan)) w.Wrapped(shop.Slogan);
        w.Wrapped($"{shop.Address}, {shop.City}");
        w.Line(shop.Phone);
        w.Separator('=');

        w.Align(EscPosAlign.Center);
        w.Line("TICKET DE DÉPÔT");
        w.Bold(true);
        w.Size(doubleHeight: true, doubleWidth: false);
        w.Line(order.TicketNumber);
        w.Size(false, false);
        w.Bold(false);
        if (order.IsExpress)
        {
            w.Bold(true);
            w.Line("*** EXPRESS ***");
            w.Bold(false);
        }

        w.Align(EscPosAlign.Left);
        w.Separator('-');
        w.Line($"Déposé le : {order.CreatedAt:dd/MM/yyyy HH:mm}");
        w.Line($"Retrait prévu : {order.DueDate:dd/MM/yyyy}");
        w.Line($"Client : {order.Client.FullName}");
        w.Line($"Tél. : {order.Client.Phone}");
        w.Separator('-');

        foreach (OrderItem item in order.Items.Where(i => i.Status != ItemStatus.Cancelled).OrderBy(i => i.Id))
        {
            w.Columns($"{item.Quantity} x {item.Category.Name}", Money(item.LineTotal, currency));
            string unit = Money(item.UnitPrice + (item.IsExpress ? item.ExpressSupplement : 0m), currency);
            w.Line($"    {item.ServiceType.Label()}{(item.IsExpress ? " (express)" : string.Empty)} - {unit}/pièce");
        }

        w.Separator('-');
        w.Columns("Sous-total", Money(order.Subtotal, currency));
        if (order.ExpressTotal > 0) w.Columns("Supplément express", Money(order.ExpressTotal, currency));
        if (order.DeliveryFee > 0) w.Columns("Livraison", Money(order.DeliveryFee, currency));
        if (order.DiscountAmount > 0) w.Columns("Remise", "-" + Money(order.DiscountAmount, currency));
        if (order.PromotionDiscount > 0) w.Columns($"Promotion {order.PromotionCode}", "-" + Money(order.PromotionDiscount, currency));
        if (order.LoyaltyDiscount > 0) w.Columns("Fidélité", "-" + Money(order.LoyaltyDiscount, currency));

        w.Bold(true);
        w.Size(doubleHeight: true, doubleWidth: false);
        w.Columns("TOTAL", Money(order.Total, currency));
        w.Size(false, false);
        w.Bold(false);

        decimal netPaid = order.Payments.Sum(p => p.Type == PaymentType.Refund ? -p.Amount : p.Amount);
        w.Columns("Payé", Money(netPaid, currency));
        w.Bold(true);
        w.Columns(order.Balance > 0 ? "RESTE À PAYER" : "SOLDE", Money(order.Balance, currency));
        w.Bold(false);
        w.Separator('-');

        w.Align(EscPosAlign.Center);
        if (options.PrintQr)
        {
            w.QrCode(order.TicketNumber);
            w.Line("Présentez ce code au retrait");
        }

        if (!string.IsNullOrWhiteSpace(shop.ReceiptFooter)) w.Wrapped(shop.ReceiptFooter);
        if (!string.IsNullOrWhiteSpace(shop.OpeningHours)) w.Wrapped($"Horaires : {shop.OpeningHours}");

        w.Feed(3);
        if (options.OpenCashDrawer) w.Raw(DrawerPulse());
        w.Cut();
        return w.ToArray();
    }

    /// <summary>Montant lisible sur 48 colonnes : « 12 500 FCFA » (espace simple, jamais d'espace insécable absent de la page).</summary>
    public static string Money(decimal value, string currency) =>
        value.ToString("N0", CultureInfo.InvariantCulture).Replace(',', ' ') + " " + currency;
}

public enum EscPosAlign : byte
{
    Left = 0,
    Center = 1,
    Right = 2
}

/// <summary>Écrivain ESC/POS de bas niveau : commandes, texte encodé, colonnes et retours à la ligne.</summary>
public sealed class EscPosWriter
{
    private readonly MemoryStream _buffer = new();
    private readonly Encoding _encoding;
    private readonly int _codePage;

    public EscPosWriter(int columns = 48, int codePage = 858)
    {
        if (columns < 16) throw new ArgumentOutOfRangeException(nameof(columns));
        Width = columns;
        _codePage = codePage;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _encoding = Encoding.GetEncoding(codePage, new EncoderReplacementFallback("?"), DecoderFallback.ReplacementFallback);
    }

    public int Width { get; }

    public byte[] ToArray() => _buffer.ToArray();

    public void Raw(params byte[] bytes) => _buffer.Write(bytes, 0, bytes.Length);

    /// <summary><c>ESC @</c> puis sélection de la page de codes (<c>ESC t n</c>).</summary>
    public void Initialize()
    {
        Raw(0x1B, (byte)'@');
        Raw(0x1B, (byte)'t', EscPosReceiptBuilder.CodePageId(_codePage));
    }

    /// <summary><c>ESC a n</c> : 0 gauche, 1 centre, 2 droite.</summary>
    public void Align(EscPosAlign align) => Raw(0x1B, (byte)'a', (byte)align);

    /// <summary><c>ESC E n</c> : gras.</summary>
    public void Bold(bool on) => Raw(0x1B, (byte)'E', on ? (byte)1 : (byte)0);

    /// <summary><c>GS ! n</c> : bit 0 = double hauteur, bit 4 = double largeur.</summary>
    public void Size(bool doubleHeight, bool doubleWidth)
    {
        byte n = (byte)((doubleHeight ? 0x01 : 0x00) | (doubleWidth ? 0x10 : 0x00));
        Raw(0x1D, (byte)'!', n);
    }

    public void Feed(int lines) => Raw(0x1B, (byte)'d', (byte)Math.Clamp(lines, 0, 255));

    /// <summary><c>GS V 66 n</c> : avance puis coupe partielle.</summary>
    public void Cut() => Raw(0x1D, (byte)'V', 66, 3);

    public void Text(string text) => Raw(_encoding.GetBytes(Normalize(text)));

    /// <summary>Texte suivi d'un saut de ligne ; tronqué à la largeur si trop long.</summary>
    public void Line(string text)
    {
        string normalized = Normalize(text);
        if (normalized.Length > Width) normalized = normalized[..Width];
        Text(normalized);
        Raw(0x0A);
    }

    /// <summary>Texte long réparti sur plusieurs lignes (retour à la ligne aux espaces).</summary>
    public void Wrapped(string text)
    {
        foreach (string line in WrapText(text, Width)) Line(line);
    }

    public void Separator(char c) => Line(new string(c, Width));

    /// <summary>Une ligne « libellé à gauche, valeur à droite » de <see cref="Width"/> caractères (la double hauteur garde les mêmes colonnes).</summary>
    public void Columns(string left, string right)
    {
        Raw(_encoding.GetBytes(FormatColumns(left, right, Width)));
        Raw(0x0A);
    }

    /// <summary>
    /// QR code natif <c>GS ( k</c> (modèle 2, module 6, correction M) : modèle, taille, niveau, données, impression.
    /// </summary>
    public void QrCode(string data)
    {
        byte[] payload = Encoding.ASCII.GetBytes(data);
        int length = payload.Length + 3;
        Raw(0x1D, (byte)'(', (byte)'k', 4, 0, 49, 65, 50, 0);                    // modèle 2
        Raw(0x1D, (byte)'(', (byte)'k', 3, 0, 49, 67, 6);                         // taille du module
        Raw(0x1D, (byte)'(', (byte)'k', 3, 0, 49, 69, 49);                        // correction d'erreur M
        Raw(0x1D, (byte)'(', (byte)'k', (byte)(length & 0xFF), (byte)(length >> 8), 49, 80, 48);
        Raw(payload);                                                            // données
        Raw(0x1D, (byte)'(', (byte)'k', 3, 0, 49, 81, 48);                        // impression
        Raw(0x0A);
    }

    /// <summary>Texte tel qu'imprimé : sans retours ni caractères exotiques, espaces insécables → espaces.</summary>
    public static string Normalize(string? text) =>
        (text ?? string.Empty)
            .Replace(' ', ' ').Replace(' ', ' ').Replace('’', '\'').Replace('‘', '\'')
            .Replace('–', '-').Replace('—', '-').Replace('…', '.')
            .Replace('\r', ' ').Replace('\n', ' ');

    /// <summary>« gauche ... droite » sur exactement <paramref name="width"/> caractères ; le libellé est tronqué si besoin.</summary>
    public static string FormatColumns(string left, string right, int width)
    {
        left = Normalize(left);
        right = Normalize(right);
        int room = width - right.Length - 1;
        if (room < 1)
        {
            right = right[..Math.Max(0, width - 2)];
            room = width - right.Length - 1;
        }

        if (left.Length > room) left = room > 1 ? left[..(room - 1)] + "." : left[..room];
        return left + new string(' ', width - left.Length - right.Length) + right;
    }

    /// <summary>Découpe aux espaces en lignes de <paramref name="width"/> caractères au plus (mot trop long coupé).</summary>
    public static IReadOnlyList<string> WrapText(string text, int width)
    {
        var lines = new List<string>();
        var current = new StringBuilder();
        foreach (string word in Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string piece = word;
            while (piece.Length > width)
            {
                if (current.Length > 0) { lines.Add(current.ToString()); current.Clear(); }
                lines.Add(piece[..width]);
                piece = piece[width..];
            }

            if (current.Length > 0 && current.Length + 1 + piece.Length > width)
            {
                lines.Add(current.ToString());
                current.Clear();
            }

            if (current.Length > 0) current.Append(' ');
            current.Append(piece);
        }

        if (current.Length > 0) lines.Add(current.ToString());
        return lines;
    }
}
