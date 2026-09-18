namespace GoldInvoice.PrintAgent;

internal static class PrinterPreflight
{
    public static string? Check(string? requestedPrinterName, string? paperSize)
    {
        var name = PrinterDiagnostics.ResolvePrinterName(requestedPrinterName);
        if (string.IsNullOrWhiteSpace(name) || !WindowsPrinterInventory.Exists(name))
            return "PRINTER_UNAVAILABLE";

        return PrinterSpoolerVerifier.Check(name) ??
               PrinterValidation.Validate(name, paperSize);
    }
}
