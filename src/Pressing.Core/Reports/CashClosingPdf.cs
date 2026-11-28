using System.Globalization;
using Pressing.Core.Entities;
using Pressing.Core.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Pressing.Core.Reports;

/// <summary>PDF du « Z de caisse », dans le même style que les reçus (A5, indigo, pied de page numéroté).</summary>
public static class CashClosingPdf
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    static CashClosingPdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] Generate(CashClosing closing, ShopInfo shop)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A5);
                page.Margin(22);
                page.DefaultTextStyle(style => style.FontSize(8.5f).FontColor(Colors.Grey.Darken4));

                page.Header().Element(header => ComposeHeader(header, shop, closing));
                page.Content().PaddingVertical(10).Element(content => ComposeContent(content, shop, closing));
                page.Footer().Element(footer => ComposeFooter(footer, shop));
            });
        }).GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, ShopInfo shop, CashClosing closing)
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

                row.ConstantItem(150).Column(right =>
                {
                    right.Item().AlignRight().Text("CLÔTURE DE CAISSE").FontSize(9).Bold().FontColor("#4f46e5");
                    right.Item().AlignRight().Text($"Z du {closing.BusinessDate:dd/MM/yyyy}").FontSize(14).Bold();
                    right.Item().AlignRight().Text(closing.Status.Label().ToUpperInvariant()).FontSize(9).Bold()
                        .FontColor(closing.Status == CashClosingStatus.Validated ? "#15803d" : "#b45309");
                });
            });

            column.Item().PaddingTop(8).LineHorizontal(1).LineColor("#c7d2fe");
        });
    }

    private static void ComposeContent(IContainer container, ShopInfo shop, CashClosing closing)
    {
        string currency = shop.Currency;
        container.Column(column =>
        {
            column.Item().Text("ENCAISSEMENTS ATTENDUS (NETS DES REMBOURSEMENTS)").FontSize(7).Bold().FontColor(Colors.Grey.Medium);
            column.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.ConstantColumn(110);
                });

                table.Header(header =>
                {
                    header.Cell().Background("#eef2ff").Padding(5).Text("Mode de paiement").FontSize(7.5f).Bold();
                    header.Cell().Background("#eef2ff").Padding(5).AlignRight().Text("Attendu").FontSize(7.5f).Bold();
                });

                Line(table, PaymentMethod.Cash.Label(), closing.ExpectedCash, currency);
                Line(table, PaymentMethod.MobileMoney.Label(), closing.ExpectedMobileMoney, currency);
                Line(table, PaymentMethod.Card.Label(), closing.ExpectedCard, currency);
                Line(table, PaymentMethod.BankTransfer.Label(), closing.ExpectedTransfer, currency);
                if (closing.ExpectedOther != 0)
                {
                    Line(table, PaymentMethod.Other.Label(), closing.ExpectedOther, currency);
                }

                table.Cell().Padding(5).Text("TOTAL ENCAISSÉ").Bold();
                table.Cell().Padding(5).AlignRight().Text(Money(closing.TotalExpected, currency)).Bold();
            });

            column.Item().PaddingTop(12).Text("TIROIR-CAISSE (ESPÈCES)").FontSize(7).Bold().FontColor(Colors.Grey.Medium);
            column.Item().PaddingTop(4).Column(cash =>
            {
                Row(cash, "Fond de caisse d'ouverture", closing.OpeningFloat, currency);
                Row(cash, "Espèces encaissées (nettes)", closing.ExpectedCash, currency);
                Row(cash, "Espèces attendues en caisse", closing.ExpectedCashInDrawer, currency, bold: true);
                Row(cash, "Espèces comptées", closing.CountedCash, currency, bold: true);
                cash.Item().PaddingTop(3).LineHorizontal(1).LineColor("#cbd5e1");
                string color = closing.Variance == 0 ? "#15803d" : closing.Variance < 0 ? "#b91c1c" : "#b45309";
                cash.Item().PaddingTop(3).Row(r =>
                {
                    r.RelativeItem().Text("ÉCART").Bold().FontSize(9).FontColor(color);
                    r.ConstantItem(110).AlignRight().Text(SignedMoney(closing.Variance, currency)).Bold().FontSize(10).FontColor(color);
                });
            });

            if (!string.IsNullOrWhiteSpace(closing.Comment))
            {
                column.Item().PaddingTop(10).Background("#f8fafc").Padding(8).Column(c =>
                {
                    c.Item().Text("COMMENTAIRE").FontSize(7).Bold().FontColor(Colors.Grey.Medium);
                    c.Item().Text(closing.Comment).FontSize(8.5f);
                });
            }

            column.Item().PaddingTop(14).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("ÉTABLI PAR").FontSize(7).Bold().FontColor(Colors.Grey.Medium);
                    c.Item().Text(closing.CreatedByName).Bold();
                    c.Item().Text(closing.CreatedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)).FontSize(7.5f);
                });
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("VALIDÉ PAR").FontSize(7).Bold().FontColor(Colors.Grey.Medium);
                    if (closing.Status == CashClosingStatus.Validated && closing.ValidatedAt.HasValue)
                    {
                        c.Item().Text(closing.ValidatedByName ?? "—").Bold();
                        c.Item().Text(closing.ValidatedAt.Value.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)).FontSize(7.5f);
                    }
                    else
                    {
                        c.Item().Text("En attente de validation").FontColor("#b45309");
                    }
                });
            });
        });
    }

    private static void ComposeFooter(IContainer container, ShopInfo shop)
    {
        container.Column(column =>
        {
            column.Item().Text($"Document généré le {DateTime.Now:dd/MM/yyyy HH:mm} · {shop.Name}")
                .FontSize(6.5f).FontColor(Colors.Grey.Medium);
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

    private static void Line(TableDescriptor table, string label, decimal value, string currency)
    {
        table.Cell().BorderBottom(1).BorderColor("#e2e8f0").Padding(5).Text(label);
        table.Cell().BorderBottom(1).BorderColor("#e2e8f0").Padding(5).AlignRight().Text(Money(value, currency));
    }

    private static void Row(ColumnDescriptor column, string label, decimal value, string currency, bool bold = false)
    {
        column.Item().Row(row =>
        {
            var left = row.RelativeItem().Text(label).FontSize(8.5f);
            var right = row.ConstantItem(110).AlignRight().Text(Money(value, currency)).FontSize(8.5f);
            if (bold)
            {
                left.Bold();
                right.Bold();
            }
        });
    }

    private static string Money(decimal value, string currency) =>
        value.ToString("N0", French) + " " + currency;

    private static string SignedMoney(decimal value, string currency) =>
        (value > 0 ? "+" : string.Empty) + Money(value, currency);
}
