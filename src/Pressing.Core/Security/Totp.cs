using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Pressing.Core.Security;

/// <summary>
/// Mots de passe à usage unique basés sur le temps (TOTP, RFC 6238) : HMAC-SHA1, pas de 30 s, 6 chiffres,
/// compatible avec Google Authenticator, Microsoft Authenticator, Aegis, etc. Aucune bibliothèque externe.
/// </summary>
public static class Totp
{
    public const int StepSeconds = 30;
    public const int Digits = 6;

    /// <summary>Nombre de pas acceptés avant et après le pas courant (décalage d'horloge toléré : ±30 s).</summary>
    public const int Window = 1;

    /// <summary>Numéro du pas de 30 s contenant <paramref name="time"/> (secondes Unix / 30).</summary>
    public static long StepOf(DateTimeOffset time) => time.ToUnixTimeSeconds() / StepSeconds;

    /// <summary>Nouveau secret de 160 bits (taille recommandée pour HMAC-SHA1), encodé en Base32.</summary>
    public static string GenerateSecret() => Base32.Encode(RandomNumberGenerator.GetBytes(20));

    /// <summary>Code HOTP (RFC 4226) d'un compteur donné, tronqué à <paramref name="digits"/> chiffres.</summary>
    /// <param name="algorithm">« SHA1 » (défaut, celui des applications), « SHA256 » ou « SHA512 » (vecteurs de la RFC 6238).</param>
    public static string ComputeForStep(byte[] key, long step, int digits = Digits, string algorithm = "SHA1")
    {
        Span<byte> counter = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, step);

        byte[] hash = algorithm switch
        {
            "SHA256" => HMACSHA256.HashData(key, counter),
            "SHA512" => HMACSHA512.HashData(key, counter),
            _ => HMACSHA1.HashData(key, counter)
        };

        int offset = hash[^1] & 0x0F;
        int binary =
            ((hash[offset] & 0x7F) << 24) |
            (hash[offset + 1] << 16) |
            (hash[offset + 2] << 8) |
            hash[offset + 3];

        int modulus = 1;
        for (int i = 0; i < digits; i++) modulus *= 10;
        return (binary % modulus).ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
    }

    /// <summary>Code attendu à l'instant <paramref name="time"/> pour un secret Base32.</summary>
    public static string Compute(string base32Secret, DateTimeOffset time) =>
        ComputeForStep(Base32.Decode(base32Secret), StepOf(time));

    /// <summary>
    /// Vérifie un code à 6 chiffres (espaces tolérés) dans la fenêtre ±<see cref="Window"/> pas.
    /// Renvoie le pas qui correspond (pour refuser un rejeu), ou null.
    /// </summary>
    public static long? Verify(string base32Secret, string? code, DateTimeOffset now)
    {
        string? clean = Normalize(code);
        if (clean is null)
        {
            return null;
        }

        byte[] key;
        try
        {
            key = Base32.Decode(base32Secret);
        }
        catch (FormatException)
        {
            return null;
        }

        long current = StepOf(now);
        long? match = null;
        // Toutes les positions sont évaluées (pas de sortie anticipée) : durée indépendante du résultat.
        for (long step = current - Window; step <= current + Window; step++)
        {
            string expected = ComputeForStep(key, step);
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(clean)))
            {
                match ??= step;
            }
        }

        return match;
    }

    /// <summary>Retire espaces et tirets ; renvoie null si ce n'est pas exactement 6 chiffres.</summary>
    public static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        string clean = new(code.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        return clean.Length == Digits && clean.All(char.IsAsciiDigit) ? clean : null;
    }

    /// <summary>
    /// URI « otpauth » lue par les applications d'authentification (et encodée en QR code).
    /// Le libellé est « Émetteur:compte » ; émetteur et compte sont encodés en pourcentage.
    /// </summary>
    public static string BuildUri(string issuer, string account, string base32Secret)
    {
        string i = Uri.EscapeDataString(issuer);
        string a = Uri.EscapeDataString(account);
        return $"otpauth://totp/{i}:{a}?secret={base32Secret}&issuer={i}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
    }
}

/// <summary>Base32 (RFC 4648, alphabet A–Z 2–7) sans remplissage, comme dans les secrets TOTP.</summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0;
        int bits = 0;
        foreach (byte b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return sb.ToString();
    }

    /// <summary>Décode (insensible à la casse ; espaces, tirets et « = » ignorés). FormatException si un caractère est invalide.</summary>
    public static byte[] Decode(string text)
    {
        var output = new List<byte>(text.Length * 5 / 8);
        int buffer = 0;
        int bits = 0;
        foreach (char raw in text)
        {
            if (char.IsWhiteSpace(raw) || raw is '-' or '=')
            {
                continue;
            }

            int value = Alphabet.IndexOf(char.ToUpperInvariant(raw));
            if (value < 0)
            {
                throw new FormatException("Secret Base32 invalide.");
            }

            buffer = ((buffer << 5) | value) & 0xFFFF;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return output.ToArray();
    }
}
