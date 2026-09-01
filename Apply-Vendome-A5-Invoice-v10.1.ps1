$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Root = "C:\Users\Arian\source\repos\VendomeJewleryInvoiceManagement"
if (-not (Test-Path -LiteralPath $Root)) {
    throw "Vendome repository root not found: $Root"
}
Set-Location $Root

$BundleRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$PayloadRoot = Join-Path $BundleRoot "payload"
$Utf8 = New-Object System.Text.UTF8Encoding($false)
$Stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$BackupRoot = Join-Path $Root ".vendome-backups\a5-invoice-template-v10.1-$Stamp"
New-Item -ItemType Directory -Path $BackupRoot -Force | Out-Null

$InvoiceTemplatePath = "GoldInvoice.Client\src\features\invoices\invoiceDocument.ts"
$DesktopRendererPath = "VendomeJewleryDesktopApp\InvoiceDocumentWindow.cs"
$Files = @($InvoiceTemplatePath, $DesktopRendererPath)

function Read-Text([string]$relative) {
    $path = Join-Path $Root $relative
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required file not found: $relative"
    }
    return [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
}

function Write-Text([string]$relative, [string]$content) {
    $path = Join-Path $Root $relative
    [System.IO.File]::WriteAllText($path, $content, $Utf8)
}

function Backup-File([string]$relative) {
    $source = Join-Path $Root $relative
    $target = Join-Path $BackupRoot $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $target -Force
}

function Restore-All {
    foreach ($relative in $Files) {
        $backup = Join-Path $BackupRoot $relative
        $target = Join-Path $Root $relative
        if (Test-Path -LiteralPath $backup) {
            Copy-Item -LiteralPath $backup -Destination $target -Force
        }
    }
}

foreach ($relative in $Files) { Backup-File $relative }

