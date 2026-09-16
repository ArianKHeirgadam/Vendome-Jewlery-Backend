using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Web;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace GoldInvoice.PrintAgent;

internal static class Program
{
    internal const string StateFile = "agent-state.json";

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        try
        {
            return args.Length > 0 && args[0].Equals("enroll", StringComparison.OrdinalIgnoreCase)
                ? await EnrollAsync(args[1..])
                : await RunAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Agent failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> EnrollAsync(string[] args)
    {
        var server = GetArg(args, "--server");
        var token = GetArg(args, "--token");
        var name = GetArg(args, "--name") ?? Environment.MachineName;

        if (server is null || token is null)
        {
            Console.WriteLine("Usage: GoldInvoice.PrintAgent enroll --server https://host --token <registration-token> [--name <display>]");
            return 2;
        }

        var state = AgentStateStore.LoadOrCreate();
        if (state.DoesCredentialExist())
        {
            Console.WriteLine("This machine is already enrolled.");
            return 0;
        }

        using var rsa = RSA.Create(2048);
        var publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();
        var identifierHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.MachineName.ToUpperInvariant())));

        using var client = new HttpClient { BaseAddress = new Uri(server.TrimEnd('/') + "/") };
        var response = await client.PostAsJsonAsync(
            "api/v1/devices/enroll",
            new Dictionary<string, string?>
            {
                ["registrationToken"] = token,
                ["deviceIdentifierHash"] = identifierHash,
                ["displayName"] = name,
                ["publicKeyPem"] = publicKeyPem
            });
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            Console.Error.WriteLine($"Enrollment rejected ({response.StatusCode}): {body}");
            return 1;
        }

        var enrolled = await response.Content.ReadFromJsonAsync<JsonElement>();
        var deviceId = enrolled.GetProperty("id").GetGuid();
        state.Save(new AgentState(
            server,
            deviceId,
            rsa.ExportRSAPrivateKeyPem(),
            rsa.ExportSubjectPublicKeyInfoPem()));
        Console.WriteLine($"Enrolled device {deviceId:N} on {server}. Run 'GoldInvoice.PrintAgent run' to start.");
        return 0;
    }

    private static async Task<int> RunAsync()
    {
        var state = AgentStateStore.LoadOrCreate().LoadRequired();
        if (state is null)
        {
            Console.Error.WriteLine("No enrollment found. Run 'GoldInvoice.PrintAgent enroll ...' first.");
            return 2;
        }

        Console.WriteLine($"Print agent running for device {state.DeviceId:N} against {state.Server}.");

        await using var printer = await InvoiceWebViewPrinter.CreateAsync();
        using var client = new DeviceAgentClient(state);
        var pollDelay = TimeSpan.FromSeconds(10);

        while (true)
        {
            try
            {
                var jobs = await client.GetPendingJobsAsync();
                if (jobs.Length > 0)
                {
                    Console.WriteLine($"Picked up {jobs.Length} pending print job(s).");
                }

                foreach (var job in jobs)
                {
                    await ProcessJobAsync(client, printer, job);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
            {
                Console.Error.WriteLine($"Poll failed: {ex.Message}");
            }

            await Task.Delay(pollDelay);
        }
    }

    private static async Task ProcessJobAsync(
        DeviceAgentClient client,
        InvoiceWebViewPrinter printer,
        PendingJob job)
    {
        try
        {
            var document = await client.GetDocumentAsync(job.JobId);
            var result = await printer.PrintAsync(
                document.Html,
                job.Copies,
                job.PrinterName,
                job.Profile);

            await client.CompleteAsync(
                job.JobId,
                succeeded: result.Succeeded,
                printerName: result.PrinterName,
                failureCode: result.Succeeded ? null : result.FailureCode);

            Console.WriteLine(
                result.Succeeded
                    ? $"Job {job.JobId:N} printed on {result.PrinterName}."
                    : $"Job {job.JobId:N} failed: {result.FailureCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Job {job.JobId:N} could not be processed: {ex.Message}");
        }
    }

    private static string? GetArg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name)
            {
                return args[i + 1];
            }
        }

        return null;
    }
}

internal sealed record AgentState(
    string Server,
    Guid DeviceId,
    string PrivateKeyPem,
    string PublicKeyPem);

