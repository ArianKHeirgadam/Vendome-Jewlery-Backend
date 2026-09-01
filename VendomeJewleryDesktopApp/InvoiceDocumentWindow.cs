using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace VendomeJewleryDesktopApp;

internal sealed class InvoiceDocumentWindow : Window
{
    private readonly CoreWebView2Environment _environment;
    private readonly string _html;
    private readonly string _suggestedFileName;
    private readonly WebView2 _webView = new();
    private readonly TextBlock _status = new()
    {
        Margin = new Thickness(12, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = new SolidColorBrush(Color.FromRgb(93, 105, 123)),
        Text = "در حال آماده‌سازی پیش‌نمایش..."
    };
    private bool _initialized;
    private bool _pdfExportInProgress;

    public InvoiceDocumentWindow(
        Window owner,
        CoreWebView2Environment environment,
        string html,
        string suggestedFileName)
    {
        Owner = owner;
        _environment = environment;
        _html = ApplyInvoicePrintTypography(html);
        _suggestedFileName = SanitizeFileName(suggestedFileName);

        Title = "پیش‌نمایش فاکتور وندوم";
        Width = 1080;
        Height = 900;
        MinWidth = 760;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(231, 233, 237));
        Content = BuildLayout();
        Closed += (_, _) => _webView.Dispose();
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        if (!IsVisible)
        {
            Show();
        }

        await _webView.EnsureCoreWebView2Async(_environment);
        _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        _webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
        _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        _webView.CoreWebView2.Settings.IsZoomControlEnabled = true;
        _webView.CoreWebView2.Settings.IsScriptEnabled = false;
        _webView.CoreWebView2.NavigationStarting += (_, eventArgs) =>
        {
            if (!string.Equals(eventArgs.Uri, "about:blank", StringComparison.OrdinalIgnoreCase))
            {
                eventArgs.Cancel = true;
            }
        };

        var navigation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs eventArgs)
        {
            if (eventArgs.IsSuccess)
            {
                navigation.TrySetResult(true);
            }
            else
            {
                navigation.TrySetException(new InvalidOperationException(
                    $"Invoice preview navigation failed: {eventArgs.WebErrorStatus}"));
            }
        }

