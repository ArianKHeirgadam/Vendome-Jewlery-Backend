# Vendome — Navasan Market API integration

این بسته برای Rail «بازار زنده» ساخته شده و منطق موجود MarketPriceSource / MarketPriceSnapshot را دور نمی‌زند.

## چه چیزهایی وصل می‌شوند
- طلای ۱۸ عیار: مستقیم از نماد `18ayar`
- طلای ۲۴ عیار: محاسبه‌شده از ۱۸ عیار با نسبت 24/18 و در UI با برچسب «محاسباتی»
- خرید/فروش طلای ۱۸: چون Navasan برای `18ayar` یک نرخ مرجع می‌دهد، Buy/Sell هر دو همان نرخ مرجع ذخیره می‌شوند؛ Spread ساختگی تولید نمی‌شود.
- دلار تهران خرید/فروش: مستقیم از `usd_buy` و `usd_sell` و تبدیل تومان→ریال برای ذخیره استاندارد پروژه
- نمودار روند: از تاریخچه Snapshotهای ذخیره‌شده در دیتابیس خوانده می‌شود و API خارجی جداگانه مصرف نمی‌کند.
- SignalR/Outbox موجود پروژه بعد از ذخیره Snapshot همچنان کار می‌کند.

## سهمیه رایگان
بسته به‌طور پیش‌فرض هر 420 دقیقه (7 ساعت) Poll می‌کند تا از سهمیه 120 درخواست ماهانه فاصله امن‌تری داشته باشد.
هر Poll فقط یک درخواست `latest` می‌زند و چند نرخ را از همان JSON استخراج می‌کند.
Restart API باعث مصرف فوری مجدد نمی‌شود چون LastSuccessfulFetchAt دیتابیس بررسی می‌شود.

## نصب
1. ZIP را Extract کن.
2. PowerShell:
   `powershell -ExecutionPolicy Bypass -File .\Apply-Navasan-Market-API.ps1`
3. سپس:
   `powershell -ExecutionPolicy Bypass -File .\Set-Navasan-Key.ps1`
4. API را کامل Stop/Start کن.
5. Desktop را باز کن.

## Secret
API key داخل Git یا appsettings ذخیره نمی‌شود؛ با `dotnet user-secrets` نگهداری می‌شود.

## HTTPS
کد عمداً فقط HTTPS را قبول می‌کند تا API key روی HTTP ساده ارسال نشود.
اگر `https://api.navasan.tech/latest/` از سمت سرویس‌دهنده در محیط شما پشتیبانی نشود، کد به HTTP downgrade نمی‌کند.

## Rollback
Installer قبل از هر تغییر در:
`.vendome-backups\navasan-market-YYYYMMDD-HHMMSS`
نسخه پشتیبان می‌سازد و اگر build/check شکست بخورد، خودکار فایل‌های لمس‌شده را برمی‌گرداند.
