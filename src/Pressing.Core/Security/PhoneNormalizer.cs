using System.Text;

namespace Pressing.Core.Security;

public static class PhoneNormalizer
{
    public static string Normalize(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(phone.Length);
        foreach (char c in phone.Trim())
        {
            if (char.IsDigit(c))
            {
                builder.Append(c);
            }
            else if (c == '+' && builder.Length == 0)
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
