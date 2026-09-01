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
$BackupRoot = Join-Path $Root ".vendome-backups\a5-invoice-data-v10.4-$Stamp"
New-Item -ItemType Directory -Path $BackupRoot -Force | Out-Null

$InvoiceTemplatePath = "GoldInvoice.Client\src\features\invoices\invoiceDocument.ts"
$OperationsPath = "GoldInvoice.Client\src\features\operations\OperationsPages.tsx"
$TypesPath = "GoldInvoice.Client\src\features\operations\operations.types.ts"
$DesktopRendererPath = "VendomeJewleryDesktopApp\InvoiceDocumentWindow.cs"
$AuthenticationTestsPath = "GoldInvoice.IntegrationTests\AuthenticationFlowTests.cs"
$Files = @(
    $InvoiceTemplatePath,
    $OperationsPath,
    $TypesPath,
    $DesktopRendererPath,
    $AuthenticationTestsPath
)

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

    # ------------------------------------------------------------------
    # README / Phase 7C boundary:
    # - invoice snapshots stay authoritative and immutable;
    # - only the replaceable invoice document mapper is changed visually;
    # - preview/PDF/print continue to consume the same HTML source;
    # - order/payment/print-job API orchestration is not replaced.
    # ------------------------------------------------------------------
    $currentInvoice = Read-Text $InvoiceTemplatePath
    if ($currentInvoice -notmatch 'export\s+function\s+buildInvoiceDocumentHtml\s*\(' -or
        $currentInvoice -notmatch 'export\s+function\s+invoiceFileName\s*\(') {
        throw "Phase 7C invoice-template boundary was not found."
    }

    $payloadInvoice = Join-Path $PayloadRoot $InvoiceTemplatePath
    if (-not (Test-Path -LiteralPath $payloadInvoice)) {
        throw "A5 v10.4 invoice template payload is missing."
    }
    Copy-Item -LiteralPath $payloadInvoice -Destination (Join-Path $Root $InvoiceTemplatePath) -Force

    # ------------------------------------------------------------------
    # Match the existing TypeScript model to the existing backend contract.
    # Backend InvoiceItemResponse is NOT an OrderItem and includes LineNumber.
    # No backend model, persistence or migration is changed.
    # ------------------------------------------------------------------
    $types = Read-Text $TypesPath

    if ($types -notmatch 'export\s+interface\s+InvoiceItem\s*\{') {
        $invoiceInterfaceAnchor = 'export interface Invoice {'
        $invoiceInterfaceIndex = $types.IndexOf(
            $invoiceInterfaceAnchor,
            [System.StringComparison]::Ordinal)

        if ($invoiceInterfaceIndex -lt 0) {
            throw "Invoice TypeScript interface ASCII anchor was not found."
        }

        $invoiceItemInterface = @'
export interface InvoiceItem {
  id: string;
  orderItemId?: string | null;
  priceCalculationSnapshotId?: string | null;
  inventoryUnitId?: string | null;
  lineNumber: number;
  sku: string;
  productName: string;
  variantName: string;
  grossWeightGrams: number;
  netGoldWeightGrams?: number | null;
  karat?: number | null;
  quantity: number;
  marketUnitPriceRials?: number | null;
  goldValueRials?: number | null;
  wageRials?: number | null;
  profitRials?: number | null;
  taxRials?: number | null;
  unitPriceRials: number;
  lineTotalRials: number;
  acquisitionUnitCostRials?: number | null;
  acquisitionTotalCostRials?: number | null;
  grossProfitRials?: number | null;
  roundingPolicy?: string | null;
}

'@
        $types = $types.Insert($invoiceInterfaceIndex, $invoiceItemInterface)
    }

    $invoiceStart = $types.IndexOf(
        'export interface Invoice {',
        [System.StringComparison]::Ordinal)
    $invoiceEnd = $types.IndexOf(
        'export interface Payment {',
        $invoiceStart,
        [System.StringComparison]::Ordinal)

    if ($invoiceStart -lt 0 -or $invoiceEnd -lt 0 -or $invoiceEnd -le $invoiceStart) {
        throw "Could not isolate Invoice TypeScript interface."
    }

    $invoiceTypeBlock = $types.Substring($invoiceStart, $invoiceEnd - $invoiceStart)
    if ($invoiceTypeBlock.Contains('items: OrderItem[];')) {
        $invoiceTypeBlock = $invoiceTypeBlock.Replace(
            'items: OrderItem[];',
            'items: InvoiceItem[];')
        $types = $types.Remove($invoiceStart, $invoiceEnd - $invoiceStart)
        $types = $types.Insert($invoiceStart, $invoiceTypeBlock)
    }
    elseif (-not $invoiceTypeBlock.Contains('items: InvoiceItem[];')) {
        throw "Invoice.items type anchor was not found."
    }

    Write-Text $TypesPath $types

    # ------------------------------------------------------------------
    # Always load the authoritative invoice detail immediately before a
    # document action. List data remains useful for the grid, but the document
    # must use GET /invoices/{id}, whose backend response contains immutable
    # item/store/customer snapshots. Payment detail comes from the existing
    # GET /payments/{paymentId} endpoint; no payment value is invented.
    #
    # IMPORTANT v10.4:
    # Do not depend on the exact spelling/format of closePreview. Some local
    # branches legitimately refactor that callback. selectedPayment cleanup is
    # tied to selected state instead, so Preview/PDF/Print behavior is stable.
    # ------------------------------------------------------------------
    $operations = Read-Text $OperationsPath
    $invoicesStart = $operations.IndexOf(
        'function InvoicesPage(',
        [System.StringComparison]::Ordinal)
    $invoicesEnd = $operations.IndexOf(
        'function InvoiceDetails(',
        $invoicesStart,
        [System.StringComparison]::Ordinal)

    if ($invoicesStart -lt 0 -or
        $invoicesEnd -lt 0 -or
        $invoicesEnd -le $invoicesStart) {
        throw "Could not isolate InvoicesPage."
    }

    $invoicesBlock = $operations.Substring(
        $invoicesStart,
        $invoicesEnd - $invoicesStart)

    # --------------------------------------------------------------
    # Authoritative preview/payment state.
    # --------------------------------------------------------------
    if ($invoicesBlock -notmatch
        'selectedPayment\s*,\s*setSelectedPayment') {

        $selectedStateMatch = [regex]::Match(
            $invoicesBlock,
            '(?m)^(?<indent>\s*)const\s+\[selected,\s*setSelected\]\s*=\s*useState<Invoice\s*\|\s*null>\(null\);\s*$')

        if (-not $selectedStateMatch.Success) {
            throw "InvoicesPage selected-state structural anchor was not found."
        }

        $stateInsert =
            $selectedStateMatch.Value +
            [Environment]::NewLine +
            $selectedStateMatch.Groups["indent"].Value +
            'const [selectedPayment, setSelectedPayment] = useState<Payment | null>(null);'

        $invoicesBlock = $invoicesBlock.Remove(
            $selectedStateMatch.Index,
            $selectedStateMatch.Length)
        $invoicesBlock = $invoicesBlock.Insert(
            $selectedStateMatch.Index,
            $stateInsert)
    }

    # Clear paired payment whenever preview closes, WITHOUT touching closePreview.
    if ($invoicesBlock -notmatch
        'if\s*\(\s*!selected\s*\)\s*setSelectedPayment\(null\)') {

        $selectedPaymentMatch = [regex]::Match(
            $invoicesBlock,
            '(?m)^(?<indent>\s*)const\s+\[selectedPayment,\s*setSelectedPayment\]\s*=\s*useState<Payment\s*\|\s*null>\(null\);\s*$')

        if (-not $selectedPaymentMatch.Success) {
            throw "InvoicesPage selectedPayment state structural anchor was not found."
        }

        $cleanupEffect =
            $selectedPaymentMatch.Value +
            [Environment]::NewLine +
            $selectedPaymentMatch.Groups["indent"].Value +
            'useEffect(() => {' +
            [Environment]::NewLine +
            $selectedPaymentMatch.Groups["indent"].Value +
            '  if (!selected) setSelectedPayment(null);' +
            [Environment]::NewLine +
            $selectedPaymentMatch.Groups["indent"].Value +
            '}, [selected]);'

        $invoicesBlock = $invoicesBlock.Remove(
            $selectedPaymentMatch.Index,
            $selectedPaymentMatch.Length)
        $invoicesBlock = $invoicesBlock.Insert(
            $selectedPaymentMatch.Index,
            $cleanupEffect)
    }

    # --------------------------------------------------------------
    # One authoritative loader for Preview / Save / Print.
    # --------------------------------------------------------------
    if ($invoicesBlock -notmatch
        'async\s+function\s+loadInvoiceDocument\s*\(\s*invoiceId:\s*string\s*\)') {

        $canPrintMatch = [regex]::Match(
            $invoicesBlock,
            '(?m)^(?<indent>\s*)const\s+canPrint\s*=\s*user\?\.permissions\.includes\("Invoices\.Print"\)\s*===\s*true;\s*$')

        if (-not $canPrintMatch.Success) {
            throw "InvoicesPage canPrint structural anchor was not found."
        }

        $helper = @'

  async function loadInvoiceDocument(invoiceId: string): Promise<{
    invoice: Invoice;
    payment: Payment | null;
  }> {
    const invoice = await request<Invoice>(`/api/v1/invoices/${invoiceId}`);

    if (!Array.isArray(invoice.items) || invoice.items.length === 0) {
      throw new Error(
        "اقلام فاکتور از سرویس فاکتور دریافت نشد؛ برای جلوگیری از چاپ سند ناقص، عملیات متوقف شد.",
      );
    }

    if (invoice.items.some((item) => item.lineNumber < 1 || item.quantity < 1)) {
      throw new Error(
        "اطلاعات اقلام فاکتور ناقص است؛ چاپ تا دریافت Snapshot معتبر متوقف شد.",
      );
    }

    const payment = invoice.paymentId
      ? await request<Payment>(`/api/v1/payments/${invoice.paymentId}`)
      : null;

    if (payment && payment.status !== "Verified") {
      throw new Error(
        "پرداخت این فاکتور هنوز تأیید نهایی نشده است؛ سند چاپ نمی‌شود.",
      );
    }

    return { invoice, payment };
  }

  async function openInvoicePreview(invoice: Invoice) {
    setBusyAction(`preview:${invoice.id}`);
    try {
      const document = await loadInvoiceDocument(invoice.id);
      setSelected(document.invoice);
      setSelectedPayment(document.payment);
    } catch (error) {
      onNotice(messageOf(error));
    } finally {
      setBusyAction(null);
    }
  }
'@

        $helperInsertAt =
            $canPrintMatch.Index +
            $canPrintMatch.Length

        $invoicesBlock = $invoicesBlock.Insert(
            $helperInsertAt,
            $helper)
    }

    # Query-string auto-open uses the same authoritative loader.
    $invoicesBlock = [regex]::Replace(
        $invoicesBlock,
        '(?m)^(?<indent>\s*)if\s*\(\s*invoice\s*\)\s*setSelected\(invoice\);\s*$',
        '${indent}if (invoice) void openInvoicePreview(invoice);')

    # Eye action must not render the potentially stale list object directly.
    $invoicesBlock = $invoicesBlock.Replace(
        'onClick={() => setSelected(invoice)}',
        'onClick={() => void openInvoicePreview(invoice)}')

    # --------------------------------------------------------------
    # Patch openDesktopDocument ITSELF.
    # v10.2 incorrectly used a file-wide Contains() guard; the preview helper
    # already contained "const document = ..." and could cause Save/Print to
    # stay on stale list data. v10.4 isolates this exact function.
    # --------------------------------------------------------------
    $desktopStart = $invoicesBlock.IndexOf(
        'const openDesktopDocument = async (',
        [System.StringComparison]::Ordinal)
    $desktopEnd = $invoicesBlock.IndexOf(
        'const savePdf = async (',
        $desktopStart,
        [System.StringComparison]::Ordinal)

    if ($desktopStart -lt 0 -or
        $desktopEnd -lt 0 -or
        $desktopEnd -le $desktopStart) {
        throw "Could not isolate openDesktopDocument."
    }

    $desktopBlock = $invoicesBlock.Substring(
        $desktopStart,
        $desktopEnd - $desktopStart)

    if ($desktopBlock -notmatch
        'const\s+document\s*=\s*await\s+loadInvoiceDocument\(invoice\.id\);') {

        $desktopBodyMatch = [regex]::Match(
            $desktopBlock,
            '\)\s*=>\s*\{')

        if (-not $desktopBodyMatch.Success) {
            throw "openDesktopDocument function-body structural anchor was not found."
        }

        $insertAt =
            $desktopBodyMatch.Index +
            $desktopBodyMatch.Length

        $desktopBlock = $desktopBlock.Insert(
            $insertAt,
            [Environment]::NewLine +
            '    const document = await loadInvoiceDocument(invoice.id);')
    }

    $desktopBlock = $desktopBlock.Replace(
        'buildInvoiceDocumentHtml(invoice)',
        'buildInvoiceDocumentHtml(document.invoice, document.payment)')

    if ($desktopBlock -notmatch
            'buildInvoiceDocumentHtml\(document\.invoice,\s*document\.payment\)' -or
        $desktopBlock -match
            'buildInvoiceDocumentHtml\(invoice\)') {
        throw "openDesktopDocument authoritative-document rewrite failed."
    }

    $invoicesBlock = $invoicesBlock.Remove(
        $desktopStart,
        $desktopEnd - $desktopStart)
    $invoicesBlock = $invoicesBlock.Insert(
        $desktopStart,
        $desktopBlock)

    # Wire the paired Payment into the exact same preview template.
    if ($invoicesBlock -notmatch
        '<InvoiceDetails\s+invoice=\{selected\}\s+payment=\{selectedPayment\}') {

        $invoicesBlock = [regex]::Replace(
            $invoicesBlock,
            '<InvoiceDetails\s+invoice=\{selected\}',
            '<InvoiceDetails invoice={selected} payment={selectedPayment}',
            [System.Text.RegularExpressions.RegexOptions]::Singleline)
    }

    # Put the patched InvoicesPage back before patching InvoiceDetails.
    $operations = $operations.Remove(
        $invoicesStart,
        $invoicesEnd - $invoicesStart)
    $operations = $operations.Insert(
        $invoicesStart,
        $invoicesBlock)

    # --------------------------------------------------------------
    # Patch InvoiceDetails separately so iframe preview shares the same
    # authoritative escaped document source.
    # --------------------------------------------------------------
    $detailsStart = $operations.IndexOf(
        'function InvoiceDetails(',
        [System.StringComparison]::Ordinal)
    $detailsEnd = $operations.IndexOf(
        'type AddressMode',
        $detailsStart,
        [System.StringComparison]::Ordinal)

    if ($detailsStart -lt 0 -or
        $detailsEnd -lt 0 -or
        $detailsEnd -le $detailsStart) {
        throw "Could not isolate InvoiceDetails."
    }

    $detailsBlock = $operations.Substring(
        $detailsStart,
        $detailsEnd - $detailsStart)

    # Add payment to the destructured props using the stable `invoice,` token.
    if ($detailsBlock -notmatch
        '(?m)^\s*payment,\s*$') {

        $invoicePropMatch = [regex]::Match(
            $detailsBlock,
            '(?m)^(?<indent>\s*)invoice,\s*$')

        if (-not $invoicePropMatch.Success) {
            throw "InvoiceDetails invoice destructuring structural anchor was not found."
        }

        $invoiceAndPayment =
            $invoicePropMatch.Value +
            [Environment]::NewLine +
            $invoicePropMatch.Groups["indent"].Value +
            'payment,'

        $detailsBlock = $detailsBlock.Remove(
            $invoicePropMatch.Index,
            $invoicePropMatch.Length)
        $detailsBlock = $detailsBlock.Insert(
            $invoicePropMatch.Index,
            $invoiceAndPayment)
    }

    # Add payment to the prop type without depending on LF/CRLF formatting.
    if ($detailsBlock -notmatch
        '(?m)^\s*payment:\s*Payment\s*\|\s*null;\s*$') {

        $invoiceTypeMatch = [regex]::Match(
            $detailsBlock,
            '(?m)^(?<indent>\s*)invoice:\s*Invoice;\s*$')

        if (-not $invoiceTypeMatch.Success) {
            throw "InvoiceDetails invoice prop-type structural anchor was not found."
        }

        $invoiceAndPaymentType =
            $invoiceTypeMatch.Value +
            [Environment]::NewLine +
            $invoiceTypeMatch.Groups["indent"].Value +
            'payment: Payment | null;'

        $detailsBlock = $detailsBlock.Remove(
            $invoiceTypeMatch.Index,
            $invoiceTypeMatch.Length)
        $detailsBlock = $detailsBlock.Insert(
            $invoiceTypeMatch.Index,
            $invoiceAndPaymentType)
    }

    $detailsBlock = [regex]::Replace(
        $detailsBlock,
        'srcDoc=\{buildInvoiceDocumentHtml\(\s*invoice\s*\)\}',
        'srcDoc={buildInvoiceDocumentHtml(invoice, payment)}')

    if ($detailsBlock -notmatch
        'srcDoc=\{buildInvoiceDocumentHtml\(\s*invoice,\s*payment\s*\)\}') {
        throw "InvoiceDetails preview document-source rewrite failed."
    }

    $operations = $operations.Remove(
        $detailsStart,
        $detailsEnd - $detailsStart)
    $operations = $operations.Insert(
        $detailsStart,
        $detailsBlock)

    Write-Text $OperationsPath $operations

    # ------------------------------------------------------------------
    # Keep/repair the existing A5 portrait desktop media settings from v10.1.
    # This changes only print media, not print-job orchestration.
    # ------------------------------------------------------------------
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
        $helperIndex = $renderer.IndexOf($helperAnchor, [System.StringComparison]::Ordinal)
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

    # ------------------------------------------------------------------
    # Reconcile stale Authentication integration tests with the CURRENT
    # management-login contract. Production security code is NOT weakened.
    #
    # Owner    -> email + Owner MFA
    # Admin    -> mobile + password, no Authenticator
    # Employee -> mobile + password, no Authenticator
    # Customer -> customer-domain account; no management-desktop login
    # ------------------------------------------------------------------
    $authTests = Read-Text $AuthenticationTestsPath

    # Refresh-token rotation should exercise a legitimate staff login.
    $refreshStart = $authTests.IndexOf(
        'public async Task SignIn_IssuesHashedRotatingRefreshTokenAndDetectsReuse()',
        [System.StringComparison]::Ordinal)
    $refreshEnd = $authTests.IndexOf(
        '[Fact]',
        $refreshStart + 10,
        [System.StringComparison]::Ordinal)

    if ($refreshStart -lt 0 -or $refreshEnd -lt 0) {
        throw "Could not isolate refresh-token integration test."
    }

    $refreshBlock = $authTests.Substring(
        $refreshStart,
        $refreshEnd - $refreshStart)

    $refreshBlock = $refreshBlock.Replace(
        'CreateUserAsync(scope.ServiceProvider, SecurityRoles.Customer)',
        'CreateUserAsync(scope.ServiceProvider, SecurityRoles.Employee)')
    $refreshBlock = $refreshBlock.Replace(
        'new SignInCommand(user.Email!, ValidPassword, null, null)',
        'new SignInCommand(user.PhoneNumber!, ValidPassword, null, null)')

    $authTests = $authTests.Remove(
        $refreshStart,
        $refreshEnd - $refreshStart)
    $authTests = $authTests.Insert(
        $refreshStart,
        $refreshBlock)

    # Access-token validation should also exercise an Employee/mobile login.
    $accessStart = $authTests.IndexOf(
        'public async Task AccessTokenValidation_LoadsCurrentRolesAndRejectsRevokedSession()',
        [System.StringComparison]::Ordinal)
    $accessEnd = $authTests.IndexOf(
        '[Fact]',
        $accessStart + 10,
        [System.StringComparison]::Ordinal)

    if ($accessStart -lt 0 -or $accessEnd -lt 0) {
        throw "Could not isolate access-token integration test."
    }

    $accessBlock = $authTests.Substring(
        $accessStart,
        $accessEnd - $accessStart)

    $accessBlock = $accessBlock.Replace(
        'CreateUserAsync(scope.ServiceProvider, SecurityRoles.Customer)',
        'CreateUserAsync(scope.ServiceProvider, SecurityRoles.Employee)')
    $accessBlock = $accessBlock.Replace(
        'new SignInCommand(user.Email!, ValidPassword, null, null)',
        'new SignInCommand(user.PhoneNumber!, ValidPassword, null, null)')
    $accessBlock = $accessBlock.Replace(
        'claim.Value == SecurityRoles.Customer',
        'claim.Value == SecurityRoles.Employee')

    $authTests = $authTests.Remove(
        $accessStart,
        $accessEnd - $accessStart)
    $authTests = $authTests.Insert(
        $accessStart,
        $accessBlock)

    # SecurityRoles now contains Owner/Admin/Employee/Customer.
    $authTests = $authTests.Replace(
        'Assert.Equal(3, await dbContext.Roles.CountAsync());',
        'Assert.Equal(SecurityRoles.All.Count, await dbContext.Roles.CountAsync());')

    # Customer creation is valid, but management login is intentionally rejected.
    $authTests = $authTests.Replace(
        'PeopleDirectory_CreatesPhoneOnlyCustomerWhoCanSignInByPhone',
        'PeopleDirectory_CreatesPhoneOnlyCustomerWhoCannotUseManagementSignIn')

    $customerStart = $authTests.IndexOf(
        'public async Task PeopleDirectory_CreatesPhoneOnlyCustomerWhoCannotUseManagementSignIn()',
        [System.StringComparison]::Ordinal)
    $helperStart = $authTests.IndexOf(
        'private static async Task<ApplicationUser> CreateUserAsync(',
        $customerStart,
        [System.StringComparison]::Ordinal)

    if ($customerStart -lt 0 -or
        $helperStart -lt 0 -or
        $helperStart -le $customerStart) {
        throw "Could not isolate phone-only Customer integration test."
    }

    $customerBlock = $authTests.Substring(
        $customerStart,
        $helperStart - $customerStart)

    $legacyCustomerSignin = @'
        var authentication = scope.ServiceProvider.GetRequiredService<IAccountAuthenticationService>();
        var signIn = await authentication.SignInAsync(
            new SignInCommand("0912-000-0001", ValidPassword, null, null),
            new RequestSecurityContext("127.0.0.1", "integration-test", null),
            CancellationToken.None);
        Assert.Equal(SignInStatus.Authenticated, signIn.Status);
        Assert.NotNull(signIn.Tokens);
