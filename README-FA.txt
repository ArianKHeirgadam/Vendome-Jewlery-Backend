Vendome — A5 Invoice Authoritative Data Mapping v10.4

هدف
===
رفع خالی ماندن اقلام و بخش پرداخت در قالب تأییدشده VENDOME_INVOICE_A5_POTRAIT.PDF،
بدون خروج از Clean Architecture و قوانین Phase 7C README.

علت مشکل v10.1
==============
1) قالب تصویری A5 واقعاً 5 ردیف دیتای کالا دارد، ولی v10.1 overlay را روی 6 ردیف تنظیم کرده بود
   و ناحیه داده را از داخل header شروع می‌کرد. در نتیجه متن اقلام درست روی ردیف‌های قالب نمی‌نشست.
2) صفحه Invoices از object موجود در لیست data.invoices مستقیماً سند می‌ساخت. v10.4 قبل از
   Preview/PDF/Print، جزئیات authoritative همان Invoice را از GET /api/v1/invoices/{id}
   دوباره می‌گیرد و اگر اقلام snapshot وجود نداشته باشند، به‌جای چاپ سند ناقص متوقف می‌شود.
3) Client قبلاً Invoice.items را اشتباهاً OrderItem[] تایپ کرده بود. Backend قرارداد جداگانه
   InvoiceItemResponse با LineNumber دارد. v10.4 TypeScript را با همین قرارداد موجود هم‌راستا می‌کند.
4) بخش اطلاعات پرداخت اکنون از Payment موجود و مرتبط با Invoice خوانده می‌شود:
   GET /api/v1/payments/{paymentId}. هیچ شماره پیگیری یا تاریخ ساختگی تولید نمی‌شود.

قوانین README که حفظ می‌شوند
=============================
- هیچ جدول، Migration یا Backend persistence جدیدی اضافه نمی‌شود.
- هیچ Snapshot مالی/کالا/فروشگاه برای چاپ از catalog زنده بازسازی نمی‌شود.
- اقلام دقیقاً از Invoice snapshot موجود می‌آیند.
- مسیر Order / Payment issuance / Print Job عوض نمی‌شود.
- Preview / PDF / Print همگی یک buildInvoiceDocumentHtml مشترک دارند.
- breakdown اجرت/سود/مالیات در سند چاپی اضافه نمی‌شود.
- اصلاح buyer/address همچنان همان audited endpoint Phase 7C را دارد.

نمایش اقلام
===========
هر ردیف چاپی:
- ردیف
- SKU
- نام محصول
- نام/مدل/تنوع
- عیار
- وزن ناخالص
- وزن طلای خالص
- تعداد
- قیمت واحد
- مبلغ کل ردیف

صفحه‌بندی: 5 ردیف در هر صفحه A5. اقلام بیشتر به صفحه بعد می‌روند.

اطلاعات پرداخت
==============
از Payment verified موجود:
- روش پرداخت
- شماره پیگیری/Reference در صورتی که PSP/manual semantic reference واقعی داشته باشد
- تاریخ پرداخت verifiedAt

برای Cash با reference مصنوعی MANUAL-* شماره پیگیری جعلی چاپ نمی‌شود.

نصب
====
powershell -NoProfile -ExecutionPolicy Bypass -File ".\Apply-Vendome-A5-Invoice-v10.4.ps1"

Installer:
- Backup و rollback دارد.
- TypeScript check
- React production build
- Full solution Release build
- Full solution tests مطابق README

بعد از SUCCESS، API/Desktop را دوباره اجرا و همان فاکتور را Preview/Print کن.


اصلاح ویژه v10.4
----------------
v10.2 به شکل دقیق تابع closePreview وابسته بود. این وابستگی برخلاف هدف patch
سازگار با شاخه‌های محلی بود و اگر closePreview کمی refactor شده بود، installer
قبل از build متوقف می‌شد.

v10.4:
- دیگر closePreview را patch نمی‌کند.
- selectedPayment با useEffect و state واقعی selected پاک می‌شود.
- openDesktopDocument به‌صورت مستقل isolate می‌شود.
- Preview، PDF و Print هر سه قبل از document generation جزئیات authoritative
  فاکتور و Payment را مجدداً از API می‌گیرند.
- bug پنهان v10.2 نیز رفع شده: وجود `const document = ...` در helper پیش‌نمایش
  دیگر باعث نمی‌شود patch مربوط به Save/Print اشتباهاً skip شود.
- هیچ endpoint، persistence، Order، Payment، Invoice issuance یا PrintJob
  orchestration تغییر نمی‌کند؛ مرز Phase 7C حفظ می‌شود.


اصلاح Integration Testها در v10.4
---------------------------------
چهار Fail ثبت‌شده مربوط به Invoice نبودند. تست‌های Authentication هنوز سیاست
قدیمی را assert می‌کردند.

سیاست فعلی برنامه:
- Owner: ایمیل + MFA مالک
- Admin/Employee: موبایل + رمز، بدون Authenticator
- Customer: حساب مشتری است و management-desktop login ندارد
- Roleها: Owner/Admin/Employee/Customer

اصلاح تست:
- refresh-token rotation با Employee/mobile
- access-token validation با Employee/mobile و claim=Employee
- Role count از SecurityRoles.All.Count
- phone-only Customer ساخته می‌شود ولی management sign-in آن باید reject شود
- test helper برای Admin/Employee موبایل را UserName و PhoneNumberConfirmed=true می‌سازد

Production authentication برای پاس شدن تست تغییر یا ضعیف نمی‌شود.
تغییرات A5 Invoice v10.3 نیز در همین بسته حفظ شده‌اند.
