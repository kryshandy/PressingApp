namespace Pressing.Core.Services;

/// <summary>Bornes de validation des saisies monétaires et des quantités (évite dépassements et incohérences).</summary>
public static class InputLimits
{
    public const decimal MaxAmount = 10_000_000m;
    public const int MaxQuantity = 1000;

    /// <summary>Montant dans [0, MaxAmount] ; lève une <see cref="ArgumentException"/> lisible sinon.</summary>
    public static void RequireAmountInRange(decimal value, string label)
    {
        if (value < 0 || value > MaxAmount)
        {
            throw new ArgumentException($"{label} : une valeur entre 0 et {MaxAmount:N0} est attendue.");
        }
    }

    /// <summary>Montant dans ]0, MaxAmount].</summary>
    public static void RequirePositiveAmount(decimal value, string label)
    {
        if (value <= 0 || value > MaxAmount)
        {
            throw new ArgumentException($"{label} : une valeur supérieure à 0 et au plus {MaxAmount:N0} est attendue.");
        }
    }
}
