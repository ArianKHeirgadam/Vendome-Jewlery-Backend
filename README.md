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

## Configuration

Configuration is supplied by standard .NET providers. Environment variables use double underscores, for example:

```text
AllowedHosts=api.example.com
Api__AllowedCorsOrigins__0=https://app.example.com
CorrelationId__HeaderName=X-Correlation-ID
ConnectionStrings__GoldInvoice=Server=sql.example.internal;Database=VendomeGoldInvoice;Encrypt=True;...
```

The default CORS origin list is empty, so cross-origin requests are denied until trusted origins are configured. Plain HTTP origins are accepted only for loopback development hosts.

The base settings intentionally contain an empty database connection string. The Development profile contains a credential-free Windows LocalDB setting; replace it with user secrets or an environment variable when using another SQL Server. Do not commit deployment credentials.

## Database migration

```bash
dotnet tool restore
dotnet ef database update \
  --project GoldInvoice.Infrastructure \
  --startup-project GoldInvoice.Api \
  --context GoldInvoiceDbContext
```

## Build and test

```bash
dotnet restore VendomeJewleryInvoiceManagement.sln
dotnet build VendomeJewleryInvoiceManagement.sln --configuration Release --no-restore
dotnet test VendomeJewleryInvoiceManagement.sln --configuration Release --no-build
```

Architecture decisions are recorded in:

- [`docs/architecture/phase-1-foundation.md`](docs/architecture/phase-1-foundation.md)
- [`docs/architecture/phase-2-data-model.md`](docs/architecture/phase-2-data-model.md)