internal static class AgentStateStore
{
    private static readonly string DirectoryPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VendomeJewelry",
            "Agent");

    private static string StateFilePath => Path.Combine(DirectoryPath, Program.StateFile);

    public static AgentStateStoreHandle LoadOrCreate()
    {
        Directory.CreateDirectory(DirectoryPath);
        return new AgentStateStoreHandle(StateFilePath);
    }

    internal sealed class AgentStateStoreHandle(string filePath)
    {
        public bool DoesCredentialExist() => File.Exists(filePath);

        public AgentState? LoadRequired()
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            var protectedBytes = File.ReadAllBytes(filePath);
            var json = Encoding.UTF8.GetString(ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser));
            return JsonSerializer.Deserialize<AgentState>(json);
        }

        public void Save(AgentState state)
        {
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(filePath, protectedBytes);
        }
    }
}

internal sealed class PendingJob
{
    public Guid JobId { get; init; }
    public Guid InvoiceId { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public int Copies { get; init; }
    public bool IsReprint { get; init; }
    public string? ReprintReason { get; init; }
    public Guid? DevicePrinterId { get; init; }
    public string? PrinterName { get; init; }
    public PrintProfileSettings? Profile { get; init; }
}

internal sealed class PrintProfileSettings
{
    public Guid? ProfileId { get; init; }
    public string? ProfileName { get; init; }
    public string? PaperSize { get; init; }
    public string? Orientation { get; init; }
    public int Copies { get; init; } = 1;
    public string? ColorMode { get; init; }
    public int MarginLeftMillimeters { get; init; }
    public int MarginRightMillimeters { get; init; }
    public int MarginTopMillimeters { get; init; }
    public int MarginBottomMillimeters { get; init; }
}

internal sealed class DeviceAgentClient(AgentState state) : IDisposable
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri(state.Server.TrimEnd('/') + "/") };

    public async Task<PendingJob[]> GetPendingJobsAsync()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var signature = Sign($"poll|{state.DeviceId:N}|{timestamp:o}");
        var query = $"api/v1/devices/{state.DeviceId:N}/print-jobs/pending?timestamp={HttpUtility.UrlEncode(timestamp.ToString("o"))}&signature={HttpUtility.UrlEncode(signature)}";
        var jobs = await _http.GetFromJsonAsync<JsonElement>(query);
        return jobs.EnumerateArray().Select(MapPendingJob).ToArray();
    }

    public async Task<PrintDocument> GetDocumentAsync(Guid jobId)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var signature = Sign($"document|{jobId:N}|{state.DeviceId:N}|{timestamp:o}");
        var query = $"api/v1/devices/{state.DeviceId:N}/print-jobs/{jobId:N}/document?timestamp={HttpUtility.UrlEncode(timestamp.ToString("o"))}&signature={HttpUtility.UrlEncode(signature)}";
        var payload = await _http.GetFromJsonAsync<JsonElement>(query);
        return new PrintDocument(payload.GetProperty("jobId").GetGuid(), payload.GetProperty("html").GetString() ?? string.Empty);
    }

    public async Task CompleteAsync(Guid jobId, bool succeeded, string? printerName, string? failureCode)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var payload = string.Join(
            "|",
            "complete",
            jobId.ToString("N"),
            state.DeviceId.ToString("N"),
            timestamp.ToString("o"),
            succeeded.ToString(),
            printerName ?? string.Empty,
            failureCode ?? string.Empty);
        var signature = Sign(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/v1/devices/print-jobs/{jobId:N}/complete")
        {
            Content = JsonContent.Create(new
            {
                timestamp,
                succeeded,
                printerName,
                failureCode,
                signature
            })
        };
        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Completion rejected ({response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
        }
    }

    public string Sign(string payload)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(state.PrivateKeyPem);
        return Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    public void Dispose() => _http.Dispose();

    private static PendingJob MapPendingJob(JsonElement element) => new()
    {
        JobId = element.GetProperty("jobId").GetGuid(),
        InvoiceId = element.GetProperty("invoiceId").GetGuid(),
        InvoiceNumber = element.GetProperty("invoiceNumber").GetString() ?? string.Empty,
        Copies = element.TryGetProperty("copies", out var copies) ? Math.Clamp(copies.GetInt32(), 1, 999) : 1,
        IsReprint = element.TryGetProperty("isReprint", out var isReprint) && isReprint.GetBoolean(),
        ReprintReason = element.TryGetProperty("reprintReason", out var reason) ? reason.GetString() : null,
        DevicePrinterId = element.TryGetProperty("devicePrinterId", out var printerId) && printerId.ValueKind != JsonValueKind.Null
            ? printerId.GetGuid()
            : null,
        PrinterName = element.TryGetProperty("printerName", out var printerName) ? printerName.GetString() : null,
        Profile = MapProfile(element)
    };

    private static PrintProfileSettings? MapProfile(JsonElement element)
    {
        if (!element.TryGetProperty("profile", out var profile) || profile.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return new PrintProfileSettings
        {
            ProfileId = profile.TryGetProperty("profileId", out var id) && id.ValueKind != JsonValueKind.Null ? id.GetGuid() : null,
            ProfileName = profile.TryGetProperty("profileName", out var name) ? name.GetString() : null,
            PaperSize = profile.TryGetProperty("paperSize", out var paperSize) ? paperSize.GetString() : null,
            Orientation = profile.TryGetProperty("orientation", out var orientation) ? orientation.GetString() : null,
            Copies = profile.TryGetProperty("copies", out var copies) ? Math.Clamp(copies.GetInt32(), 1, 999) : 1,
            ColorMode = profile.TryGetProperty("colorMode", out var colorMode) ? colorMode.GetString() : null,
            MarginLeftMillimeters = GetNonNegativeInt(profile, "marginLeftMillimeters"),
            MarginRightMillimeters = GetNonNegativeInt(profile, "marginRightMillimeters"),
            MarginTopMillimeters = GetNonNegativeInt(profile, "marginTopMillimeters"),
            MarginBottomMillimeters = GetNonNegativeInt(profile, "marginBottomMillimeters")
        };
    }

    private static int GetNonNegativeInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var number)
            ? Math.Max(0, number)
            : 0;
}

