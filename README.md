# Vendome Jewelry Invoice Management

.NET 8 modular monolith for the Vendome jewelry invoice-management platform. The solution follows Clean Architecture and is being delivered in independently verified phases.

## Projects

- `GoldInvoice.Domain`: domain model with no outward dependencies.
- `GoldInvoice.Contracts`: transport and integration contracts.
- `GoldInvoice.Application`: use cases; depends on Domain and Contracts.
- `GoldInvoice.Infrastructure`: technical adapters; depends on Application, Domain, and Contracts.
- `GoldInvoice.Api`: HTTP composition root.
- `GoldInvoice.Worker`: background-processing composition root.
- `GoldInvoice.UnitTests`: Domain and Application tests.
- `GoldInvoice.IntegrationTests`: API and infrastructure-boundary tests.

## Phase 1 endpoints

- `GET /health/live`: process liveness.
- `GET /health/ready`: dependency readiness. It currently contains only the self-check and will gain database and broker checks when those adapters are introduced.

The API accepts a safe correlation ID through `X-Correlation-ID` and returns the effective value in the same response header. Errors use RFC 7807-compatible Problem Details and never include exception messages for unhandled server failures.

## Phase 2 database foundation

Phase 2 adds the complete initial domain and SQL Server model: ASP.NET Core Identity persistence, permissions and sessions, catalog, inventory, orders, payments, invoice snapshots, desktop devices, outbox, audit logs, settings, and idempotency records. SQL Server schemas, foreign keys, check constraints, indexes, soft-delete filters, UTC audit timestamps, and `rowversion` concurrency tokens are configured explicitly.

The initial migration is `InitialDomainModel`. No user, role, permission, credential, or owner account is seeded.

## Phase 3 identity and sessions

Phase 3 adds ASP.NET Core Identity, the `Owner`, `Admin`, and `Customer` system roles, the permission catalog, JWT access tokens, one-time refresh-token rotation, session revocation, account lockout, and authenticator-app MFA enrollment. Access-token validation checks the current user, session, security stamp, roles, and permissions against SQL Server on every authenticated request, so disabling a session or changing access does not wait for a JWT to expire.

Authentication endpoints are under `/api/v1/auth`:

- `POST /login`, `POST /refresh`
- `POST /mfa/setup`, `POST /mfa/enable`
- `POST /logout`, `POST /logout-all`
- `GET /sessions`, `DELETE /sessions/{sessionId}`
- `GET /me`

The login, refresh, and MFA routes have separate fixed-window rate-limit policies. Owner and Admin sessions are never issued until MFA succeeds. Recovery codes are returned once when MFA is enabled and must be stored by the user in a secure location.

## Phase 4 catalog, pricing, and inventory

Phase 4 adds hierarchical categories, gold-specific variant details, effective-dated pricing rules, safe market-price ingestion, deterministic backend price calculations, aggregate and physical-piece inventory, reservations, transfers, and an append-only stock ledger. The shared initial migration remains unchanged; the additive migration is `AddPhase4CatalogPricingInventory`.

Catalog, pricing, and inventory endpoints are under `/api/v1/catalog`, `/api/v1/pricing`, and `/api/v1/inventory`. They require the existing product or inventory permissions. Product and stock-movement lists are bounded to 100 rows per page. Physical units can be found by ID, serial number, or barcode; acquisition cost is not exposed by read responses.

The Worker polls only explicitly registered `IMarketPriceProvider` implementations and expires overdue reservations. The repository includes a fake provider for tests but no invented production vendor integration. Provider credentials must come from a deployment secret manager or environment variables; `MarketPriceSources` stores only non-sensitive configuration references.

## Phase 5 orders, payments, and invoices

Phase 5 adds customer addresses, idempotent order creation, backend-authoritative price snapshots, atomic stock reservation/confirmation, pluggable payment gateways, authenticated and deduplicated callbacks, manual payments, atomic invoice sequences, immutable invoice snapshots, unpaid-order cancellation, and invoice voiding. The additive migration is `AddPhase5OrdersPaymentsInvoices`; the Phase 2 and Phase 4 migrations remain unchanged.

Routes are under `/api/v1/customers/{customerId}/addresses`, `/api/v1/orders`, `/api/v1/payments`, `/api/v1/invoices`, and `/api/v1/settings/store-profile`. Ownership is enforced for customer reads and mutations; cross-customer and manual-payment operations require existing management permissions. Order and payment creation routes require an `Idempotency-Key` header.

