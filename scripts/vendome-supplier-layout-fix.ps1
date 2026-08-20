$ErrorActionPreference = 'Stop'
Set-Location 'C:\Users\Arian\source\repos\VendomeJewleryInvoiceManagement'
Get-Process VendomeJewleryDesktopApp -ErrorAction SilentlyContinue | Stop-Process -Force

git restore --source=HEAD -- 'GoldInvoice.Client/src/features/operations/SupplierPurchasesPage.tsx'

$p = 'GoldInvoice.Client/src/features/operations/SupplierPurchasesPage.tsx'
$enc = New-Object System.Text.UTF8Encoding($false)
$s = [System.IO.File]::ReadAllText($p, $enc)

if (-not $s.Contains('تأمین‌کنندگان و خرید')) {
    throw 'نسخه سالم فارسی از Git برنگشته؛ ادامه ندادم.'
}
if (-not $s.Contains('className="module-page"')) {
    throw 'module-page پیدا نشد؛ ادامه ندادم.'
}

$s = $s.Replace('className="module-page"', 'className="module-main"')
[System.IO.File]::WriteAllText($p, $s, $enc)

git diff -- 'GoldInvoice.Client/src/features/operations/SupplierPurchasesPage.tsx'

Remove-Item '.\GoldInvoice.Client\dist' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item '.\VendomeJewleryDesktopApp\bin\Debug\net8.0-windows\ClientApp\dist' -Recurse -Force -ErrorAction SilentlyContinue

npm --prefix '.\GoldInvoice.Client' run check
npm --prefix '.\GoldInvoice.Client' run build

dotnet build '.\VendomeJewleryDesktopApp\VendomeJewleryDesktopApp.csproj' -c Debug
Start-Process '.\VendomeJewleryDesktopApp\bin\Debug\net8.0-windows\VendomeJewleryDesktopApp.exe'