'@

    $currentCustomerSignin = @'
        var authentication = scope.ServiceProvider.GetRequiredService<IAccountAuthenticationService>();
        await Assert.ThrowsAsync<AuthenticationRejectedException>(() =>
            authentication.SignInAsync(
                new SignInCommand("0912-000-0001", ValidPassword, null, null),
                new RequestSecurityContext("127.0.0.1", "integration-test", null),
                CancellationToken.None));
'@

    if (-not $customerBlock.Contains($legacyCustomerSignin)) {
        throw "Legacy Customer management-signin assertion was not found."
    }

    $customerBlock = $customerBlock.Replace(
        $legacyCustomerSignin,
        $currentCustomerSignin)

    $authTests = $authTests.Remove(
        $customerStart,
        $helperStart - $customerStart)
    $authTests = $authTests.Insert(
        $customerStart,
        $customerBlock)

    # Test helper must create Admin/Employee in the same identifier shape as
    # production: mobile is UserName and the phone is confirmed.
    $helperStart = $authTests.IndexOf(
        'private static async Task<ApplicationUser> CreateUserAsync(',
        [System.StringComparison]::Ordinal)
    $providerStart = $authTests.IndexOf(
        'private static ServiceProvider CreateProvider(',
        $helperStart,
        [System.StringComparison]::Ordinal)

    if ($helperStart -lt 0 -or
        $providerStart -lt 0 -or
        $providerStart -le $helperStart) {
        throw "Could not isolate CreateUserAsync integration-test helper."
    }

    $helperBlock = $authTests.Substring(
        $helperStart,
        $providerStart - $helperStart)

    $oldUserShapePattern =
        '(?s)var user = new ApplicationUser\(\$"\{roleName\} Test User"\)\s*\{\s*Email = \$"\{roleName\.ToLowerInvariant\(\)\}-\{Guid\.NewGuid\(\):N\}@example\.test",\s*UserName = \$"\{roleName\.ToLowerInvariant\(\)\}-\{Guid\.NewGuid\(\):N\}@example\.test",\s*EmailConfirmed = true\s*\};\s*user\.UserName = user\.Email;'

    if (-not [regex]::IsMatch(
            $helperBlock,
            $oldUserShapePattern)) {
        throw "CreateUserAsync legacy test-user shape was not found."
    }

    $newUserShape = @'
