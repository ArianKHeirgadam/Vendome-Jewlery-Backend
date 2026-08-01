namespace GoldInvoice.Infrastructure.Configuration;

public sealed class InvoicingOptions
{
    public const string SectionName = "Invoicing";

    public string SequenceSeries { get; init; } = "DEFAULT";

    public string SequencePrefix { get; init; } = "INV";

    public static bool IsValid(InvoicingOptions options) =>
        IsSafeIdentifier(options.SequenceSeries, 50) &&
        IsSafeIdentifier(options.SequencePrefix, 20);

    private static bool IsSafeIdentifier(string value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximumLength &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
