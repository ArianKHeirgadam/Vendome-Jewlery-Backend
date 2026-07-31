# Implementation roadmap

## Current Phase

Phase 4: Catalog, pricing, market prices, and inventory workflows.

The repository entry point for this phase is commit `4cd41ec`. Phases 1 through 3 are already shared on `main`; they are not rebuilt or squashed. The shared `InitialDomainModel` migration is immutable. Phase 4 database changes must be additive.

## Completed Requirements

### Phase 1: Foundation

- Clean Architecture project boundaries and composition roots.
- Validated configuration, structured logging, correlation IDs, Problem Details, CORS, security headers, and health endpoints.
- Release build and foundation tests recorded in `docs/architecture/phase-1-foundation.md`.

### Phase 2: Domain model and SQL Server

- Initial 32 business entities plus ASP.NET Core Identity persistence.
- Explicit schemas, `NO ACTION` relationships, SQL constraints, indexes, soft deletion, audit fields, and `rowversion` concurrency.
- Shared migration `InitialDomainModel` in `GoldInvoice.Infrastructure/Persistence/Migrations`.
- Database decisions recorded in `docs/architecture/phase-2-data-model.md`.

### Phase 3: Identity and authorization

- Owner, Admin, and Customer roles; stable permission catalog and permission policies.
- Identity password policy, lockout, short-lived JWT access tokens, live session/security-stamp validation, hashed refresh tokens, token families, rotation, reuse detection, and session revocation.
- TOTP enrollment, recovery codes, privileged-user MFA enforcement, login tracking, and security-event classification.
- Pure administration policies prevent an Admin managing an Owner, privilege grants beyond the actor's access, and removal of the final active Owner when administration endpoints apply them.
- Explicit Owner bootstrap with no default credential.
- Phase details and verification are recorded in `docs/architecture/phase-3-identity-security.md`.

## Current Phase Requirements

### Phase 3 additions required before Phase 4 closes

- Serialize initial Owner bootstrap across application instances so two Owners cannot be created by a startup race.
- Protect Identity authenticator keys and recovery-code payloads at rest with ASP.NET Core Data Protection.
- Preserve idempotent role and permission seeding and the Owner/Admin/Customer security-policy tests.

### Catalog

- Hierarchical product categories with unique slugs, active state, display order, cycle validation, and restricted deletion semantics.
- Product and product-variant APIs with bounded pagination and concurrency tokens.
- Gold-specific variant details covering karat, gross/net gold/stone/other-material weights, wage method/value, profit, tax, stone presence, and variable-weight behavior.
- Keep the existing `ProductVariant` compatibility columns intact because `InitialDomainModel` is shared; new APIs use the Phase 4 detail and pricing models as the authoritative calculation inputs.

### Pricing and market prices

- Versioned product-variant pricing rules for fixed, weight-based, market-based, and manual-review pricing.
- Reject overlapping active rule windows for the same variant.
- Market-price source and append-only snapshot persistence without provider secrets or raw sensitive payloads.
- Provider interface, validation, bounded timeout, retry, deterministic latest-valid-price selection, and a fake provider for tests.
- Backend-only price calculator with itemized components, deterministic rial rounding, audit references, and persisted price-calculation snapshots.
- A Phase 4 polling job that runs only registered market-price providers; durable outbox processing remains Phase 6.

### Inventory

- Support both aggregate quantity inventory and individually tracked physical jewelry units.
- Unique optional serial numbers and barcodes, actual weight/karat, acquisition cost, warehouse location, and controlled unit status transitions.
- Atomic stock receipts, adjustments, reservations, releases, confirmations, unit transfers, and aggregate transfers.
- Every quantity or unit state change must produce an append-only stock movement in the same transaction.
- Optimistic concurrency and tests for reservation races and overselling.

## Deferred Requirements

### Phase 5: Orders, payments, and invoices

