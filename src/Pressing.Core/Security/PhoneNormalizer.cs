using System.Text;

namespace Pressing.Core.Security;

public static class PhoneNormalizer
{
    private const string CountryCode = "225";
    private const int NationalLength = 10;

    /// <summary>
    /// Forme canonique d'un numéro : uniquement des chiffres, sans préfixe international « 00 » ni indicatif
    /// ivoirien « 225 » devant un numéro national à 10 chiffres.
    /// « +225 05 55 66 77 88 », « 00225 0555667788 » et « 05 55 66 77 88 » donnent tous « 0555667788 ».
    /// </summary>
    public static string Normalize(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(phone.Length);
        foreach (char c in phone.Trim())
        {
            if (c is >= '0' and <= '9')
            {
                builder.Append(c);
            }
        }

        string digits = builder.ToString();
        if (digits.StartsWith("00", StringComparison.Ordinal))
        {
            digits = digits[2..];
        }

        if (digits.StartsWith(CountryCode, StringComparison.Ordinal) && digits.Length == CountryCode.Length + NationalLength)
        {
            digits = digits[CountryCode.Length..];
        }

        return digits;
    }
}
