param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string] $FilePath,
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]] $Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

Push-Location $repositoryRoot
try {
    Write-Host "[1/9] Restoring pinned .NET tools"
    Invoke-Checked dotnet tool restore

    Write-Host "[2/9] Restoring the React client"
    Invoke-Checked npm --prefix GoldInvoice.Client ci

    Write-Host "[3/9] Checking, building, and auditing React"
    Invoke-Checked npm --prefix GoldInvoice.Client run check
    Invoke-Checked npm --prefix GoldInvoice.Client run build
    Invoke-Checked npm --prefix GoldInvoice.Client audit

    Write-Host "[4/9] Restoring the complete solution"
    Invoke-Checked dotnet restore VendomeJewleryInvoiceManagement.sln -warnaserror

    Write-Host "[5/9] Building the complete solution with warnings treated as errors"
    Invoke-Checked dotnet build VendomeJewleryInvoiceManagement.sln `
        --configuration $Configuration `
        --no-restore `
        -warnaserror

    Write-Host "[6/9] Running all .NET tests"
    Invoke-Checked dotnet test VendomeJewleryInvoiceManagement.sln `
        --configuration $Configuration `
        --no-build

    Write-Host "[7/9] Publishing the desktop application as a deployment smoke test"
    Invoke-Checked dotnet publish VendomeJewleryDesktopApp\VendomeJewleryDesktopApp.csproj `
        --configuration $Configuration `
        --no-build `
        --no-restore

    $publishedClientEntryPoint = Join-Path $repositoryRoot "VendomeJewleryDesktopApp\bin\$Configuration\net8.0-windows\publish\ClientApp\dist\index.html"
    if (-not (Test-Path $publishedClientEntryPoint -PathType Leaf)) {
        throw "Published React entry point was not found: $publishedClientEntryPoint"
    }

    Write-Host "[8/9] Listing committed migrations (read-only)"
    $migrationList = & dotnet ef migrations list `
        --project GoldInvoice.Infrastructure `
        --startup-project GoldInvoice.Infrastructure `
        --context GoldInvoiceDbContext `
        --no-connect
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: dotnet ef migrations list"
    }
    $migrationList | Write-Host

    Write-Host "[9/9] Verifying the Phase 7B and Phase 8 migrations are present"
    if (($migrationList -join "`n") -notmatch "AddPhase7BusinessDirectories") {
        throw "The Phase 7B migration was not discovered."
    }

    if (($migrationList -join "`n") -notmatch "AddPhase8SupplierPurchasesAndProfitSnapshots") {
        throw "The Phase 8 migration was not discovered."
    }

    Write-Host "Phase 7B/8 verification completed successfully."
}
finally {
    Pop-Location
}
