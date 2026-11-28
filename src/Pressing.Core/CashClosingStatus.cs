namespace Pressing.Core;

/// <summary>Statut d'une clôture de caisse : un brouillon est modifiable, une clôture validée est figée.</summary>
public enum CashClosingStatus
{
    Draft = 0,
    Validated = 1
}

public static class CashClosingStatusLabels
{
    public static string Label(this CashClosingStatus value) => value switch
    {
        CashClosingStatus.Draft => "Brouillon",
        CashClosingStatus.Validated => "Validée",
        _ => value.ToString()
    };
}
