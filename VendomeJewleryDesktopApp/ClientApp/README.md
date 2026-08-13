# GoldInvoice.Client

Shared React + TypeScript presentation layer for the Vendome website and the
Desktop host. The current scope is intentionally limited to the Persian Home /
Dashboard experience.

The visual contract follows the Persian dashboard at
`https://vendome-lumina-suite.lovable.app/fa`: fixed 64px navy header, 240px
left navigation, 320px live-market rail, ivory canvas, 12px luxury cards,
muted-gold accents, local Vazirmatn typography, and the same dashboard content
hierarchy. Both `/` and `/dashboard` render this page.

## Architecture boundary

- The client never connects directly to SQL Server.
- It does not contain a database connection string, JWT signing key, provider
  credential, or authoritative pricing calculation.
- Dashboard data currently comes from a typed local fixture. A later adapter
  can replace that fixture with the existing authenticated `.NET API` without
  changing the page components.
- SignalR will be treated as a post-commit hint. Authoritative state will still
  be recovered from the API, matching the backend roadmap.
- Desktop device enrollment and printing remain Phase 7 work and are not
  implemented by this visual slice.

## Commands

```bash
npm install
npm run check
npm run build
npm run dev
```

The Vite build uses `base: "./"`, so the generated `dist` is hosted both by the
Desktop WebView shell and by a regular web host. Fonts and icons are bundled
locally; the dashboard does not need internet access at runtime.

`VendomeJewleryDesktopApp` now hosts this build through WebView2 at the private
virtual origin `https://desktop.vendome.example`. Desktop navigation uses a hash so a
refresh never asks the local host for a non-existent physical route. The same
React source keeps normal paths when it is deployed as the website.
