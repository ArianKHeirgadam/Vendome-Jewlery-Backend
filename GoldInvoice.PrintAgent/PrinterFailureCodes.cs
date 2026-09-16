namespace GoldInvoice.PrintAgent;

internal static class PrinterFailureCodes
{
    public const string PrinterUnavailable = "PRINTER_UNAVAILABLE";
    public const string PrinterOffline = "PRINTER_OFFLINE";
    public const string OutOfPaper = "OUT_OF_PAPER";
    public const string PrinterJam = "PRINTER_JAM";
    public const string PrintCancelled = "PRINT_CANCELLED";
    public const string UnsupportedPaperSize = "UNSUPPORTED_PAPER_SIZE";
    public const string GenericFailure = "GENERIC_FAILURE";

    public static bool IsAllowed(string? value) => value is
        PrinterUnavailable or PrinterOffline or OutOfPaper or PrinterJam or PrintCancelled or UnsupportedPaperSize or GenericFailure;
}
