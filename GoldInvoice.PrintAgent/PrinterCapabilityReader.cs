using System.Runtime.InteropServices;

namespace GoldInvoice.PrintAgent;

internal static class PrinterCapabilityReader
{
    private const int DC_PAPERS = 2;
    private const int DC_PAPERNAMES = 16;
    private const int PAPER_NAME_CHARS = 64;

    public static PrinterCapabilities? Read(string printerName)
    {
        var paperCount = DeviceCapabilities(printerName, null, DC_PAPERS, IntPtr.Zero, IntPtr.Zero);
        var nameCount = DeviceCapabilities(printerName, null, DC_PAPERNAMES, IntPtr.Zero, IntPtr.Zero);
        if (paperCount <= 0 || nameCount <= 0)
            return new PrinterCapabilities(Array.Empty<string>());

        var namesBuffer = Marshal.AllocHGlobal(nameCount * PAPER_NAME_CHARS * sizeof(char));
        try
        {
            var result = DeviceCapabilities(printerName, null, DC_PAPERNAMES, namesBuffer, IntPtr.Zero);
            if (result <= 0)
                return new PrinterCapabilities(Array.Empty<string>());

            var names = new List<string>(paperCount);
            for (var i = 0; i < paperCount; i++)
            {
                var name = Marshal.PtrToStringUni(
                    namesBuffer + (i * PAPER_NAME_CHARS * sizeof(char)),
                    PAPER_NAME_CHARS)?.TrimEnd('\0', ' ');
                if (!string.IsNullOrWhiteSpace(name))
                    names.Add(name);
            }

            return new PrinterCapabilities(names);
        }
        finally
        {
            Marshal.FreeHGlobal(namesBuffer);
        }
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int DeviceCapabilities(
        string device,
        string? port,
        int capability,
        IntPtr output,
        IntPtr devmode);
}

internal sealed record PrinterCapabilities(IReadOnlyList<string> PaperSizes);
