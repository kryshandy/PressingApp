namespace Pressing.Core.Dtos;

/// <summary>Montants attendus d'un jour de caisse, calculés depuis les paiements et remboursements (rien n'est enregistré).</summary>
public record CashClosingPreviewDto(
    DateTime Date,
    decimal ExpectedCash,
    decimal ExpectedMobileMoney,
    decimal ExpectedCard,
    decimal ExpectedTransfer,
    decimal ExpectedOther,
    decimal TotalExpected,
    int PaymentsCount,
    int RefundsCount,
    int? ExistingClosingId,
    CashClosingStatus? ExistingStatus);

public record CashClosingDto(
    int Id,
    DateTime Date,
    decimal OpeningFloat,
    decimal ExpectedCash,
    decimal ExpectedMobileMoney,
    decimal ExpectedCard,
    decimal ExpectedTransfer,
    decimal ExpectedOther,
    decimal TotalExpected,
    decimal ExpectedCashInDrawer,
    decimal CountedCash,
    decimal Variance,
    string? Comment,
    CashClosingStatus Status,
    string CreatedBy,
    DateTime CreatedAt,
    string? ValidatedBy,
    DateTime? ValidatedAt);

/// <summary>Création ou mise à jour (brouillon uniquement) d'une clôture.</summary>
public record CashClosingRequest(
    DateTime Date,
    decimal OpeningFloat,
    decimal CountedCash,
    string? Comment);

public static class CashClosingMappings
{
    public static CashClosingDto ToDto(this Entities.CashClosing c) => new(
        c.Id, c.BusinessDate, c.OpeningFloat,
        c.ExpectedCash, c.ExpectedMobileMoney, c.ExpectedCard, c.ExpectedTransfer, c.ExpectedOther,
        c.TotalExpected, c.ExpectedCashInDrawer, c.CountedCash, c.Variance, c.Comment, c.Status,
        c.CreatedByName, c.CreatedAt, c.ValidatedByName, c.ValidatedAt);
}
