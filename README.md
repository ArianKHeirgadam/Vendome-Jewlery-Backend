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

## Configuration

Configuration is supplied by standard .NET providers. Environment variables use double underscores, for example:

```text
AllowedHosts=api.example.com
Api__AllowedCorsOrigins__0=https://app.example.com
CorrelationId__HeaderName=X-Correlation-ID
ConnectionStrings__GoldInvoice=Server=sql.example.internal;Database=VendomeGoldInvoice;Encrypt=True;...
Jwt__SigningKey=<base64-encoded-random-key-of-at-least-32-bytes>
```

The default CORS origin list is empty, so cross-origin requests are denied until trusted origins are configured. Plain HTTP origins are accepted only for loopback development hosts.

The base settings intentionally contain empty database and JWT secrets. The Development profile contains a credential-free Windows LocalDB setting; replace it with user secrets or environment variables when using another SQL Server. Do not commit deployment credentials or signing keys.

For local development, create a persistent random signing key outside the repository. One PowerShell example is:

```powershell
$bytes = [byte[]]::new(32)
[System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
dotnet user-secrets set "Jwt:SigningKey" ([Convert]::ToBase64String($bytes)) --project GoldInvoice.Api
dotnet user-secrets set "ConnectionStrings:GoldInvoice" "<local SQL Server connection string>" --project GoldInvoice.Api
```

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
