namespace GoldInvoice.PrintAgent;

internal static class PrinterAgentSelfCheck
{
    public static IReadOnlyList<string> GetInstalledPrinterNames() =>
        WindowsPrinterInventory.Enumerate().Select(x => x.Name).ToArray();

    public static string? ValidateConfiguredPrinter(string? printerName, string? paperSize) =>
        PrinterPreflight.Check(printerName, paperSize);
}
