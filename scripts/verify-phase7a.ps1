param(
    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot

Push-Location $repositoryRoot
try {
    Write-Host "[1/7] Restoring pinned .NET tools"
    dotnet tool restore

    Write-Host "[2/7] Restoring the React client"
    npm --prefix GoldInvoice.Client ci

    Write-Host "[3/7] Checking and building React"
    npm --prefix GoldInvoice.Client run check
    npm --prefix GoldInvoice.Client run build

    Write-Host "[4/7] Restoring the complete solution"
    dotnet restore VendomeJewleryInvoiceManagement.sln

    Write-Host "[5/7] Building the complete solution"
    dotnet build VendomeJewleryInvoiceManagement.sln `
        --configuration $Configuration `
        --no-restore

    Write-Host "[6/7] Running all .NET tests"
    dotnet test VendomeJewleryInvoiceManagement.sln `
        --configuration $Configuration `
        --no-build

    Write-Host "[7/7] Listing committed migrations (read-only)"
    dotnet ef migrations list `
        --project GoldInvoice.Infrastructure `
        --startup-project GoldInvoice.Infrastructure `
        --context GoldInvoiceDbContext

    Write-Host "Phase 7A verification commands completed successfully."
}
finally {
    Pop-Location
}