        _webView.NavigationCompleted += NavigationCompleted;
        try
        {
            _webView.NavigateToString(_html);
            await navigation.Task.WaitAsync(TimeSpan.FromSeconds(30));
            _initialized = true;
            _status.Text = "فاکتور آمادهٔ چاپ یا ذخیره PDF است.";
        }
        finally
        {
            _webView.NavigationCompleted -= NavigationCompleted;
        }
    }

    public async Task<string?> SavePdfAsync()
    {
        EnsureInitialized();
        if (_pdfExportInProgress)
        {
            throw new InvalidOperationException("A PDF export is already in progress.");
        }

        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = ".pdf",
            FileName = _suggestedFileName,
            Filter = "PDF Files (*.pdf)|*.pdf",
            OverwritePrompt = true,
            Title = "ذخیره فاکتور وندوم"
        };
        if (dialog.ShowDialog(this) != true)
        {
            _status.Text = "ذخیره PDF لغو شد.";
            return null;
        }

        _pdfExportInProgress = true;
        _status.Text = "در حال ساخت فایل PDF...";
        try
        {
            CoreWebView2PrintSettings settings = _environment.CreatePrintSettings();
            settings.ShouldPrintBackgrounds = true;
            settings.ShouldPrintHeaderAndFooter = false;
            bool succeeded = await _webView.CoreWebView2.PrintToPdfAsync(dialog.FileName, settings);
            if (!succeeded)
            {
                throw new IOException("WebView2 could not create the invoice PDF.");
            }

            _status.Text = $"PDF ذخیره شد: {dialog.FileName}";
            return dialog.FileName;
        }
        finally
        {
            _pdfExportInProgress = false;
        }
    }

    private UIElement BuildLayout()
    {
        var root = new DockPanel();
        var toolbar = new Border
        {
            Padding = new Thickness(14, 10, 14, 10),
            Background = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(218, 222, 229)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        DockPanel.SetDock(toolbar, Dock.Top);

        var toolbarContent = new DockPanel { LastChildFill = true };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            FlowDirection = FlowDirection.RightToLeft
        };
        actions.Children.Add(CreateButton("دانلود PDF", async (_, _) => await SavePdfWithMessageAsync(), primary: true));
        actions.Children.Add(CreateButton("بستن", (_, _) => Close()));
        DockPanel.SetDock(actions, Dock.Right);
        toolbarContent.Children.Add(actions);
        toolbarContent.Children.Add(_status);
        toolbar.Child = toolbarContent;

        root.Children.Add(toolbar);
        root.Children.Add(_webView);
        return root;
    }

    public async Task<CoreWebView2PrintStatus> PrintDefaultAsync(int copies)
    {
        EnsureInitialized();
        if (copies is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(copies));
        }

        _status.Text = "در حال ارسال فاکتور به چاپگر پیش‌فرض...";
        CoreWebView2PrintSettings settings = _environment.CreatePrintSettings();
        settings.Copies = copies;
        settings.ShouldPrintBackgrounds = true;
        settings.ShouldPrintHeaderAndFooter = false;
        CoreWebView2PrintStatus status = await _webView.CoreWebView2.PrintAsync(settings);
        _status.Text = status switch
        {
            CoreWebView2PrintStatus.Succeeded => "فاکتور با موفقیت به چاپگر پیش‌فرض ارسال شد.",
            CoreWebView2PrintStatus.PrinterUnavailable => "چاپگر پیش‌فرض در دسترس نیست.",
            _ => "چاپ فاکتور کامل نشد."
        };
        return status;
    }

    private Button CreateButton(string label, RoutedEventHandler handler, bool primary = false)
    {
        var button = new Button
        {
            Content = label,
            MinWidth = 100,
            Height = 36,
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(14, 0, 14, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Foreground = new SolidColorBrush(primary ? Colors.White : Color.FromRgb(20, 40, 68)),
            Background = new SolidColorBrush(primary
                ? Color.FromRgb(20, 40, 68)
                : Color.FromRgb(247, 248, 250)),
            BorderBrush = new SolidColorBrush(primary
                ? Color.FromRgb(20, 40, 68)
                : Color.FromRgb(210, 215, 223)),
            BorderThickness = new Thickness(1)
        };
        button.Click += handler;
        return button;
    }

    private async Task SavePdfWithMessageAsync()
    {
        try
        {
            await SavePdfAsync();
        }
        catch (Exception)
        {
            _status.Text = "ساخت PDF کامل نشد؛ دوباره تلاش کن.";
            MessageBox.Show(
                this,
                "فایل PDF ساخته نشد. مسیر ذخیره و دسترسی فایل را بررسی کن.",
                "خطای خروجی PDF",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void EnsureInitialized()
    {
        if (!_initialized || _webView.CoreWebView2 is null)
        {
            throw new InvalidOperationException("The invoice preview is not ready.");
        }
    }

    private static string ApplyInvoicePrintTypography(string html)
    {
        const string printTypography = @"
<style id=""vendome-invoice-print-typography"">
  /* IranSans is preferred for the invoice; Windows-local variants cover common installations. */
  html, body {
    font-family: ""IRANSans"", ""IRANSansWeb"", ""IRANSansX"", Tahoma, ""Segoe UI"", sans-serif !important;
    font-size: 9.5px !important;
    line-height: 1.6 !important;
    text-rendering: optimizeLegibility;
  }

  .sheet,
  .header,
  .party-grid,
  .party-card,
  .info-list,
  .info-item,
  .document-strip,
  .document-strip > div,
  .summary,
  .notes,
  .totals,
  .total-row,
  .signatures,
  .signature,
  .footer {
    min-width: 0 !important;
  }

  .invoice-meta,
  .party-card,
  .info-item,
  .info-item strong,
  .document-strip > div,
  .document-strip strong,
  .invoice-number,
  .invoice-date,
  .status,
  th,
  td,
  .product-cell,
  .product-cell strong,
  .product-cell span,
  .product-cell small,
  .money-cell,
  .notes,
  .notes p,
  .total-row span,
  .total-row strong,
  .footer > div {
    overflow-wrap: anywhere !important;
    word-break: normal !important;
    min-width: 0 !important;
  }

  .party-card,
  .document-strip > div,
  .totals,
  .notes {
    overflow: hidden;
  }

  .invoice-meta h2 {
    font-size: 16px !important;
  }

  .invoice-number {
    font-size: 11px !important;
    white-space: normal !important;
  }

  .info-item strong {
    font-size: 8.6px !important;
    line-height: 1.6 !important;
  }

  .document-strip strong {
    font-size: 8.5px !important;
    line-height: 1.55 !important;
  }

  th {
    font-size: 7.25px !important;
    line-height: 1.35 !important;
  }

  td {
    font-size: 7.7px !important;
    line-height: 1.5 !important;
  }

  .product-cell strong {
    font-size: 8.1px !important;
    line-height: 1.45 !important;
  }

  .product-cell span {
    font-size: 7.1px !important;
    line-height: 1.4 !important;
  }

  .product-cell small {
    font-size: 6.5px !important;
    line-height: 1.3 !important;
  }

  .money-cell,
  .total-row strong {
    white-space: normal !important;
    font-variant-numeric: tabular-nums;
  }

  .total-row span,
  .total-row strong {
    line-height: 1.5 !important;
  }

  .total-row.grand strong {
    font-size: 10.5px !important;
  }

  .notes p {
    line-height: 1.75 !important;
  }

  .footer {
    align-items: start !important;
  }
</style>"";

        const headClose = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (headClose < 0)
        {
            return printTypography + html;
        }

        return html.Insert(headClose, printTypography);
    }

    private static string SanitizeFileName(string value)
    {
        string candidate = string.IsNullOrWhiteSpace(value) ? "Vendome-Invoice.pdf" : value.Trim();
        foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
        {
            candidate = candidate.Replace(invalidCharacter, '-');
        }

        if (!candidate.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            candidate += ".pdf";
        }

        return candidate.Length <= 160 ? candidate : $"{candidate[..156]}.pdf";
    }
}