internal sealed record PrintDocument(Guid JobId, string Html);

internal sealed record PrintOutcome(bool Succeeded, string? PrinterName, string? FailureCode);

internal sealed class InvoiceWebViewPrinter : IAsyncDisposable
{
    private readonly CoreWebView2Environment _environment;
    private readonly WebView2WindowHost _host;

    private InvoiceWebViewPrinter(
        CoreWebView2Environment environment,
        WebView2WindowHost host)
    {
        _environment = environment;
        _host = host;
    }

    public static async Task<InvoiceWebViewPrinter> CreateAsync()
    {
        var environment = await CoreWebView2Environment.CreateAsync(
            userDataFolder: Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VendomeJewelry",
                "Agent",
                "WebView2"));
        var host = new WebView2WindowHost();
        var windowThread = new Thread(() =>
        {
            host.Initialize();
            Dispatcher.Run();
        });
        windowThread.SetApartmentState(ApartmentState.STA);
        windowThread.IsBackground = true;
        windowThread.Start();
        await host.Ready;
        return new InvoiceWebViewPrinter(environment, host);
    }

    public async Task<PrintOutcome> PrintAsync(
        string html,
        int copies,
        string? requestedPrinterName,
        PrintProfileSettings? profile)
    {
        var printerName = PrinterDiagnostics.ResolvePrinterName(requestedPrinterName);
        if (string.IsNullOrWhiteSpace(printerName))
        {
            return new PrintOutcome(false, null, "PRINTER_UNAVAILABLE");
        }

        var preflightFailure = PrinterDiagnostics.GetFailureCode(printerName);
        if (preflightFailure is not null)
        {
            return new PrintOutcome(false, printerName, preflightFailure);
        }

        await _host.NavigateToHtmlAsync(_environment, html);
        var effectiveCopies = Math.Clamp(copies > 0 ? copies : profile?.Copies ?? 1, 1, 999);

        CoreWebView2PrintStatus status;
        try
        {
            status = await _host.PrintAsync(
                effectiveCopies,
                printerName,
                profile,
                _environment);
        }
        catch (ArgumentException)
        {
            return new PrintOutcome(false, printerName, "GENERIC_FAILURE");
        }
        catch (COMException)
        {
            return new PrintOutcome(false, printerName, PrinterDiagnostics.GetFailureCode(printerName) ?? "GENERIC_FAILURE");
        }

        if (status == CoreWebView2PrintStatus.Succeeded)
        {
            return new PrintOutcome(true, printerName, null);
        }

        var failureCode = PrinterDiagnostics.GetFailureCode(printerName)
            ?? (status == CoreWebView2PrintStatus.PrinterUnavailable ? "PRINTER_UNAVAILABLE" : "GENERIC_FAILURE");
        return new PrintOutcome(false, printerName, failureCode);
    }

    public ValueTask DisposeAsync()
    {
        _host.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed class WebView2WindowHost : IDisposable
{
    private Window? _window;
    private Microsoft.Web.WebView2.Wpf.WebView2? _webView;
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Ready => _ready.Task;

    public void Initialize()
    {
        _window = new Window
        {
            Width = 1,
            Height = 1,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            Opacity = 0
        };
        _webView = new Microsoft.Web.WebView2.Wpf.WebView2();
        _window.Content = _webView;
        _window.Closed += (_, _) => Dispatcher.CurrentDispatcher.InvokeShutdown();
        _window.Show();
        _ready.TrySetResult(true);
    }

    public async Task NavigateToHtmlAsync(CoreWebView2Environment environment, string html)
    {
        await _webView!.EnsureCoreWebView2Async(environment);
        _webView.CoreWebView2.Settings.IsScriptEnabled = false;
        _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

        var navigation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs eventArgs)
        {
            if (!string.Equals(eventArgs.Uri, "about:blank", StringComparison.OrdinalIgnoreCase))
            {
                eventArgs.Cancel = true;
            }
        }

        void NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs eventArgs)
        {
            if (eventArgs.IsSuccess)
            {
                navigation.TrySetResult(true);
            }
            else
            {
                navigation.TrySetException(new InvalidOperationException($"Document navigation failed: {eventArgs.WebErrorStatus}"));
            }
        }

        _webView.CoreWebView2.NavigationStarting += NavigationStarting;
        _webView.NavigationCompleted += NavigationCompleted;
        try
        {
            _webView.NavigateToString(html);
            await navigation.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            _webView.CoreWebView2.NavigationStarting -= NavigationStarting;
            _webView.NavigationCompleted -= NavigationCompleted;
        }
    }

    public async Task<CoreWebView2PrintStatus> PrintAsync(
        int copies,
        string printerName,
        PrintProfileSettings? profile,
        CoreWebView2Environment environment)
    {
        if (_webView?.CoreWebView2 is null)
        {
            return CoreWebView2PrintStatus.PrinterUnavailable;
        }

        var settings = environment.CreatePrintSettings();
        settings.PrinterName = printerName;
        settings.Copies = Math.Clamp(copies, 1, 999);
        settings.ShouldPrintBackgrounds = true;
        settings.ShouldPrintHeaderAndFooter = false;
        settings.ShouldPrintSelectionOnly = false;

        ApplyProfile(settings, profile);
        return await _webView.CoreWebView2.PrintAsync(settings);
    }

    private static void ApplyProfile(CoreWebView2PrintSettings settings, PrintProfileSettings? profile)
    {
        if (profile is null)
        {
            return;
        }

        settings.Orientation = string.Equals(profile.Orientation, "Landscape", StringComparison.OrdinalIgnoreCase)
            ? CoreWebView2PrintOrientation.Landscape
            : CoreWebView2PrintOrientation.Portrait;

        settings.ColorMode = profile.ColorMode?.ToLowerInvariant() switch
        {
            "color" => CoreWebView2PrintColorMode.Color,
            "grayscale" or "greyscale" or "blackandwhite" or "black_and_white" => CoreWebView2PrintColorMode.Grayscale,
            _ => CoreWebView2PrintColorMode.Default
        };

        settings.MarginLeft = Math.Max(0, profile.MarginLeftMillimeters) / 25.4;
        settings.MarginRight = Math.Max(0, profile.MarginRightMillimeters) / 25.4;
        settings.MarginTop = Math.Max(0, profile.MarginTopMillimeters) / 25.4;
        settings.MarginBottom = Math.Max(0, profile.MarginBottomMillimeters) / 25.4;

        if (TryGetPaperDimensions(profile.PaperSize, out var widthMm, out var heightMm))
        {
            settings.MediaSize = CoreWebView2PrintMediaSize.Custom;
            settings.PageWidth = widthMm / 25.4;
            settings.PageHeight = heightMm / 25.4;
        }
    }

    private static bool TryGetPaperDimensions(string? paperSize, out double widthMm, out double heightMm)
    {
        widthMm = 0;
        heightMm = 0;
        var value = paperSize?.Replace(" ", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty).ToUpperInvariant();
        (widthMm, heightMm) = value switch
        {
            "A3" => (297, 420),
            "A4" => (210, 297),
            "A5" => (148, 210),
            "A6" => (105, 148),
            "B5" => (176, 250),
            "LETTER" => (215.9, 279.4),
            "LEGAL" => (215.9, 355.6),
            "TABLOID" => (279.4, 431.8),
            "EXECUTIVE" => (184.15, 266.7),
            "RECEIPT58MM" or "THERMAL58MM" => (58, 200),
            "RECEIPT80MM" or "THERMAL80MM" => (80, 200),
            _ => (0, 0)
        };
        return widthMm > 0 && heightMm > 0;
    }

    public void Dispose()
    {
        _window?.Close();
        _webView?.Dispose();
        _window = null;
        _webView = null;
    }
}

