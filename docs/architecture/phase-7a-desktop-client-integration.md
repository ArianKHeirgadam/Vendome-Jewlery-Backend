# Phase 7A: Desktop client integration

## Scope and result

Phase 7A integrates the shared React presentation layer and the WPF/WebView2
Desktop host with the existing Phase 3 authentication APIs and Phase 6 SignalR
pipeline. It intentionally makes no database-model change. The device and
printing entities assigned to Phase 7 are deferred to the immediately following
Phase 7B slice so that authentication/reconnect behavior can be verified before
device trust is introduced.

## Shared client boundary

`GoldInvoice.Client` is the only React source tree. It builds once for both the
website and `VendomeJewleryDesktopApp`. The Desktop project links the generated
`dist` files into `ClientApp/dist` at build/publish time and loads them from a
WebView2 virtual host.

```text
Website React ----------+
                        +--> authenticated GoldInvoice.Api --> SQL Server
Desktop WebView2/React -+
```

Neither React nor WPF receives a SQL Server connection string. Controllers and
existing Application/Infrastructure services remain the only route to business
state.

## Authentication and token storage

The client supports the complete existing login state machine:

- `authenticated`
- `mfa_required`, including authenticator and recovery codes
- `mfa_enrollment_required`, including setup, enable, and one-time recovery-code
  display

Access tokens are short lived and remain in React memory. The client calls
`/api/v1/auth/me` after every accepted token pair and refreshes before access
token expiry. Refresh operations are single-flight because backend refresh
tokens are one-time and rotation/reuse detection must never see two parallel
uses from one client.

Inside Desktop, React sends an authentication command to WPF. WPF calls the API,
extracts the refresh token, protects it with Windows DPAPI at CurrentUser scope,
and removes it before responding to JavaScript. On restart, WPF rotates the
stored refresh token and returns only the new access-token metadata. Logout
attempts server revocation and always removes the local protected token.

For the regular browser build, the current fallback uses per-tab
`sessionStorage`, not durable `localStorage`. A production website can replace
that adapter with a same-origin BFF/HttpOnly cookie without changing UI
components.

## WebView2 trust boundary

- The local bundle is mapped to `https://desktop.vendome.invalid`, a reserved,
  non-resolving host.
- Both incoming message source and current outgoing document source are checked
  before a native command or response is processed.
- Navigation outside that host and all new-window requests are rejected.
- Authentication bridge messages are bounded to 32 KiB.
- Native and API failures are sanitized before they reach React.
- The HTML entry point declares a restrictive Content Security Policy while
  allowing configured HTTP(S)/WebSocket API connections.

The API CORS list includes only the Desktop virtual origin by default.
Development additionally permits `http://localhost:5173` for Vite.

## SignalR and recovery

After `/me` succeeds, the client connects to `/hubs/integration` with the current
access token and no client-selected groups. Phase 6 continues to resolve user and
role audiences on the server. Phase 7B will add an approved `deviceId` only after
the registration workflow exists.

The client listens for `integrationEvent`, de-duplicates event IDs, and stores a
non-secret per-user cursor. After reconnect it scans the bounded
`/api/v1/integration/events` endpoint until the server stops advancing the
cursor. Events update connection/notification state only; each business module
must re-read its authoritative API resource when its adapter is implemented.

## Verification gate

This environment can execute Node but not .NET/MSBuild or Windows WebView2.
Completed locally:

- strict TypeScript check
- Vite production build
- npm production dependency audit
- XAML XML validation
- C# tree-sitter syntax scan with no error nodes
- JSON configuration parsing

Required on the target Windows/.NET 8 environment:

1. Restore and Release-build the complete solution.
2. Run the full Unit and Integration test suite.
3. Start API and Desktop together and verify login, required MFA, enrollment,
   refresh rotation across restart, logout, and invalid/revoked sessions.
4. Disconnect/reconnect the API and verify SignalR status plus cursor recovery.
5. Confirm the migration list remains the three committed migrations and
   `HasPendingModelChanges()` is false.

## Next slice

Phase 7B implements short-lived Desktop registration tokens, explicit approval,
public-key/thumbprint binding, heartbeat/revocation, approved device SignalR
identity, local printer discovery, `DevicePrinter`, `PrintProfile`, durable
`InvoicePrintJob`, and append-only `InvoicePrintLog` history through a new
additive migration.
