Vendome Navasan Market + UI Fix v5

هدف:
1) رفع «ثبت نشده» برای دلار تهران خرید/فروش
2) کوچک‌تر و Bold شدن اعداد Market Rail
3) اولویت دادن به IranSans برای اعداد

چرا دلار نمایش داده نمی‌شد:
Vendome هنگام ingestion، ProviderTimestamp قدیمی‌تر از MaximumQuoteAgeMinutes
را Stale می‌کند. نرخ نقدی دلار ممکن است timestamp آخرین بازار را نگه دارد،
درحالی‌که طلای 18 عیار تازه‌تر شده باشد. v5 برای Currency زمان ارائه‌دهنده
را جعل نمی‌کند؛ ProviderTimestamp را null می‌گذارد و CapturedAt واقعیِ
دریافت توسط Vendome همچنان در دیتابیس ثبت می‌شود.

v5 همچنین:
- usd_buy / usd_sell را ترجیح می‌دهد.
- اگر یکی در پاسخ وجود نداشت، از نماد مستند usd برای همان سمت fallback می‌گیرد.
- spread ساختگی ایجاد نمی‌کند.
- hash نسخه Currency را تغییر می‌دهد تا Snapshot نامعتبر v4 مانع ثبت Snapshot
  اصلاح‌شده نشود.
- اگر Currency هنوز usable نباشد، پس از اولین restart یک Poll فوری انجام می‌دهد.
- پس از موفقیت، Poll معمولی همچنان حدود هر 7 ساعت باقی می‌ماند.

فونت:
هیچ فایل فونتی در ZIP وجود ندارد.
CSS اول این نام‌های نصب‌شده روی Windows را امتحان می‌کند:
IRANSansXFaNum, IRANSansX, IRANSans, IRANSansWeb
و اگر روی سیستم نصب نباشند به Vazirmatn برمی‌گردد.

نصب:
powershell -ExecutionPolicy Bypass -File ".\Apply-Navasan-Market-UI-Fix-v5.ps1"

بعد از SUCCESS:
GoldInvoice.Api را یک بار Restart کن و سپس Desktop را دوباره باز کن.
