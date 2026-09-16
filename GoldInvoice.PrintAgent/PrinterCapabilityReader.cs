using System.Runtime.InteropServices;
using System.Text;

namespace GoldInvoice.PrintAgent;

internal static class PrinterCapabilityReader
{
    private const int DC_PAPERS = 2;
    private const int DC_PAPERSIZE = 3;
    private const int DC_PAPERNAMES = 16;

    public static PrinterCapabilities? Read(string printerName)
    {
        var paperCount = DeviceCapabilities(printerName, null, DC_PAPERS, null, IntPtr.Zero);
        var nameChars = DeviceCapabilities(printerName, null, DC_PAPERNAMES, null, IntPtr.Zero);
        if (paperCount <= 0 || nameChars <= 0)
            return new PrinterCapabilities(Array.Empty<string>());

        var namesBuffer = Marshal.AllocHGlobal(nameChars * 64 * 2);
        try
        {
            var result = DeviceCapabilities(printerName, null, DC_PAPERNAMES, namesBuffer, IntPtr.Zero);
            if (result <= 0)
                return new PrinterCapabilities(Array.Empty<string>());

            var names = new List<string>();
            for (var i = 0; i < paperCount; i++)
            {
                var name = Marshal.PtrToStringUni(namesBuffer + i * 64 * 2, 64)?.TrimEnd('\0', ' ');
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
    private static extern int DeviceCapabilities(string device, string? port, int capability, IntPtr output, IntPtr devmode);
}

internal sealed record PrinterCapabilities(IReadOnlyList<string> PaperSizes);
