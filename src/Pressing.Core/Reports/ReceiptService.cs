using System.Globalization;
using Pressing.Core.Entities;
using Pressing.Core.Options;
using Pressing.Core.Services;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Pressing.Core.Reports;

public class ReceiptService
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");
    private readonly SettingsService _settings;
    private readonly EmailOptions _options;

    static ReceiptService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public ReceiptService(SettingsService settings, EmailOptions options)
    {
        _settings = settings;
        _options = options;
    }

    public async Task<byte[]> GeneratePdfAsync(Order order, CancellationToken ct = default)
    {
        ShopInfo shop = await _settings.GetShopInfoAsync(ct);
        return GeneratePdf(order, shop);
    }

    public byte[] GeneratePdf(Order order, ShopInfo shop)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A5);
                page.Margin(22);
                page.DefaultTextStyle(style => style.FontSize(8.5f).FontColor(Colors.Grey.Darken4));

                page.Header().Element(header => ComposeHeader(header, shop, order));
                page.Content().PaddingVertical(10).Element(content => ComposeContent(content, shop, order));
                page.Footer().Element(footer => ComposeFooter(footer, shop, order));
            });
        }).GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, ShopInfo shop, Order order)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text(shop.Name).FontSize(22).Bold().FontColor("#312e81");
                    left.Item().Text(shop.Slogan).FontSize(8).FontColor(Colors.Grey.Medium);
                    left.Item().PaddingTop(6).Text($"{shop.Address}, {shop.City}").FontSize(8);
                    left.Item().Text($"{shop.Phone} · {shop.Email}").FontSize(8);
                });

                row.ConstantItem(150).AlignRight().Column(right =>
                {
                    right.Item().AlignRight().Text("REÇU / TICKET").FontSize(9).Bold().FontColor("#4f46e5");
                    right.Item().AlignRight().Text(order.TicketNumber).FontSize(14).Bold();
                    right.Item().AlignRight().Text($"Déposé le {order.CreatedAt:dd/MM/yyyy HH:mm}").FontSize(8);
                    right.Item().AlignRight().Text($"Retrait prévu le {order.DueDate:dd/MM/yyyy}").FontSize(8);
                    if (order.IsExpress)
                    {
                        right.Item().AlignRight().PaddingTop(4).Text("EXPRESS").FontSize(9).Bold().FontColor("#0ea5e9");
                    }
                });
            });

            column.Item().PaddingTop(8).LineHorizontal(1).LineColor("#c7d2fe");
        });
    }

    private static void ComposeContent(IContainer container, ShopInfo shop, Order order)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(client =>
                {
                    client.Item().Text("CLIENT").FontSize(7).Bold().FontColor(Colors.Grey.Medium);
                    client.Item().Text(order.Client.FullName).FontSize(11).Bold();
                    client.Item().Text(order.Client.Phone).FontSize(8);
                    if (!string.IsNullOrWhiteSpace(order.Client.Email))
                    {
                        client.Item().Text(order.Client.Email).FontSize(8);
                    }
                });

                row.ConstantItem(190).Column(meta =>
                {
                    meta.Item().Text("STATUT").FontSize(7).Bold().FontColor(Colors.Grey.Medium);
                    meta.Item().Text(order.Status.Label()).FontSize(10).Bold();
                    if (!string.IsNullOrWhiteSpace(order.Notes))
                    {
                        meta.Item().Text(order.Notes).FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                    }
                });
            });

            column.Item().PaddingTop(10).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(4);
                    columns.ConstantColumn(28);
                    columns.ConstantColumn(58);
                    columns.ConstantColumn(58);
                });

                table.Header(header =>
                {
                    header.Cell().Background("#eef2ff").Padding(5).Text("Article").FontSize(7.5f).Bold();
                    header.Cell().Background("#eef2ff").Padding(5).AlignCenter().Text("Qté").FontSize(7.5f).Bold();
                    header.Cell().Background("#eef2ff").Padding(5).AlignRight().Text("P.U.").FontSize(7.5f).Bold();
                    header.Cell().Background("#eef2ff").Padding(5).AlignRight().Text("Total").FontSize(7.5f).Bold();
                });

                foreach (OrderItem item in order.Items)
                {
                    string label = item.Category.Name;
                    string service = item.ServiceType.Label() + (item.IsExpress ? " · Express" : string.Empty);

                    table.Cell().BorderBottom(1).BorderColor("#e2e8f0").Padding(5).Column(c =>
                    {
                        c.Item().Text(label).FontSize(8).Bold();
                        c.Item().Text(service).FontSize(7).FontColor(Colors.Grey.Medium);
                    });
                    table.Cell().BorderBottom(1).BorderColor("#e2e8f0").Padding(5).AlignCenter().Text(item.Quantity.ToString());
                    table.Cell().BorderBottom(1).BorderColor("#e2e8f0").Padding(5).AlignRight().Text(Money(item.UnitPrice, shop.Currency));
                    table.Cell().BorderBottom(1).BorderColor("#e2e8f0").Padding(5).AlignRight().Text(Money(item.LineTotal, shop.Currency)).Bold();
                }
            });

            column.Item().PaddingTop(10).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("PAIEMENTS").FontSize(7).Bold().FontColor(Colors.Grey.Medium);
                    if (order.Payments.Count == 0)
                    {
                        left.Item().Text("Aucun paiement enregistré.").FontSize(7.5f).FontColor(Colors.Grey.Medium);
                    }
                    else
                    {
                        foreach (Payment payment in order.Payments.OrderBy(p => p.PaidAt))
                        {
                            left.Item().Text(PaymentLine(payment, shop.Currency)).FontSize(7.5f);
                        }
                    }
                });

                row.ConstantItem(210).Column(totals =>
                {
                    TotalRow(totals, "Sous-total", order.Subtotal, shop.Currency);
                    if (order.ExpressTotal > 0) TotalRow(totals, "Supplément express", order.ExpressTotal, shop.Currency);
                    if (order.DeliveryFee > 0) TotalRow(totals, "Livraison", order.DeliveryFee, shop.Currency);
                    if (order.DiscountAmount > 0) TotalRow(totals, "Remise", -order.DiscountAmount, shop.Currency);
                    if (order.PromotionDiscount > 0) TotalRow(totals, $"Promotion {order.PromotionCode}", -order.PromotionDiscount, shop.Currency);
                    if (order.LoyaltyDiscount > 0) TotalRow(totals, "Fidélité", -order.LoyaltyDiscount, shop.Currency);
                    totals.Item().PaddingTop(3).LineHorizontal(1).LineColor("#cbd5e1");
                    totals.Item().PaddingTop(3).Row(r =>
                    {
                        r.RelativeItem().Text("TOTAL").Bold().FontSize(9);
                        r.ConstantItem(80).AlignRight().Text(Money(order.Total, shop.Currency)).Bold().FontSize(10);
                    });
                    TotalRow(totals, "Payé (net)", NetPaid(order), shop.Currency);
                    totals.Item().Row(r =>
                    {
                        r.RelativeItem().Text("SOLDE").Bold().FontSize(8.5f).FontColor(order.Balance > 0 ? "#b45309" : "#15803d");
                        r.ConstantItem(80).AlignRight().Text(Money(order.Balance, shop.Currency)).Bold().FontColor(order.Balance > 0 ? "#b45309" : "#15803d");
                    });
                });
            });

            if (!string.IsNullOrWhiteSpace(order.CancelledAt is null ? null : order.CancelReason))
            {
                column.Item().PaddingTop(10).Background("#fef2f2").Padding(8).Text($"Commande annulée : {order.CancelReason}").FontColor("#b91c1c").FontSize(8);
            }
        });
    }

    private static void ComposeFooter(IContainer container, ShopInfo shop, Order order)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(info =>
                {
                    info.Item().Text(shop.ReceiptFooter).FontSize(7).FontColor(Colors.Grey.Darken1);
                    info.Item().Text($"Horaires : {shop.OpeningHours} · {shop.Phone}").FontSize(7).FontColor(Colors.Grey.Medium);
                    info.Item().PaddingTop(4).Text($"Document généré le {DateTime.Now:dd/MM/yyyy HH:mm}").FontSize(6.5f).FontColor(Colors.Grey.Medium);
                });

                row.ConstantItem(58).AlignRight().Column(qr =>
                {
                    byte[] image = GenerateQr($"TICKET:{order.TicketNumber}|CLIENT:{order.Client.Phone}");
                    qr.Item().Width(48).Image(image);
                    qr.Item().AlignCenter().Text("Suivi").FontSize(6).FontColor(Colors.Grey.Medium);
                });
            });

            column.Item().PaddingTop(4).AlignCenter().Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(6.5f).FontColor(Colors.Grey.Medium));
                text.Span("Page ");
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
            });
        });
    }

    /// <summary>Ligne de paiement ; un remboursement est libellé « Remboursement » avec un montant négatif.</summary>
    public static string PaymentLine(Payment payment, string currency)
    {
        string when = payment.PaidAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        return payment.Type == PaymentType.Refund
            ? $"{when} · Remboursement · {payment.Method.Label()} · {Money(-payment.Amount, currency)}"
            : $"{when} · {payment.Method.Label()} · {Money(payment.Amount, currency)}";
    }

    /// <summary>Total payé net des remboursements.</summary>
    public static decimal NetPaid(Order order) =>
        order.Payments.Sum(p => p.Type == PaymentType.Refund ? -p.Amount : p.Amount);

    private static void TotalRow(ColumnDescriptor column, string label, decimal value, string currency)
    {
        column.Item().Row(row =>
        {
            row.RelativeItem().Text(label).FontSize(8);
            row.ConstantItem(80).AlignRight().Text(Money(value, currency)).FontSize(8);
        });
    }

    private static string Money(decimal value, string currency) =>
        value.ToString("N0", French) + " " + currency;

    private static byte[] GenerateQr(string value)
    {
        using var generator = new QRCodeGenerator();
        using QRCodeData data = generator.CreateQrCode(value, QRCodeGenerator.ECCLevel.Q);
        using var code = new PngByteQRCode(data);
        return code.GetGraphic(8);
    }
}
