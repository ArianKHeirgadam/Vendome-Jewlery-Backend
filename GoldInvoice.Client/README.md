# GoldInvoice.Client

Shared React + TypeScript presentation layer for the Vendome website and the
Windows Desktop host.

The visual contract follows the Persian dashboard at
`https://vendome-lumina-suite.lovable.app/fa`: fixed 64px navy header, 240px
left navigation, 320px live-market rail, ivory canvas, muted-gold accents,
local Vazirmatn typography, and RTL content. `/`, `/dashboard`, `/fa`, and the
Desktop hash route render the same Home/Dashboard.

## Phase 7A behavior

- Password login uses `POST /api/v1/auth/login`.
- Owner/Admin MFA-required and first-login MFA-enrollment states are supported.
- `/api/v1/auth/me` replaces the fixture profile after authentication.
- Access tokens stay in React memory and are refreshed before expiration.
- In Desktop, WPF performs login/refresh/MFA calls and stores the rotating
  refresh token with Windows DPAPI; JavaScript never receives that token.
- The regular website fallback keeps its refresh token in `sessionStorage`,
  never `localStorage`. A production website can later move this boundary to a
  same-origin BFF/HttpOnly-cookie deployment without changing page components.
- SignalR connects to `/hubs/integration`, de-duplicates event IDs, and uses the
  bounded `/api/v1/integration/events` cursor endpoint after reconnect.
- SignalR notifications are hints. Business state is always re-read from API
  endpoints as each dashboard module is connected.

The dashboard financial values are still typed presentation fixtures. Phase
7A deliberately connects identity/session/realtime infrastructure first;
module-by-module data adapters follow without putting a SQL connection string
in React.

## Commands

```bash
npm ci
npm run check
npm run build
npm run dev
```

The Vite build uses `base: "./"`, bundles fonts and icons locally, and writes
`dist`. The WPF project links that single `dist` directory into its output, so
the site and Desktop do not maintain separate React source trees.