var email =
            $"{roleName.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test";
        var isStaff =
            roleName == SecurityRoles.Admin ||
            roleName == SecurityRoles.Employee;
        var phoneNumber = isStaff
            ? "09120000001"
            : null;

        var user = new ApplicationUser($"{roleName} Test User")
        {
            Email = email,
            EmailConfirmed = true,
            UserName = isStaff ? phoneNumber : email,
            PhoneNumber = phoneNumber,
            PhoneNumberConfirmed = isStaff
        };
'@

    $helperBlock = [regex]::Replace(
        $helperBlock,
        $oldUserShapePattern,
        $newUserShape)

    $authTests = $authTests.Remove(
        $helperStart,
        $providerStart - $helperStart)
    $authTests = $authTests.Insert(
        $helperStart,
        $helperBlock)

    Write-Text $AuthenticationTestsPath $authTests

    # Validate test-policy reconciliation before compilation.
    $authTestsCheck = Read-Text $AuthenticationTestsPath

    foreach ($token in @(
        'CreateUserAsync(scope.ServiceProvider, SecurityRoles.Employee)',
        'new SignInCommand(user.PhoneNumber!, ValidPassword, null, null)',
        'claim.Value == SecurityRoles.Employee',
        'Assert.Equal(SecurityRoles.All.Count, await dbContext.Roles.CountAsync());',
        'PeopleDirectory_CreatesPhoneOnlyCustomerWhoCannotUseManagementSignIn',
        'Assert.ThrowsAsync<AuthenticationRejectedException>',
        'PhoneNumberConfirmed = isStaff'
    )) {
        if (-not $authTestsCheck.Contains($token)) {
            throw "Authentication test-policy validation failed: $token"
        }
    }

    # Guard against accidentally restoring the obsolete successful Customer
    # management-login assumptions in these two success-path tests.
    foreach ($methodName in @(
        'SignIn_IssuesHashedRotatingRefreshTokenAndDetectsReuse',
        'AccessTokenValidation_LoadsCurrentRolesAndRejectsRevokedSession'
    )) {
        $methodStart = $authTestsCheck.IndexOf(
            "public async Task $methodName()",
            [System.StringComparison]::Ordinal)
        $methodEnd = $authTestsCheck.IndexOf(
            '[Fact]',
            $methodStart + 10,
            [System.StringComparison]::Ordinal)

        if ($methodStart -lt 0 -or $methodEnd -lt 0) {
            throw "Could not validate Authentication test method: $methodName"
        }

        $methodBlock = $authTestsCheck.Substring(
            $methodStart,
            $methodEnd - $methodStart)

        if ($methodBlock.Contains('SecurityRoles.Customer') -or
            $methodBlock.Contains('new SignInCommand(user.Email!')) {
            throw "Obsolete Customer management-login assumption remains in: $methodName"
        }
    }

    # ------------------------------------------------------------------
    # Structural verification BEFORE compilation.
    # ------------------------------------------------------------------
    $invoiceCheck = Read-Text $InvoiceTemplatePath
    foreach ($token in @(
        'VENDOME_INVOICE_A5_POTRAIT.PDF',
        '@page { size: 148mm 210mm; margin: 0; }',
        'const ITEMS_PER_PAGE = 5',
        'grid-template-rows: repeat(5, 1fr)',
        'top: 45.95%',
        'InvoiceItem',
        'paymentMethodLabel',
        'paymentTrackingReference',
        'number(item.quantity)',
        'optional(item.productName)',
        'optional(item.sku)',
        'number(item.unitPriceRials)',
        'number(item.lineTotalRials)',
        'buildInvoiceDocumentHtml('
    )) {
        if (-not $invoiceCheck.Contains($token)) {
            throw "A5 v10.4 invoice-template validation failed: $token"
        }
    }

    if ($invoiceCheck.Contains('item.wageRials') -or
        $invoiceCheck.Contains('item.taxRials') -or
        $invoiceCheck.Contains('item.profitRials')) {
        throw "README rule violation: wage/tax/profit breakdown was added to the printable invoice."
    }

    $typesCheck = Read-Text $TypesPath
    if ($typesCheck -notmatch 'export\s+interface\s+InvoiceItem\s*\{' -or
        $typesCheck -notmatch 'items:\s*InvoiceItem\[\];') {
        throw "InvoiceItem TypeScript contract validation failed."
    }

    $operationsCheck = Read-Text $OperationsPath
    foreach ($token in @(
        'async function loadInvoiceDocument(invoiceId: string)',
        '/api/v1/invoices/${invoiceId}',
        '/api/v1/payments/${invoice.paymentId}',
        'invoice.items.length === 0',
        'payment.status !== "Verified"',
        'openInvoicePreview(invoice)',
        'const document = await loadInvoiceDocument(invoice.id);',
        'buildInvoiceDocumentHtml(document.invoice, document.payment)',
        'payment={selectedPayment}',
        'if (!selected) setSelectedPayment(null);',
        'buildInvoiceDocumentHtml(invoice, payment)'
    )) {
        if (-not $operationsCheck.Contains($token)) {
            throw "Authoritative invoice-document flow validation failed: $token"
        }
    }

    # Guard the README boundary: this package must not patch backend or payment/order
    # implementations. Only the three client document/type files and A5 renderer are touched.
    $rendererCheck = Read-Text $DesktopRendererPath
    foreach ($token in @(
        'CreateA5InvoicePrintSettings()',
        'settings.PageWidth = 148.0 / 25.4;',
        'settings.PageHeight = 210.0 / 25.4;',
        'settings.ShouldPrintBackgrounds = true;'
    )) {
        if (-not $rendererCheck.Contains($token)) {
            throw "A5 Desktop renderer validation failed: $token"
        }
    }

    Write-Host ""
    Write-Host "v10.4 README/contract validations passed." -ForegroundColor Green

    Write-Host "[1/4] TypeScript check..." -ForegroundColor Cyan
    npm --prefix ".\GoldInvoice.Client" run check
    if ($LASTEXITCODE -ne 0) { throw "TypeScript check failed." }

    Write-Host "[2/4] React production build..." -ForegroundColor Cyan
    Remove-Item ".\GoldInvoice.Client\dist" -Recurse -Force -ErrorAction SilentlyContinue
    npm --prefix ".\GoldInvoice.Client" run build
    if ($LASTEXITCODE -ne 0) { throw "React production build failed." }

    Write-Host "[3/4] Full solution Release build..." -ForegroundColor Cyan
    dotnet build ".\VendomeJewleryInvoiceManagement.sln" -c Release
    if ($LASTEXITCODE -ne 0) { throw "Solution Release build failed." }

    Write-Host "[4/4] Full solution tests..." -ForegroundColor Cyan
    dotnet test ".\VendomeJewleryInvoiceManagement.sln" -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw "Solution tests failed." }

    Write-Host ""
    Write-Host "SUCCESS: VENDOME A5 invoice data mapping v10.4 installed." -ForegroundColor Green
    Write-Host "Invoice items: authoritative snapshot rows are printed (5 rows per A5 page)." -ForegroundColor Green
    Write-Host "Customer/store fields: aligned and typography strengthened." -ForegroundColor Green
    Write-Host "Payment section: verified method/reference/date loaded from existing Payment API." -ForegroundColor Green
    Write-Host "Preview/PDF/Print: all use the same freshly loaded invoice document source." -ForegroundColor Green
    Write-Host "README Phase 7C boundary preserved: no backend persistence/order/payment/print-job implementation changed." -ForegroundColor Green
    Write-Host "Authentication tests: aligned with Owner-email / Staff-mobile / Customer-no-management-login policy." -ForegroundColor Green
    Write-Host "Backup: $BackupRoot"
    Write-Host ""
    Write-Host "Restart GoldInvoice.Api if it was running, reopen Desktop, then preview and print the same invoice." -ForegroundColor Yellow
}
catch {
    Write-Host ""
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Rolling back every v10.4 invoice change..." -ForegroundColor Yellow
    Restore-All
    Write-Host "Rollback completed. Backup remains at: $BackupRoot" -ForegroundColor Yellow
    throw
}
