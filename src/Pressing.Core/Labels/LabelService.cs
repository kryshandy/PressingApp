using Pressing.Core.Entities;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Pressing.Core.Labels;

/// <summary>Contenu d'une étiquette (une pièce d'un article).</summary>
public sealed record LabelInfo(
    string Code,
    string TicketNumber,
    string ClientName,
    string ItemName,
    string ServiceLabel,
    string PieceText,
    DateTime DueDate,
    bool IsExpress);

/// <summary>
/// Planche d'étiquettes A4 (3 colonnes × 8 lignes, ≈ 67 × 35,5 mm) : une étiquette par pièce d'article, avec un QR
/// contenant le code <see cref="ItemCodes"/>, le ticket, le client, l'article, le service et la date prévue.
/// </summary>
public class LabelService
{
    public const int Columns = 3;
    public const int RowsPerPage = 8;
    public const int LabelsPerPage = Columns * RowsPerPage;

    static LabelService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary>Une étiquette par pièce : une ligne de quantité 3 donne 3 étiquettes (-a, -b, -c).</summary>
    public static IReadOnlyList<LabelInfo> BuildLabels(Order order)
    {
        var labels = new List<LabelInfo>();
        foreach (OrderItem item in order.Items.Where(i => i.Status != ItemStatus.Cancelled).OrderBy(i => i.Id))
        {
            int rank = ItemCodes.RankOf(order, item);
            int pieces = Math.Max(1, item.Quantity);
            for (int piece = 1; piece <= pieces; piece++)
            {
                string? unit = pieces > 1 ? ItemCodes.UnitSuffix(piece) : null;
                labels.Add(new LabelInfo(
                    ItemCodes.Format(order.TicketNumber, rank, unit),
                    order.TicketNumber,
                    order.Client.FullName,
                    item.Category.Name,
                    item.ServiceType.Label() + (item.IsExpress ? " · Express" : string.Empty),
                    $"Pièce {piece}/{pieces}",
                    item.DueDate ?? order.DueDate,
                    item.IsExpress || order.IsExpress));
            }
        }

        return labels;
    }

    public byte[] GeneratePdf(Order order)
    {
        IReadOnlyList<LabelInfo> labels = BuildLabels(order);
        if (labels.Count == 0)
        {
            throw new InvalidOperationException("Cette commande n'a aucun article à étiqueter.");
        }

        int cells = (int)Math.Ceiling(labels.Count / (double)Columns) * Columns;
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(5, Unit.Millimetre);
                page.MarginVertical(5, Unit.Millimetre);
                page.DefaultTextStyle(style => style.FontSize(8).FontColor(Colors.Black));
                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        for (int c = 0; c < Columns; c++) columns.RelativeColumn();
                    });

                    for (int i = 0; i < cells; i++)
                    {
                        IContainer cell = table.Cell().Height(35.5f, Unit.Millimetre)
                            .Border(0.4f).BorderColor(Colors.Grey.Lighten1).Padding(6);
                        if (i < labels.Count) ComposeLabel(cell, labels[i]);
                    }
                });
            });
        }).GeneratePdf();
    }

    private static void ComposeLabel(IContainer cell, LabelInfo label)
    {
        cell.Row(row =>
        {
            row.ConstantItem(26, Unit.Millimetre).AlignMiddle().Image(GenerateQr(label.Code)).FitArea();

            row.RelativeItem().PaddingLeft(5).Column(col =>
            {
                col.Item().Text(label.TicketNumber).FontSize(9.5f).Bold();
                col.Item().Text(label.ClientName).FontSize(8.5f).SemiBold().ClampLines(1);
                col.Item().PaddingTop(2).Text(label.ItemName).FontSize(10).Bold().ClampLines(1);
                col.Item().Text(label.ServiceLabel).FontSize(8).ClampLines(1);
                col.Item().PaddingTop(2).Text($"Prévu le {label.DueDate:dd/MM/yyyy}").FontSize(8.5f).SemiBold();
                col.Item().Text($"{label.PieceText} · {label.Code}").FontSize(6.5f).FontColor(Colors.Grey.Darken2);
            });
        });
    }

    private static byte[] GenerateQr(string value)
    {
        using var generator = new QRCodeGenerator();
        using QRCodeData data = generator.CreateQrCode(value, QRCodeGenerator.ECCLevel.M);
        using var code = new PngByteQRCode(data);
        return code.GetGraphic(10);
    }
}
