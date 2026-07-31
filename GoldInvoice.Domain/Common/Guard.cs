namespace GoldInvoice.Domain.Common;

internal static class Guard
{
    public static void AgainstEmpty(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty identifier is required.", parameterName);
        }
    }

    public static string Required(string? value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"The value cannot exceed {maximumLength} characters.");
        }

        return normalized;
    }

    public static string? Optional(string? value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Required(value, parameterName, maximumLength);
    }

    public static void AgainstNegative(long value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The value cannot be negative.");
        }
    }

    public static void AgainstNegative(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The value cannot be negative.");
        }
    }

    public static void AgainstNegative(decimal value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The value cannot be negative.");
        }
    }

    public static void AgainstNonPositive(long value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The value must be positive.");
        }
    }

    public static void AgainstNonPositive(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The value must be positive.");
        }
    }

    public static void AgainstNonPositive(decimal value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The value must be positive.");
        }
    }

    public static void AgainstOutOfRange(int value, int minimum, int maximum, string parameterName)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(parameterName, $"The value must be between {minimum} and {maximum}.");
        }
    }

    public static void AgainstPercentage(decimal value, string parameterName)
    {
        if (value is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The percentage must be between 0 and 100.");
        }
    }

    public static void AgainstDefault(DateTimeOffset value, string parameterName)
    {
        if (value == default)
        {
            throw new ArgumentException("A date and time value is required.", parameterName);
        }
    }
}
