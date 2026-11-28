using System.ComponentModel.DataAnnotations;

namespace Pressing.Core.Entities;

/// <summary>
/// Clôture de caisse journalière (« Z de caisse »). Les montants attendus sont figés à l'enregistrement
/// (et recalculés à la validation) ; une clôture validée n'est plus jamais modifiable.
/// </summary>
public class CashClosing
{
    public int Id { get; set; }

    /// <summary>Jour de caisse (partie date uniquement).</summary>
    public DateTime BusinessDate { get; set; }

    /// <summary>Fond de caisse d'ouverture (espèces présentes en début de journée).</summary>
    public decimal OpeningFloat { get; set; }

    /// <summary>Encaissements espèces nets des remboursements du jour (hors fond de caisse).</summary>
    public decimal ExpectedCash { get; set; }

    public decimal ExpectedMobileMoney { get; set; }

    public decimal ExpectedCard { get; set; }

    public decimal ExpectedTransfer { get; set; }

    /// <summary>Autres modes de paiement (net), pour que le total du Z soit complet.</summary>
    public decimal ExpectedOther { get; set; }

    /// <summary>Espèces comptées dans le tiroir-caisse (fond de caisse inclus).</summary>
    public decimal CountedCash { get; set; }

    /// <summary>Écart = espèces comptées − (fond de caisse + espèces attendues).</summary>
    public decimal Variance { get; set; }

    [MaxLength(500)]
    public string? Comment { get; set; }

    public CashClosingStatus Status { get; set; } = CashClosingStatus.Draft;

    public int? CreatedByStaffId { get; set; }

    [MaxLength(120)]
    public string CreatedByName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public int? ValidatedByStaffId { get; set; }

    [MaxLength(120)]
    public string? ValidatedByName { get; set; }

    public DateTime? ValidatedAt { get; set; }

    /// <summary>Espèces attendues dans le tiroir : fond de caisse + espèces nettes du jour.</summary>
    public decimal ExpectedCashInDrawer => OpeningFloat + ExpectedCash;

    public decimal TotalExpected => ExpectedCash + ExpectedMobileMoney + ExpectedCard + ExpectedTransfer + ExpectedOther;
}