try {
    foreach ($processName in @("VendomeJewleryDesktopApp", "GoldInvoice.Api", "GoldInvoice.Worker")) {
        Get-Process $processName -ErrorAction SilentlyContinue |
            Stop-Process -Force -ErrorAction SilentlyContinue
    }
    Start-Sleep -Milliseconds 700

    # Phase 7C-A explicitly defines invoiceDocument.ts as the replaceable template boundary.
    $currentInvoice = Read-Text $InvoiceTemplatePath
    if ($currentInvoice -notmatch 'export\s+function\s+buildInvoiceDocumentHtml\s*\(' -or
        $currentInvoice -notmatch 'export\s+function\s+invoiceFileName\s*\(') {
        throw "Phase 7C invoice-template boundary was not found."
    }

    $payloadInvoice = Join-Path $PayloadRoot $InvoiceTemplatePath
    if (-not (Test-Path -LiteralPath $payloadInvoice)) {
        throw "A5 invoice template payload is missing."
    }
    Copy-Item -LiteralPath $payloadInvoice -Destination (Join-Path $Root $InvoiceTemplatePath) -Force

    # The existing Desktop print orchestration remains unchanged. Only its document
    # media settings become A5 portrait, so preview/save/print still use the same HTML.
    $renderer = Read-Text $DesktopRendererPath

    if ($renderer -notmatch 'CreateA5InvoicePrintSettings\s*\(') {
        $savePattern = 'CoreWebView2PrintSettings\s+settings\s*=\s*_environment\.CreatePrintSettings\(\);\s*settings\.ShouldPrintBackgrounds\s*=\s*true;\s*settings\.ShouldPrintHeaderAndFooter\s*=\s*false;'
        $saveMatches = [regex]::Matches(
            $renderer,
            $savePattern,
            [System.Text.RegularExpressions.RegexOptions]::Singleline)

        if ($saveMatches.Count -lt 1) {
            throw "Desktop PDF print-settings ASCII anchor was not found."
        }

        # Replace only the first no-Copies settings block (SavePdfAsync).
        $saveMatch = $saveMatches[0]
        $renderer = $renderer.Remove($saveMatch.Index, $saveMatch.Length)
        $renderer = $renderer.Insert(
            $saveMatch.Index,
            'CoreWebView2PrintSettings settings = CreateA5InvoicePrintSettings();')

        $printPattern = 'CoreWebView2PrintSettings\s+settings\s*=\s*_environment\.CreatePrintSettings\(\);\s*settings\.Copies\s*=\s*copies;\s*settings\.ShouldPrintBackgrounds\s*=\s*true;\s*settings\.ShouldPrintHeaderAndFooter\s*=\s*false;'
        $printMatch = [regex]::Match(
            $renderer,
            $printPattern,
            [System.Text.RegularExpressions.RegexOptions]::Singleline)

        if (-not $printMatch.Success) {
            throw "Desktop direct-print settings ASCII anchor was not found."
        }

        $renderer = $renderer.Remove($printMatch.Index, $printMatch.Length)
        $renderer = $renderer.Insert(
            $printMatch.Index,
            "CoreWebView2PrintSettings settings = CreateA5InvoicePrintSettings();`r`n        settings.Copies = copies;")

        $helperAnchor = '    private Button CreateButton(string label, RoutedEventHandler handler, bool primary = false)'
        $helperIndex = $renderer.IndexOf(
            $helperAnchor,
            [System.StringComparison]::Ordinal)

        if ($helperIndex -lt 0) {
            throw "Desktop invoice renderer helper ASCII anchor was not found."
        }

        $helper = @'
    private CoreWebView2PrintSettings CreateA5InvoicePrintSettings()
    {
        CoreWebView2PrintSettings settings = _environment.CreatePrintSettings();
        settings.MediaSize = CoreWebView2PrintMediaSize.Custom;
        settings.Orientation = CoreWebView2PrintOrientation.Portrait;
        settings.PageWidth = 148.0 / 25.4;
        settings.PageHeight = 210.0 / 25.4;
        settings.MarginTop = 0;
        settings.MarginRight = 0;
        settings.MarginBottom = 0;
        settings.MarginLeft = 0;
        settings.ScaleFactor = 1.0;
        settings.ShouldPrintBackgrounds = true;
        settings.ShouldPrintHeaderAndFooter = false;
        settings.ShouldPrintSelectionOnly = false;
        return settings;
    }

'@
        $renderer = $renderer.Insert($helperIndex, $helper)
        Write-Text $DesktopRendererPath $renderer
    }

    # Static semantics: no order/payment/print-job workflow files are touched.
    $invoiceCheck = Read-Text $InvoiceTemplatePath
    foreach ($token in @(
        'VENDOME_INVOICE_A5_POTRAIT.PDF',
        '@page { size: 148mm 210mm; margin: 0; }',
        'const ITEMS_PER_PAGE = 6',
        'number(item.unitPriceRials)',
        'decimal(item.netGoldWeightGrams)',
        'background-image: url("${APPROVED_A5_TEMPLATE}")'
    )) {
        if (-not $invoiceCheck.Contains($token)) {
            throw "A5 invoice-template validation failed: $token"
        }
    }
    if ($invoiceCheck.Contains('item.wageRials') -or
        $invoiceCheck.Contains('item.taxRials') -or
        $invoiceCheck.Contains('item.profitRials')) {
        throw "README rule violation: wage/tax/profit breakdown was added to the invoice."
    }

    $rendererCheck = Read-Text $DesktopRendererPath
    foreach ($token in @(
        'CreateA5InvoicePrintSettings()',
        'CoreWebView2PrintMediaSize.Custom',
        'settings.PageWidth = 148.0 / 25.4;',
        'settings.PageHeight = 210.0 / 25.4;',
        'settings.MarginTop = 0;',
        'settings.ShouldPrintBackgrounds = true;'
    )) {
        if (-not $rendererCheck.Contains($token)) {
            throw "A5 Desktop renderer validation failed: $token"
        }
    }

    Write-Host ""
    Write-Host "[1/3] TypeScript check..." -ForegroundColor Cyan
    npm --prefix ".\GoldInvoice.Client" run check
    if ($LASTEXITCODE -ne 0) { throw "TypeScript check failed." }

    Write-Host ""
    Write-Host "[2/3] React production build..." -ForegroundColor Cyan
    Remove-Item ".\GoldInvoice.Client\dist" -Recurse -Force -ErrorAction SilentlyContinue
    npm --prefix ".\GoldInvoice.Client" run build
    if ($LASTEXITCODE -ne 0) { throw "React production build failed." }

    Write-Host ""
    Write-Host "[3/3] Desktop A5 print renderer build..." -ForegroundColor Cyan
    dotnet build ".\VendomeJewleryDesktopApp\VendomeJewleryDesktopApp.csproj" -c Release
    if ($LASTEXITCODE -ne 0) { throw "Desktop Release build failed." }

    Write-Host ""
    Write-Host "SUCCESS: Approved VENDOME A5 portrait invoice v10.1 is connected." -ForegroundColor Green
    Write-Host "Preview / PDF / Print now share the same approved A5 document source." -ForegroundColor Green
    Write-Host "Invoice print media: A5 portrait, zero document margins." -ForegroundColor Green
    Write-Host "README Phase 7C rule preserved: no payment/order/print-job orchestration changed." -ForegroundColor Green
    Write-Host "README Phase 7C rule preserved: wage/tax/profit breakdown is not printed." -ForegroundColor Green
    Write-Host "Backup: $BackupRoot"
    Write-Host ""
    Write-Host "Reopen Desktop and print an issued invoice. Windows default printer must have A5 paper available." -ForegroundColor Yellow
}
catch {
    Write-Host ""
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Rolling back A5 invoice changes..." -ForegroundColor Yellow
    Restore-All
    Write-Host "Rollback completed. Backup remains at: $BackupRoot" -ForegroundColor Yellow
    throw
}
