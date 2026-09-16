using System.Runtime.InteropServices;
using System.Text;

namespace GoldInvoice.PrintAgent;

internal static class WindowsPrinterInventory
{
    private const int ERROR_INSUFFICIENT_BUFFER = 122;
    private const int PRINTER_ENUM_LOCAL = 2;
    private const int PRINTER_ENUM_CONNECTIONS = 4;

    public static IReadOnlyList<PrinterInfo> Enumerate()
    {
        var flags = PRINTER_ENUM_LOCAL | PRINTER_ENUM_CONNECTIONS;
        var bytesNeeded = 0;
        var count = 0;
        EnumPrinters(flags, null, 2, IntPtr.Zero, 0, ref bytesNeeded, ref count);
        if (bytesNeeded <= 0 || count <= 0)
            return Array.Empty<PrinterInfo>();

        var buffer = Marshal.AllocHGlobal(bytesNeeded);
        try
        {
            if (!EnumPrinters(flags, null, 2, buffer, bytesNeeded, ref bytesNeeded, ref count))
                return Array.Empty<PrinterInfo>();

            var result = new List<PrinterInfo>(count);
            var size = Marshal.SizeOf<PRINTER_INFO_2>();
            for (var i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<PRINTER_INFO_2>(buffer + i * size);
                if (!string.IsNullOrWhiteSpace(info.pPrinterName))
                    result.Add(new PrinterInfo(info.pPrinterName, info.pPortName, info.pDriverName, info.Status));
            }
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static bool Exists(string printerName) =>
        Enumerate().Any(x => string.Equals(x.Name, printerName, StringComparison.OrdinalIgnoreCase));

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumPrinters(int flags, string? name, int level, IntPtr pPrinterEnum, int cbBuf, ref int pcbNeeded, ref int pcReturned);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PRINTER_INFO_2
    {
        public string? pServerName;
        public string? pPrinterName;
        public string? pShareName;
        public string? pPortName;
        public string? pDriverName;
        public string? pComment;
        public string? pLocation;
        public IntPtr pDevMode;
        public string? pSepFile;
        public string? pPrintProcessor;
        public string? pDatatype;
        public string? pParameters;
        public IntPtr pSecurityDescriptor;
        public uint Attributes;
        public uint Priority;
        public uint DefaultPriority;
        public uint StartTime;
        public uint UntilTime;
        public uint Status;
        public uint cJobs;
        public uint AveragePPM;
    }
}

internal sealed record PrinterInfo(string Name, string? PortName, string? DriverName, uint Status);
