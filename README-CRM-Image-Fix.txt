Vendome CRM Remove + Product Image Display Fix v1

What this installer changes:
1. Removes the "ارتباط با مشتری" item from the desktop sidebar.
2. Removes /crm routing from the desktop UI.
3. Removes "یادداشت‌ها" and "ارتباطات" shortcuts from customer cards.
4. Fixes product image rendering in WebView by allowing blob: in the client CSP.
5. Makes ProductPhoto fetch the real image bytes with Accept: image/* and no-store.
6. Refetches the image when ProductImage.rowVersion changes.

Important:
- CRM backend/database data is NOT deleted.
- Product image backend storage is NOT replaced. The app already has real local file storage.
- No database migration is required.
- The installer backs up all modified files and rolls them back automatically if validation/build fails.

Run from the repository root after extracting:
powershell -NoProfile -ExecutionPolicy Bypass -File ".\Apply-CRM-Remove-Product-Image-Fix.ps1"
