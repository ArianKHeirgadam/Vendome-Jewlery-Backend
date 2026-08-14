param(
    [string]$Root = (Get-Location).Path
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$utf8NoBom = New-Object System.Text.UTF8Encoding -ArgumentList $false

function Read-Utf8([string]$Path) {
    return [System.IO.File]::ReadAllText($Path, $utf8NoBom)
}

function Write-Utf8([string]$Path, [string]$Content) {
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

function Replace-FirstExact([string]$Text, [string]$Old, [string]$New, [string]$Label) {
    $idx = $Text.IndexOf($Old, [System.StringComparison]::Ordinal)
    if ($idx -lt 0) {
        throw "Anchor not found: $Label"
    }
    return $Text.Substring(0, $idx) + $New + $Text.Substring($idx + $Old.Length)
}

function Ensure-RegexOnce([string]$Text, [string]$Pattern, [string]$Replacement, [string]$Label) {
    $matches = [regex]::Matches($Text, $Pattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if ($matches.Count -ne 1) {
        throw "Expected exactly 1 match for '$Label', found $($matches.Count)."
    }
    return [regex]::Replace(
        $Text,
        $Pattern,
        $Replacement,
        [System.Text.RegularExpressions.RegexOptions]::Singleline,
        [TimeSpan]::FromSeconds(2)
    )
}

$operationsPath = Join-Path $Root 'GoldInvoice.Client\src\features\operations\OperationsPages.tsx'
$supplierPath   = Join-Path $Root 'GoldInvoice.Client\src\features\operations\SupplierPurchasesPage.tsx'
$stylesPath     = Join-Path $Root 'GoldInvoice.Client\src\styles.css'

foreach ($path in @($operationsPath, $supplierPath, $stylesPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required file not found: $path"
    }
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupDir = Join-Path $Root ".phase9b-backup-$stamp"
New-Item -ItemType Directory -Path $backupDir | Out-Null

$backups = @{}
foreach ($path in @($operationsPath, $supplierPath, $stylesPath)) {
    $backup = Join-Path $backupDir ([System.IO.Path]::GetFileName($path))
    Copy-Item -LiteralPath $path -Destination $backup -Force
    $backups[$path] = $backup
}

try {
    # ---------------------------------------------------------------------
    # 1) Supplier page: use the same page shell as the rest of operations.
    # ---------------------------------------------------------------------
    $supplier = Read-Utf8 $supplierPath
    if ($supplier.Contains('<div className="module-page">')) {
        $supplier = Replace-FirstExact $supplier '<div className="module-page">' '<div className="module-main">' 'Supplier root module-page'
    }
    elseif (-not $supplier.Contains('<div className="module-main">')) {
        throw 'Supplier page root wrapper is neither module-page nor module-main.'
    }
    Write-Utf8 $supplierPath $supplier

    # ---------------------------------------------------------------------
    # 2) OperationsPages: current StoreProfile for old invoice preview/PDF/print.
    #    No line numbers; only stable function anchors.
    # ---------------------------------------------------------------------
    $ops = Read-Utf8 $operationsPath

    if (-not $ops.Contains('function invoiceWithCurrentStoreProfile(')) {
        $helperAnchor = 'function invoiceActualProfit(invoice: Invoice): number {'
        if (-not $ops.Contains($helperAnchor)) {
            throw 'Could not find invoiceActualProfit anchor in OperationsPages.tsx.'
        }

        $helper = @'
function invoiceWithCurrentStoreProfile(
  invoice: Invoice,
  storeProfile: StoreProfile | null,
): Invoice {
  if (!storeProfile) return invoice;

  return {
    ...invoice,
    store: {
      id: invoice.store?.id ?? "current-store-profile",
      tradeName: storeProfile.tradeName,
      legalName: storeProfile.legalName,
      nationalId: storeProfile.nationalId,
      economicCode: storeProfile.economicCode,
      registrationNumber: storeProfile.registrationNumber,
      phoneNumber: storeProfile.phoneNumber,
      postalCode: storeProfile.postalCode,
      addressLine: storeProfile.addressLine,
    },
  };
}

'@
        $ops = Replace-FirstExact $ops $helperAnchor ($helper + $helperAnchor) 'invoiceActualProfit insertion point'
    }

    # Ensure StoreProfile is available as a type. In the current file it already is,
    # but this makes the script resilient if import ordering changed.
    if (-not [regex]::IsMatch($ops, '(?s)import\s+type\s*\{.*?\bStoreProfile\b.*?\}\s+from\s+"\.\/operations\.types";')) {
        $ops = Ensure-RegexOnce $ops '(ProductVariant,\s*\r?\n)(\s*)(Supplier,)' ('$1$2StoreProfile,' + [Environment]::NewLine + '$2$3') 'StoreProfile type import'
    }

    # Patch only the openDesktopDocument function body.
    if (-not [regex]::IsMatch($ops, '(?s)const\s+openDesktopDocument\s*=\s*async\s*\(.*?invoiceWithCurrentStoreProfile\(invoice,\s*data\.storeProfile\)')) {
        $openStart = $ops.IndexOf('const openDesktopDocument = async (', [System.StringComparison]::Ordinal)
        $openEnd   = $ops.IndexOf('const savePdf = async', $openStart, [System.StringComparison]::Ordinal)
        if ($openStart -lt 0 -or $openEnd -lt 0) {
            throw 'Could not isolate openDesktopDocument in OperationsPages.tsx.'
        }
        $before = $ops.Substring(0, $openStart)
        $block  = $ops.Substring($openStart, $openEnd - $openStart)
        $after  = $ops.Substring($openEnd)

        $block = Ensure-RegexOnce $block '(copies\s*=\s*1,\s*\r?\n\s*\)\s*=>\s*\{\s*\r?\n)' ('$1' + '    invoice = invoiceWithCurrentStoreProfile(invoice, data.storeProfile);' + [Environment]::NewLine + [Environment]::NewLine) 'openDesktopDocument function body'

        $ops = $before + $block + $after
    }

    # Patch only InvoiceDetails so the on-screen preview also uses current store data.
    if (-not [regex]::IsMatch($ops, '(?s)function\s+InvoiceDetails\(.*?invoiceWithCurrentStoreProfile\(invoice,\s*data\.storeProfile\)')) {
        $detailsStart = $ops.IndexOf('function InvoiceDetails({', [System.StringComparison]::Ordinal)
        $detailsEnd   = $ops.IndexOf('type AddressMode', $detailsStart, [System.StringComparison]::Ordinal)
        if ($detailsStart -lt 0 -or $detailsEnd -lt 0) {
            throw 'Could not isolate InvoiceDetails in OperationsPages.tsx.'
        }
        $before = $ops.Substring(0, $detailsStart)
        $block  = $ops.Substring($detailsStart, $detailsEnd - $detailsStart)
        $after  = $ops.Substring($detailsEnd)

        $block = Ensure-RegexOnce $block '(\}\)\s*\{\s*\r?\n)(\s*return\s*\()' ('$1' + '  const { data } = useOperations();' + [Environment]::NewLine + '  invoice = invoiceWithCurrentStoreProfile(invoice, data.storeProfile);' + [Environment]::NewLine + [Environment]::NewLine + '$2') 'InvoiceDetails function body'

        $ops = $before + $block + $after
    }

    Write-Utf8 $operationsPath $ops

    # ---------------------------------------------------------------------
    # 3) CSS: dark-mode contrast + supplier wizard viewport/scroll safety.
    # ---------------------------------------------------------------------
    $css = Read-Utf8 $stylesPath

    # Fix the actual primary button rule without relying on a line number.
    $primaryStart = $css.IndexOf('.primary-button {', [System.StringComparison]::Ordinal)
    if ($primaryStart -lt 0) {
        throw 'Could not find .primary-button rule in styles.css.'
    }
    $primaryEnd = $css.IndexOf('}', $primaryStart, [System.StringComparison]::Ordinal)
    if ($primaryEnd -lt 0) {
        throw 'Could not find end of .primary-button rule.'
    }
    $primaryBlock = $css.Substring($primaryStart, $primaryEnd - $primaryStart + 1)
    if ($primaryBlock.Contains('color: var(--background);')) {
        $newPrimaryBlock = $primaryBlock.Replace('color: var(--background);', 'color: var(--navy-ink);')
        $css = $css.Substring(0, $primaryStart) + $newPrimaryBlock + $css.Substring($primaryEnd + 1)
    }

    $marker = '/* Phase 9B safe UI fixes */'
    if (-not $css.Contains($marker)) {
        $css += @'

/* Phase 9B safe UI fixes */
/* Supplier wizard: keep the dialog within the viewport and make its body scrollable. */
.modal-layer:has(.purchase-wizard) {
  align-items: start;
  overflow-y: auto;
  overscroll-behavior: contain;
}

.modal-card:has(.purchase-wizard) {
  display: flex;
  width: min(940px, 100%);
  max-height: calc(100dvh - 24px);
  min-height: 0;
  flex-direction: column;
  margin-block: 12px;
}

.modal-card:has(.purchase-wizard) > header {
  flex: 0 0 auto;
}

.modal-card:has(.purchase-wizard) > .modal-body {
  min-height: 0;
  max-height: none;
  flex: 1 1 auto;
  overflow-x: hidden;
  overflow-y: auto;
  overscroll-behavior: contain;
  scrollbar-gutter: stable;
}

.purchase-wizard {
  min-width: 0;
  min-height: 0;
}

.purchase-wizard .wizard-panel {
  min-width: 0;
  min-height: min(330px, 42vh);
  max-height: min(520px, 52vh);
  overflow-y: auto;
  overscroll-behavior: contain;
}

/* Dark mode: keep primary/selected actions visibly separated from the background. */
:root[data-theme="dark"] .primary-button {
  color: #fffdf8;
  background: linear-gradient(135deg, #29496f, #203b5f);
  border: 1px solid color-mix(in oklch, var(--gold) 26%, #29496f);
}

:root[data-theme="dark"] .primary-button:hover:not(:disabled) {
  color: #ffffff;
  background: linear-gradient(135deg, #355c86, #29496f);
  border-color: color-mix(in oklch, var(--gold) 46%, #355c86);
}

:root[data-theme="dark"] .wizard-steps .is-current button > span,
:root[data-theme="dark"] .wizard-mode-switch button.is-active,
:root[data-theme="dark"] .reference-tabs button.is-active {
  color: #fffdf8;
  background: color-mix(in oklch, var(--navy-soft) 82%, var(--gold) 18%);
  border-color: color-mix(in oklch, var(--gold) 40%, var(--border));
}

@media (max-height: 760px) {
  .modal-layer:has(.purchase-wizard) {
    padding-block: 8px;
  }

  .modal-card:has(.purchase-wizard) {
    max-height: calc(100dvh - 16px);
    margin-block: 0;
  }

  .purchase-wizard .wizard-panel {
    min-height: 0;
    max-height: 46vh;
  }
}

@media (max-width: 760px) {
  .purchase-wizard .wizard-steps {
    grid-template-columns: repeat(3, minmax(0, 1fr));
    row-gap: 12px;
  }

  .purchase-wizard .wizard-steps li:not(:last-child)::after {
    display: none;
  }
}
'@
    }

    Write-Utf8 $stylesPath $css

    # ---------------------------------------------------------------------
    # Validation. Roll back automatically if whitespace validation fails.
    # ---------------------------------------------------------------------
    Push-Location $Root
    try {
        & git diff --check
        if ($LASTEXITCODE -ne 0) {
            throw 'git diff --check failed.'
        }
    }
    finally {
        Pop-Location
    }

    Write-Host ''
    Write-Host 'Phase 9B safe fix applied successfully.' -ForegroundColor Green
    Write-Host "Backup: $backupDir"
    Write-Host ''
    Write-Host 'Changed files:'
    Write-Host '  GoldInvoice.Client/src/features/operations/SupplierPurchasesPage.tsx'
    Write-Host '  GoldInvoice.Client/src/features/operations/OperationsPages.tsx'
    Write-Host '  GoldInvoice.Client/src/styles.css'
    Write-Host ''
    Write-Host 'Next:'
    Write-Host '  cd GoldInvoice.Client'
    Write-Host '  npm run check'
    Write-Host '  npm run build'
}
catch {
    Write-Host ''
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'Restoring original files from backup...' -ForegroundColor Yellow
    foreach ($path in $backups.Keys) {
        Copy-Item -LiteralPath $backups[$path] -Destination $path -Force
    }
    Write-Host 'Rollback completed. Your source files were restored.' -ForegroundColor Yellow
    throw
}