internal static class PrinterDiagnostics
{
    private const uint PrinterStatusError = 0x00000002;
    private const uint PrinterStatusPaperJam = 0x00000008;
    private const uint PrinterStatusPaperOut = 0x00000010;
    private const uint PrinterStatusPaperProblem = 0x00000040;
    private const uint PrinterStatusOffline = 0x00000080;
    private const uint PrinterStatusNotAvailable = 0x00001000;
    private const uint PrinterStatusNoToner = 0x00040000;
    private const uint PrinterStatusTonerLow = 0x00020000;

    public static string? ResolvePrinterName(string? requestedName)
    {
        if (!string.IsNullOrWhiteSpace(requestedName))
        {
            return requestedName.Trim();
        }

        var capacity = 0u;
        GetDefaultPrinter(null, ref capacity);
        if (capacity == 0)
        {
            return null;
        }

        var buffer = new StringBuilder((int)capacity);
        return GetDefaultPrinter(buffer, ref capacity) ? buffer.ToString() : null;
    }

    public static string? GetFailureCode(string printerName)
    {
        if (!OpenPrinter(printerName, out var handle, IntPtr.Zero))
        {
            return "PRINTER_UNAVAILABLE";
        }

        try
        {
            var needed = 0u;
            GetPrinter(handle, 6, IntPtr.Zero, 0, ref needed);
            if (needed == 0)
            {
                return null;
            }

            var buffer = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!GetPrinter(handle, 6, buffer, needed, ref needed))
                {
                    return null;
                }

                var info = Marshal.PtrToStructure<PrinterInfo6>(buffer);
                var status = info.Status;
                if ((status & PrinterStatusPaperJam) != 0)
                {
                    return "PRINTER_JAM";
                }
                if ((status & (PrinterStatusPaperOut | PrinterStatusPaperProblem)) != 0)
                {
                    return "OUT_OF_PAPER";
                }
                if ((status & PrinterStatusOffline) != 0)
                {
                    return "PRINTER_OFFLINE";
                }
                if ((status & PrinterStatusNotAvailable) != 0)
                {
                    return "PRINTER_UNAVAILABLE";
                }
                if ((status & (PrinterStatusError | PrinterStatusNoToner)) != 0)
                {
                    return "GENERIC_FAILURE";
                }
                _ = PrinterStatusTonerLow;
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            ClosePrinter(handle);
        }
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetDefaultPrinter(StringBuilder? pszBuffer, ref uint pcchBuffer);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool GetPrinter(
        IntPtr hPrinter,
        uint level,
        IntPtr pPrinter,
        uint cbBuf,
        ref uint pcbNeeded);

    [StructLayout(LayoutKind.Sequential)]
    private struct PrinterInfo6
    {
        public uint Status;
    }
}