The current seller identity is a typed JSON document under `Store.Profile` in the existing settings table and is copied into every order and invoice. Payment-gateway rows contain only a `ConfigurationReference`; real credentials must remain in an external secret provider. The repository defines `IPaymentGatewayProvider` but does not invent or register a production gateway adapter.

Refunds and returns remain deferred until their business and fiscal rules are confirmed. See [`docs/architecture/phase-5-orders-payments-invoices.md`](docs/architecture/phase-5-orders-payments-invoices.md).

## Configuration

Configuration is supplied by standard .NET providers. Environment variables use double underscores, for example:

```text
AllowedHosts=api.example.com
Api__AllowedCorsOrigins__0=https://app.example.com
CorrelationId__HeaderName=X-Correlation-ID
ConnectionStrings__GoldInvoice=Server=sql.example.internal;Database=VendomeGoldInvoice;Encrypt=True;...
Jwt__SigningKey=<base64-encoded-random-key-of-at-least-32-bytes>
Payments__ProviderTimeoutSeconds=15
Invoicing__SequenceSeries=DEFAULT
Invoicing__SequencePrefix=INV
```

The default CORS origin list is empty, so cross-origin requests are denied until trusted origins are configured. Plain HTTP origins are accepted only for loopback development hosts.

The base settings intentionally contain empty database and JWT secrets. The Development profile contains a credential-free Windows LocalDB setting; replace it with user secrets or environment variables when using another SQL Server. Do not commit deployment credentials or signing keys.

For local development, create a persistent random signing key outside the repository. This PowerShell form also works on Windows PowerShell versions that do not expose the static `RandomNumberGenerator.Fill` method:

```powershell
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$bytes = New-Object byte[] 32
$rng.GetBytes($bytes)
dotnet user-secrets set "Jwt:SigningKey" ([Convert]::ToBase64String($bytes)) --project GoldInvoice.Api
$rng.Dispose()
Remove-Variable rng,bytes
dotnet user-secrets set "ConnectionStrings:GoldInvoice" "<local SQL Server connection string>" --project GoldInvoice.Api
```

Authenticator keys and recovery-code payloads are protected with ASP.NET Core Data Protection. Production must use a persistent, access-controlled key ring shared by every API instance; do not rely on ephemeral container keys.

### Initial owner

Roles and permissions are seeded idempotently when the API starts. No default account or password exists. To create the first Owner, supply these values through user secrets or a deployment secret manager for one startup only:

```text
Security__BootstrapOwner__Enabled=true
Security__BootstrapOwner__Email=<owner email>
Security__BootstrapOwner__Password=<temporary strong password>
Security__BootstrapOwner__DisplayName=<display name>
```

Remove the bootstrap values after the account is created. The Owner email starts confirmed, but the first successful password check returns `mfa_enrollment_required`; no access or refresh token is issued until TOTP enrollment is completed.

## Database migration

```bash
dotnet tool restore
dotnet ef database update \
  --project GoldInvoice.Infrastructure \
  --startup-project GoldInvoice.Infrastructure \
  --context GoldInvoiceDbContext
```

The design-time factory reads `ConnectionStrings__GoldInvoice` from the current process. Set that environment variable for the migration command and clear it afterwards; API user secrets are used by the running API, not by the Infrastructure startup project.

## Build and test

```bash
dotnet restore VendomeJewleryInvoiceManagement.sln
dotnet build VendomeJewleryInvoiceManagement.sln --configuration Release --no-restore
dotnet test VendomeJewleryInvoiceManagement.sln --configuration Release --no-build
```

Architecture decisions are recorded in:

- [`docs/architecture/phase-1-foundation.md`](docs/architecture/phase-1-foundation.md)
- [`docs/architecture/phase-2-data-model.md`](docs/architecture/phase-2-data-model.md)
- [`docs/architecture/phase-3-identity-security.md`](docs/architecture/phase-3-identity-security.md)
- [`docs/architecture/phase-4-catalog-pricing-inventory.md`](docs/architecture/phase-4-catalog-pricing-inventory.md)
- [`docs/architecture/phase-5-orders-payments-invoices.md`](docs/architecture/phase-5-orders-payments-invoices.md)
- [`docs/implementation-roadmap.md`](docs/implementation-roadmap.md)
