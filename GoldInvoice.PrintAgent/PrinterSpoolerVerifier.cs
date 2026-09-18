using System.Runtime.InteropServices;

namespace GoldInvoice.PrintAgent;

internal static class PrinterSpoolerVerifier
{
    private const int PRINTER_STATUS_ERROR = 0x00000002;
    private const int PRINTER_STATUS_PAPER_JAM = 0x00000008;
    private const int PRINTER_STATUS_PAPER_OUT = 0x00000010;
    private const int PRINTER_STATUS_OFFLINE = 0x00000080;

    public static string? Check(string printerName)
    {
        if (!OpenPrinter(printerName, out var handle, IntPtr.Zero))
            return "PRINTER_UNAVAILABLE";
        try
        {
            var needed = 0;
            GetPrinter(handle, 6, IntPtr.Zero, 0, ref needed);
            if (needed <= 0) return null;
            var buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (!GetPrinter(handle, 6, buffer, needed, ref needed))
                    return "PRINTER_UNAVAILABLE";
                var info = Marshal.PtrToStructure<PRINTER_INFO_6>(buffer);
                if ((info.dwStatus & PRINTER_STATUS_PAPER_JAM) != 0) return "PRINTER_JAM";
                if ((info.dwStatus & PRINTER_STATUS_PAPER_OUT) != 0) return "OUT_OF_PAPER";
                if ((info.dwStatus & PRINTER_STATUS_OFFLINE) != 0) return "PRINTER_OFFLINE";
                if ((info.dwStatus & PRINTER_STATUS_ERROR) != 0) return "GENERIC_FAILURE";
                return null;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { ClosePrinter(handle); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct PRINTER_INFO_6 { public uint dwStatus; }
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool GetPrinter(IntPtr hPrinter, int level, IntPtr pPrinter, int cbBuf, ref int pcbNeeded);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool ClosePrinter(IntPtr hPrinter);
}
