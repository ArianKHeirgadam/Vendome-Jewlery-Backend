$ErrorActionPreference = "Stop"
$Root = "C:\Users\Arian\source\repos\VendomeJewleryInvoiceManagement"
$key = Read-Host "Paste Navasan API key"
if ([string]::IsNullOrWhiteSpace($key)) { throw "API key is empty." }

dotnet user-secrets set "Navasan:ApiKey" $key --project "$Root\GoldInvoice.Api\GoldInvoice.Api.csproj"
if ($LASTEXITCODE -ne 0) { throw "Saving API key failed." }

dotnet user-secrets set "Navasan:BaseUrl" "http://api.navasan.tech/latest/" --project "$Root\GoldInvoice.Api\GoldInvoice.Api.csproj"
if ($LASTEXITCODE -ne 0) { throw "Saving BaseUrl failed." }

Write-Host "Navasan API key saved to .NET User Secrets." -ForegroundColor Green
Write-Host "The key was not written into the repository." -ForegroundColor Green
