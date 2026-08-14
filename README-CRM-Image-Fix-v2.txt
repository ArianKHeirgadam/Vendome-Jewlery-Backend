Vendome CRM Remove + Product Image Fix v2

Why v1 failed:
The repository already had unrelated trailing whitespace in OperationsPages.tsx.
v1 treated `git diff --check` as fatal, so it rolled back even though the requested changes themselves were valid.

v2:
- does NOT fail on pre-existing whitespace
- validates the exact requested source changes
- runs TypeScript check
- runs Vite production build
- validates the built CSP
- runs Desktop .NET build
- auto-rolls back source files if any real validation/build step fails

Changes:
1. Remove CRM from desktop sidebar.
2. Remove /crm desktop route.
3. Remove CRM shortcuts from customer cards.
4. Allow blob: images in CSP.
5. Fetch product image bytes with Accept image/* and cache no-store.
6. Refetch product image when rowVersion changes.

Backend CRM data is NOT deleted.
Existing product image storage is preserved.
No database migration is required.

Run:
powershell -NoProfile -ExecutionPolicy Bypass -File ".\Apply-CRM-Remove-Product-Image-Fix-v2.ps1"
