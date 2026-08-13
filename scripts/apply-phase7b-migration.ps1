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

    # GoldInvoiceDbContextFactory reads this exact environment variable. Setting
    # it explicitly prevents EF from falling back to VendomeGoldInvoiceDesignTime.
    $env:ConnectionStrings__GoldInvoice = $resolvedConnectionString
    Write-Host "Restoring the pinned EF Core tool"
    Invoke-Checked dotnet tool restore

    Write-Host "Applying the additive Phase 7B migration to the explicitly resolved database"
    Invoke-Checked dotnet ef database update `
        --project GoldInvoice.Infrastructure `
        --startup-project GoldInvoice.Infrastructure `
        --context GoldInvoiceDbContext

    Write-Host "Database migration completed successfully."
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
