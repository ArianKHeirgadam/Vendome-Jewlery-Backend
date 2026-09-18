using GoldInvoice.PrintAgent;

namespace GoldInvoice.UnitTests.PrintAgent;

public sealed class PrinterFailureCodesTests
{
    [Fact]
    public void Known_failure_codes_are_allowed()
    {
        Assert.True(PrinterFailureCodes.IsAllowed(PrinterFailureCodes.PrinterUnavailable));
        Assert.True(PrinterFailureCodes.IsAllowed(PrinterFailureCodes.PrinterOffline));
        Assert.True(PrinterFailureCodes.IsAllowed(PrinterFailureCodes.OutOfPaper));
        Assert.True(PrinterFailureCodes.IsAllowed(PrinterFailureCodes.PrinterJam));
        Assert.True(PrinterFailureCodes.IsAllowed(PrinterFailureCodes.PrintCancelled));
        Assert.True(PrinterFailureCodes.IsAllowed(PrinterFailureCodes.UnsupportedPaperSize));
        Assert.True(PrinterFailureCodes.IsAllowed(PrinterFailureCodes.GenericFailure));
    }

    [Fact]
    public void Unknown_failure_code_is_rejected()
    {
        Assert.False(PrinterFailureCodes.IsAllowed("UNKNOWN"));
    }
}
