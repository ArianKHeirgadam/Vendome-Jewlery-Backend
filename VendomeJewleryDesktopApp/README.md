# VendomeJewleryDesktopApp

Full-screen Windows host for the shared `GoldInvoice.Client` React UI.

## Runtime behavior

- `MainWindow` hosts the local production build with WebView2 at the isolated
  virtual origin `https://desktop.vendome.invalid`.
- `F11` toggles full-screen mode; `Esc` returns to a resizable window.
- Navigation outside the bundled host is blocked, new windows are denied, and
  production builds disable DevTools.
- Authentication messages are accepted only from the trusted virtual origin.
- WPF brokers login, refresh, MFA setup/enable, and logout calls to the selected
  `GoldInvoice.Api` endpoint.
- The rotating refresh token is encrypted with Windows DPAPI using the current
  Windows account. It is never returned to React or written to browser storage.
- API settings and encrypted secrets live under the current user's local
  application-data directory, outside the repository.

The API URL defaults to `https://localhost:7156`. The login screen can change
it to another HTTPS address, loopback HTTP address, or temporary private-LAN
HTTP address. Use HTTPS for production.

## Build

Open `VendomeJewleryInvoiceManagement.sln` in Visual Studio 2022, configure the
API's connection string and JWT signing key, and start both:

1. `GoldInvoice.Api`
2. `VendomeJewleryDesktopApp`

If `GoldInvoice.Client/node_modules` exists, a Desktop build first runs the
React production build. Otherwise it uses the checked `GoldInvoice.Client/dist`
output included in the delivery package.

The desktop project discovers `dist` files only after Vite finishes. Never add
the hashed `dist/assets/index-*` files as evaluation-time `Content` items;
their names are intentionally replaced whenever the client output changes.
