using System.Text;

namespace GoldInvoice.Infrastructure.Identity;

internal static class ContactIdentifierNormalizer
{
    public static string NormalizePhoneNumber(string value)
    {
        if (!TryNormalizePhoneNumber(value, out var normalized))
        {
            throw new ArgumentException("The phone number is invalid.", nameof(value));
        }

        return normalized;
    }

    public static bool TryNormalizePhoneNumber(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            if (character is '+' && builder.Length == 0)
            {
                builder.Append(character);
                continue;
            }

            var digit = character switch
            {
                >= '0' and <= '9' => character,
                >= '\u06F0' and <= '\u06F9' => (char)('0' + character - '\u06F0'),
                >= '\u0660' and <= '\u0669' => (char)('0' + character - '\u0660'),
                _ => '\0'
            };
            if (digit != '\0')
            {
                builder.Append(digit);
            }
            else if (character is not (' ' or '-' or '(' or ')'))
            {
                return false;
            }
        }

        var candidate = builder.ToString();
        if (candidate.Length == 0)
        {
            return false;
        }

        var digitCount = candidate[0] == '+' ? candidate.Length - 1 : candidate.Length;
        if (digitCount is < 7 or > 15)
        {
            return false;
        }

        normalized = candidate;
        return true;
    }
}