- Customer addresses and immutable order-address snapshots.
- Complete order and invoice snapshots of weight, karat, wage, profit, tax, market rate, store identity, and price-calculation audit references.
- Payment gateways and credential references, callback idempotency, invoice sequences, atomic invoice numbering, immutable invoices, cancellation, returns, and refunds when confirmed by a business use case.
- Decide whether store identity needs a typed `StoreProfile` or can safely remain in typed system settings, and whether a real discount-policy use case warrants a `DiscountPolicy` entity; do not create either as an unused generic table.
- Duplicate callback, duplicate invoice, and concurrent invoice-number tests.

### Phase 6: Worker, outbox, and SignalR

- Durable outbox/inbox processing, exponential retry, dead-letter handling, processing locks, heartbeat, and graceful shutdown.
- Events for invoice creation, inventory changes, order status changes, and market-price updates.
- Authorized SignalR groups for users, roles, and devices; reconnect recovery remains API-backed.

### Phase 7: Desktop and printing

- Device printers, print profiles, idempotent invoice print jobs, durable print logs, reprint approval, secure device enrollment, and printer-result reporting by the Desktop application.

### Phase 8: Production hardening

- Query-plan/index review, enforced pagination, retention policies, backup and restore tests, database integrity checks, least-privilege SQL access, transport encryption, secret/key rotation, dependency scanning, WAF/origin guidance, readiness/liveness review, structured alerting, load/concurrency tests, and disaster-recovery documentation.

## Architecture Decisions

- The solution remains a .NET 8 modular monolith with Domain and Application independent of EF Core and ASP.NET Core.
- Product variants are the sellable pricing and inventory boundary. A product owns catalog presentation; a variant owns gold detail, pricing rules, aggregate stock, and optional physical units.
- Application interfaces define use cases and provider boundaries. EF Core, transactions, retries, and external adapters remain in Infrastructure. Controllers contain transport mapping only.
- Market quotes and price calculations are immutable audit inputs. Historical order/invoice snapshots will reference or copy them in Phase 5.
- Price calculations use explicit rule inputs and `MidpointRounding.AwayFromZero` to whole Iranian rials. Client-provided totals are never authoritative.
- Image metadata continues to use object-storage `StorageKey`; actual Cloudinary/S3 upload is deferred until a storage provider is selected.

## Database Decisions

- `InitialDomainModel` is committed and pushed and will never be edited, deleted, or removed from migration history.
- Phase 4 uses a new additive migration. Existing `ProductVariant` columns are retained for backward compatibility.
- New money columns use `bigint` Iranian rials. Weights and percentages use explicit decimal precision; `float` and `double` are prohibited.
- Foreign keys use `NO ACTION`/restrict semantics. Ledgers, market snapshots, and calculation snapshots are append-only and protected from hard deletion.
- SQL constraints and unique indexes provide the final duplicate and state-safety boundary; Application validation provides useful errors before a constraint is reached.

## Security Decisions

- Provider API keys, payment credentials, JWT keys, Data Protection keys, and connection strings never enter committed configuration or business tables.
- Market sources store only non-secret endpoints and configuration references. Raw provider payloads are represented only by a cryptographic hash.
- Data Protection key rings must be persisted and access-controlled in production; ephemeral container keys are not acceptable for protected MFA data.
- Catalog mutation, pricing, market ingestion, and inventory mutation endpoints require explicit permissions. Inventory reads never expose acquisition cost to customer-facing APIs.

## Known Risks

- The target SQL Server instance is reachable only from the user's Windows machine, so this environment cannot inspect its `__EFMigrationsHistory` or apply migrations. Because `InitialDomainModel` is already shared, this does not change the additive migration decision.
- No production market-price provider or secret-delivery mechanism has been selected. Phase 4 supplies the provider contract, safe ingestion pipeline, worker scheduling, and fake provider tests without inventing a vendor integration.
- Store-specific pricing decisions may change. The Phase 4 formula and rounding policy are isolated and versioned so later changes do not rewrite historical calculation snapshots.

## Next Step

Complete the Phase 3 security additions, then implement and verify the Phase 4 model, APIs, pricing pipeline, inventory transactions, additive migration, and tests. Phase 5 must not begin until Phase 4 restore, build, tests, migration validation, security scans, commit, and push are complete.
