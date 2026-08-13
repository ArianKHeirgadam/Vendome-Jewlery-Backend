param(
    [ValidateSet("Development", "Production")]
    [string] $Environment = "Development",

    [string] $ConnectionString = ""
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
$previousConnectionString = $env:ConnectionStrings__GoldInvoice

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
    $requiredPhase8Files = @(
        "GoldInvoice.Domain\Business\BusinessModels.cs",
        "GoldInvoice.Infrastructure\Inventory\SupplierPurchaseService.cs",
        "GoldInvoice.Infrastructure\Persistence\Migrations\20260813103000_AddPhase8SupplierPurchasesAndProfitSnapshots.cs"
    )
    foreach ($requiredFile in $requiredPhase8Files) {
        if (-not (Test-Path (Join-Path $repositoryRoot $requiredFile))) {
            throw "The Phase 8 source tree is incomplete. Extract the complete Hotfix package into a new folder. Missing: $requiredFile"
        }
    }

    $env:ASPNETCORE_ENVIRONMENT = $Environment
    $resolvedConnectionString = $ConnectionString
    if ([string]::IsNullOrWhiteSpace($resolvedConnectionString)) {
        $resolvedConnectionString = $env:ConnectionStrings__GoldInvoice
    }

    if ([string]::IsNullOrWhiteSpace($resolvedConnectionString) -and $Environment -eq "Development") {
        $secretPrefix = "ConnectionStrings:GoldInvoice = "
        $secretLines = & dotnet user-secrets list --project GoldInvoice.Api 2>$null
        if ($LASTEXITCODE -eq 0) {
            $connectionSecret = $secretLines |
                Where-Object { $_.StartsWith($secretPrefix, [StringComparison]::Ordinal) } |
                Select-Object -First 1
            if ($null -ne $connectionSecret) {
                $resolvedConnectionString = $connectionSecret.Substring($secretPrefix.Length)
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($resolvedConnectionString) -and $Environment -eq "Development") {
        $developmentSettingsPath = Join-Path $repositoryRoot "GoldInvoice.Api\appsettings.Development.json"
        $developmentSettings = Get-Content $developmentSettingsPath -Raw | ConvertFrom-Json
        $resolvedConnectionString = $developmentSettings.ConnectionStrings.GoldInvoice
    }

    if ([string]::IsNullOrWhiteSpace($resolvedConnectionString)) {
        throw "Set ConnectionStrings__GoldInvoice or pass -ConnectionString for the intended database."
    }

    $env:ConnectionStrings__GoldInvoice = $resolvedConnectionString
    Write-Host "Restoring the pinned EF Core tool"
    Invoke-Checked dotnet tool restore

    Write-Host "Restoring the Phase 8 infrastructure project"
    Invoke-Checked dotnet restore GoldInvoice.Infrastructure\GoldInvoice.Infrastructure.csproj

    Write-Host "Building the Phase 8 infrastructure project"
    Invoke-Checked dotnet build GoldInvoice.Infrastructure\GoldInvoice.Infrastructure.csproj `
        --configuration Release `
        --no-restore

    Write-Host "Applying the additive Phase 8 supplier purchase and profit migration"
    Invoke-Checked dotnet ef database update 20260813103000_AddPhase8SupplierPurchasesAndProfitSnapshots `
        --project GoldInvoice.Infrastructure `
        --startup-project GoldInvoice.Infrastructure `
        --context GoldInvoiceDbContext `
        --configuration Release `
        --no-build

    Write-Host "Phase 8 database migration completed successfully."
}
finally {
    if ($null -eq $previousEnvironment) {
        Remove-Item Env:ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue
    }
    else {
        $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
    }

    if ($null -eq $previousConnectionString) {
        Remove-Item Env:ConnectionStrings__GoldInvoice -ErrorAction SilentlyContinue
    }
    else {
        $env:ConnectionStrings__GoldInvoice = $previousConnectionString
    }

    Pop-Location
}
