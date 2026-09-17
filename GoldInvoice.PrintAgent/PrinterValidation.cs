namespace GoldInvoice.PrintAgent;

internal static class PrinterValidation
{
    public static string? Validate(string? requestedPrinterName, string? paperSize)
    {
        var printerName = PrinterDiagnostics.ResolvePrinterName(requestedPrinterName);
        if (string.IsNullOrWhiteSpace(printerName) || !WindowsPrinterInventory.Exists(printerName))
            return "PRINTER_UNAVAILABLE";

        var failure = PrinterDiagnostics.GetFailureCode(printerName);
        if (failure is not null)
            return failure;

        if (!string.IsNullOrWhiteSpace(paperSize))
        {
            var capabilities = PrinterCapabilityReader.Read(printerName);
            if (capabilities is not null && capabilities.PaperSizes.Count > 0 &&
                !capabilities.PaperSizes.Any(x => string.Equals(x, paperSize, StringComparison.OrdinalIgnoreCase)))
                return "UNSUPPORTED_PAPER_SIZE";
        }

        return null;
    }
}
