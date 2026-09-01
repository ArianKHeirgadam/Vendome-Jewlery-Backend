using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using VendomeJewleryDesktopApp.Services;

namespace VendomeJewleryDesktopApp;

public partial class MainWindow : Window
{
    private const string AppHostName = "desktop.zarnom.invalid";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly DesktopSettingsStore _settingsStore;
    private readonly DesktopSecretStore _secretStore;
    private readonly DesktopAuthBroker _authBroker;
    private readonly LocalDeviceDetectionAgent _deviceDetectionAgent;
    private bool _initializationInProgress;
    private bool _isFullScreen = true;

    public MainWindow()
    {
        InitializeComponent();
        string applicationDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VendomeJewelry");
        _settingsStore = new DesktopSettingsStore(applicationDataDirectory);
        _secretStore = new DesktopSecretStore(applicationDataDirectory);
        _authBroker = new DesktopAuthBroker(_settingsStore, _secretStore);
        _deviceDetectionAgent = new LocalDeviceDetectionAgent(() => _authBroker.CurrentAccessToken, () => _settingsStore.ApiBaseUrl);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _deviceDetectionAgent.Start();
        await InitializeDashboardAsync();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _deviceDetectionAgent.Dispose();
        _authBroker.Dispose();
        DashboardWebView.Dispose();
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e) => await InitializeDashboardAsync();

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11) { SetFullScreen(!_isFullScreen); e.Handled = true; return; }
        if (e.Key == Key.Escape && _isFullScreen) { SetFullScreen(false); e.Handled = true; }
    }

    private void SetFullScreen(bool enabled)
    {
        WindowState = WindowState.Normal;
        if (enabled)
        {
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowState = WindowState.Maximized;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow; ResizeMode = ResizeMode.CanResize;
            Width = Math.Min(1440, SystemParameters.WorkArea.Width); Height = Math.Min(900, SystemParameters.WorkArea.Height);
            Left = SystemParameters.WorkArea.Left + Math.Max(0, (SystemParameters.WorkArea.Width - Width) / 2);
            Top = SystemParameters.WorkArea.Top + Math.Max(0, (SystemParameters.WorkArea.Height - Height) / 2);
        }
        _isFullScreen = enabled;
    }

    private async Task InitializeDashboardAsync()
    {
        if (_initializationInProgress) return;
        _initializationInProgress = true; ShowLoadingState();
        try
        {
            string clientDirectory = Path.Combine(AppContext.BaseDirectory, "ClientApp", "dist");
            string entryPoint = Path.Combine(clientDirectory, "index.html");
            if (!File.Exists(entryPoint))
            {
                ShowStartupError("فایل‌های رابط React پیدا نشد.", "داخل GoldInvoice.Client دستور npm ci و npm run build را اجرا کن، سپس Rebuild Solution بزن.");
                return;
            }
            if (DashboardWebView.CoreWebView2 is null)
            {
                string userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VendomeJewelry", "WebView2");
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
                await DashboardWebView.EnsureCoreWebView2Async(environment);
                CoreWebView2 initializedWebView = DashboardWebView.CoreWebView2 ?? throw new InvalidOperationException("WebView2 initialization completed without a CoreWebView2 instance.");
                ConfigureWebView(initializedWebView, clientDirectory);
            }
            CoreWebView2 webView = DashboardWebView.CoreWebView2 ?? throw new InvalidOperationException("WebView2 is not initialized.");
            webView.Navigate($"https://{AppHostName}/index.html#/dashboard");
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowStartupError("Microsoft Edge WebView2 Runtime نصب نیست.", "WebView2 Runtime را نصب کن و برنامه را دوباره اجرا کن.");
        }
        catch (Exception)
        {
            ShowStartupError("برنامه اجرا نشد.", "فایل‌های React، WebView2 Runtime و دسترسی پوشهٔ LocalAppData را بررسی کن.");
        }
        finally { _initializationInProgress = false; }
    }

    private void ConfigureWebView(CoreWebView2 webView, string clientDirectory)
    {
        webView.SetVirtualHostNameToFolderMapping(AppHostName, clientDirectory, CoreWebView2HostResourceAccessKind.DenyCors);
        webView.Settings.AreDefaultContextMenusEnabled = false; webView.Settings.IsStatusBarEnabled = false; webView.Settings.IsZoomControlEnabled = false;
#if DEBUG
        webView.Settings.AreDevToolsEnabled = true;
#else
        webView.Settings.AreDevToolsEnabled = false;
#endif
        webView.NavigationStarting += WebView_NavigationStarting;
        webView.NavigationCompleted += WebView_NavigationCompleted;
        webView.NewWindowRequested += WebView_NewWindowRequested;
        webView.WebMessageReceived += WebView_WebMessageReceived;
    }

    private static void WebView_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? uri) || !string.Equals(uri.Host, AppHostName, StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
    }

    private void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess) { StartupPanel.Visibility = Visibility.Collapsed; return; }
        ShowStartupError("بارگذاری رابط کامل نشد.", $"WebView2 نتوانست رابط محلی را بارگذاری کند: {e.WebErrorStatus}");
    }

    private static void WebView_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e) => e.Handled = true;

    private async void WebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!Uri.TryCreate(e.Source, UriKind.Absolute, out Uri? source) || !string.Equals(source.Host, AppHostName, StringComparison.OrdinalIgnoreCase)) return;
        string? requestId = null;
        try
        {
            string requestJson = e.WebMessageAsJson;
            if (requestJson.Length > 512 * 1024) throw new DesktopBridgeException("request_too_large", "درخواست رابط بیش از حد بزرگ است.", 413);
            using JsonDocument document = JsonDocument.Parse(requestJson);
            JsonElement root = document.RootElement;
            requestId = GetRequiredString(root, "id", 100);
            string requestType = GetRequiredString(root, "type", 100);
            JsonElement payload = root.TryGetProperty("payload", out JsonElement payloadElement) ? payloadElement.Clone() : JsonSerializer.SerializeToElement(new { }, JsonOptions);
            JsonElement result = await HandleBridgeRequestAsync(requestType, payload, CancellationToken.None);
            PostBridgeResponse(new { id = requestId, ok = true, result });
        }
        catch (DesktopBridgeException exception)
        {
            PostBridgeResponse(new { id = requestId ?? string.Empty, ok = false, error = new { exception.Code, exception.Message, exception.Status } });
        }
        catch (JsonException)
        {
            PostBridgeResponse(new { id = requestId ?? string.Empty, ok = false, error = new { code = "invalid_request", message = "ساختار درخواست رابط معتبر نیست.", status = 400 } });
        }
        catch (Exception)
        {
            PostBridgeResponse(new { id = requestId ?? string.Empty, ok = false, error = new { code = "desktop_error", message = "برنامهٔ دسکتاپ نتوانست درخواست را کامل کند.", status = 500 } });
        }
    }

    private async Task<JsonElement> HandleBridgeRequestAsync(string requestType, JsonElement payload, CancellationToken cancellationToken)
    {
        switch (requestType)
        {
            case "runtime.get": return JsonSerializer.SerializeToElement(_settingsStore.GetRuntimeConfiguration(_secretStore.Exists), JsonOptions);
            case "runtime.configure":
            {
                string apiBaseUrl = GetRequiredString(payload, "apiBaseUrl", 2048);
                bool changed = _settingsStore.Configure(apiBaseUrl);
                if (changed) _authBroker.ClearSession();
                return JsonSerializer.SerializeToElement(_settingsStore.GetRuntimeConfiguration(_secretStore.Exists), JsonOptions);
            }
            case "auth.login": return await _authBroker.LoginAsync(payload, cancellationToken);
            case "auth.refresh": return await _authBroker.RefreshAsync(cancellationToken);
            case "auth.mfa.setup": return await _authBroker.SetupMfaAsync(payload, cancellationToken);
            case "auth.mfa.enable": return await _authBroker.EnableMfaAsync(payload, cancellationToken);
            case "auth.logout": return await _authBroker.LogoutAsync(payload, cancellationToken);
            case "auth.clear": _authBroker.ClearSession(); return JsonSerializer.SerializeToElement(new { cleared = true }, JsonOptions);
            case "invoice.document":
            {
                string action = GetRequiredString(payload, "action", 20).ToLowerInvariant();
                if (action is not ("preview" or "save" or "print")) throw new DesktopBridgeException("invalid_request", "عملیات فاکتور معتبر نیست.", 400);
                string html = GetRequiredString(payload, "html", 450_000);
                string suggestedFileName = GetRequiredString(payload, "suggestedFileName", 180);
                int copies = action == "print" ? GetRequiredInt(payload, "copies", 1, 20) : 1;
                CoreWebView2Environment environment = DashboardWebView.CoreWebView2?.Environment ?? throw new DesktopBridgeException("desktop_not_ready", "میزبان دسکتاپ هنوز آماده نیست.", 409);
                var invoiceWindow = new InvoiceDocumentWindow(this, environment, html, suggestedFileName);
                await invoiceWindow.InitializeAsync();
                if (action == "print")
                {
                    CoreWebView2PrintStatus printStatus = await invoiceWindow.PrintDefaultAsync(copies);
                    return JsonSerializer.SerializeToElement(new { action, opened = true, saved = false, printed = printStatus == CoreWebView2PrintStatus.Succeeded, printerName = "Windows default printer", failureCode = printStatus switch { CoreWebView2PrintStatus.Succeeded => (string?)null, CoreWebView2PrintStatus.PrinterUnavailable => "PRINTER_UNAVAILABLE", _ => "PRINT_FAILED" } }, JsonOptions);
                }
                if (action == "save")
                {
                    string? savedPath = await invoiceWindow.SavePdfAsync();
                    return JsonSerializer.SerializeToElement(new { action, opened = true, saved = savedPath is not null, fileName = savedPath is null ? null : Path.GetFileName(savedPath) }, JsonOptions);
                }
                return JsonSerializer.SerializeToElement(new { action, opened = true, saved = false }, JsonOptions);
            }
            default: throw new DesktopBridgeException("unsupported_command", "فرمان درخواستی توسط برنامهٔ دسکتاپ پشتیبانی نمی‌شود.", 400);
        }
    }

    private void PostBridgeResponse(object response)
    {
        if (!Uri.TryCreate(DashboardWebView.CoreWebView2?.Source, UriKind.Absolute, out Uri? source) || !string.Equals(source.Host, AppHostName, StringComparison.OrdinalIgnoreCase)) return;
        DashboardWebView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(response, JsonOptions));
    }

    private static string GetRequiredString(JsonElement element, string propertyName, int maximumLength)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.GetString())) throw new DesktopBridgeException("invalid_request", "اطلاعات درخواست رابط کامل نیست.", 400);
        string value = property.GetString()!.Trim();
        if (value.Length > maximumLength) throw new DesktopBridgeException("invalid_request", "یکی از مقادیر درخواست بیش از حد بلند است.", 400);
        return value;
    }

    private static int GetRequiredInt(JsonElement element, string propertyName, int minimum, int maximum)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out JsonElement property) || !property.TryGetInt32(out int value) || value < minimum || value > maximum) throw new DesktopBridgeException("invalid_request", "یکی از مقادیر عددی درخواست معتبر نیست.", 400);
        return value;
    }

    private void ShowLoadingState()
    {
        StartupPanel.Visibility = Visibility.Visible;
        StartupStatusText.Text = "در حال آماده‌سازی برنامه...";
        StartupDetailsText.Text = "رابط React و نشست امن Windows در حال راه‌اندازی هستند.";
        RetryButton.Visibility = Visibility.Collapsed;
    }

    private void ShowStartupError(string title, string details)
    {
        StartupPanel.Visibility = Visibility.Visible;
        StartupStatusText.Text = title;
        StartupDetailsText.Text = details;
        RetryButton.Visibility = Visibility.Visible;
    }
}
