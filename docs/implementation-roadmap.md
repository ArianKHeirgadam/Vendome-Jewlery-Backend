# Implementation roadmap

## Current Phase

Phase 5: Customer addresses, orders, payments, and immutable invoices.

The repository entry point for this phase is commit `ca76a90`. Phases 1 through 4 are already shared on `main`; they are not rebuilt or squashed. The shared `InitialDomainModel` and `AddPhase4CatalogPricingInventory` migrations are immutable. Phase 5 database changes must be additive.

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

### Phase 4: Catalog, pricing, and inventory

- Hierarchical product categories and authoritative gold-product details.
- Effective-dated fixed, weight, market, and manual-review pricing rules with deterministic rial calculations and immutable calculation snapshots.
- Validated market-price provider boundary, retry/timeout behavior, safe quote snapshots, and polling worker.
- Aggregate inventory plus individually tracked physical units, atomic reservations/transfers, overselling protection, and append-only stock movements.
- Serialized Owner bootstrap and protected MFA material completed as Phase 3 closure work.
- Phase details and verification are recorded in `docs/architecture/phase-4-catalog-pricing-inventory.md`.

## Current Phase Requirements

### Customer addresses and store identity

- Customer-owned soft-deletable addresses, one active default per customer, ownership checks, and rowversion-protected updates.
- Immutable order and invoice address snapshots so address edits never rewrite history.
- Typed `Store.Profile` JSON in existing system settings, copied into order and invoice snapshots.
- No unused `StoreProfile` or `DiscountPolicy` table.

### Orders and inventory coordination

- Idempotent order creation with server-generated totals and complete weight, karat, price-component, market-rate, calculation-reference, customer, address, and store snapshots.
- Serializable reservation of aggregate stock and individually tracked units with stock-ledger entries in the same transaction.
- Order-linked reservation integrity checks and payment-only confirmation; generic inventory mutations cannot bypass the sales workflow.
- Ownership-scoped reads and mutations, bounded pagination, optimistic concurrency, and staff-only cross-customer/discount/shipping behavior.
- Unpaid cancellation that releases reservations, restores tracked units, cancels non-final payments, and records status history atomically.

### Payments

- Configurable gateways storing only non-secret configuration references and a pluggable provider interface.
- Idempotent online initiation with bounded provider timeout, validated HTTPS redirects, masked metadata, and cancellation-safe persistence.
- Authenticated, request-bounded, rate-limited callbacks with provider/external-ID and provider/payload-hash duplicate boundaries.
- Gateway, authority, amount, state, and inventory validation before accepting a payment; ambiguous callbacks enter explicit review instead of marking an order paid.
- Review-state online payments cannot be silently cancelled or replaced by a manual payment.
- Permission-protected manual payments using the same inventory confirmation and invoice transaction.

### Invoices

- Exactly one invoice per order and verified payment, with an atomic concurrency-protected sequence and unique invoice number.
- Immutable item/address/store/customer snapshots copied from the paid order.
- Explicit invoice void transition with reason and rowversion; no deletion or snapshot rewrite.
- Duplicate callback, duplicate invoice, idempotent order/payment, sequence, filtered-index, no-cascade, and pending-model-change tests.

## Deferred Requirements

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
- Market quotes and price calculations are immutable audit inputs. Phase 5 order and invoice items reference and copy the values required to reproduce a sale.
- Price calculations use explicit rule inputs and `MidpointRounding.AwayFromZero` to whole Iranian rials. Client-provided totals are never authoritative.
- Store identity remains a typed document in existing settings; orders and invoices own immutable copies. Payment providers are Infrastructure adapters behind Application contracts.
- Returns and refunds remain deferred until stock, gateway, partial-return, and fiscal-document rules are confirmed.
- Image metadata continues to use object-storage `StorageKey`; actual Cloudinary/S3 upload is deferred until a storage provider is selected.

## Database Decisions

- `InitialDomainModel` and `AddPhase4CatalogPricingInventory` are committed and pushed and will never be edited, deleted, or removed from migration history.
- Phase 5 uses the additive migration `AddPhase5OrdersPaymentsInvoices`; existing nullable order/invoice extensions preserve legacy rows.
- New money columns use `bigint` Iranian rials. Weights and percentages use explicit decimal precision; `float` and `double` are prohibited.
- Foreign keys use `NO ACTION`/restrict semantics. Ledgers, market/calculation snapshots, order/invoice snapshots, and callbacks are append-only or protected from hard deletion.
- SQL constraints and unique indexes provide the final duplicate and state-safety boundary; Application validation provides useful errors before a constraint is reached.

## Security Decisions

- Provider API keys, payment credentials, JWT keys, Data Protection keys, and connection strings never enter committed configuration or business tables.
- Market sources store only non-secret endpoints and configuration references. Raw provider payloads are represented only by a cryptographic hash.
- Payment gateways also store only external configuration references. Callback authenticity is decided by the registered provider adapter before any payment transition; raw callback bodies are not persisted or logged.
- Data Protection key rings must be persisted and access-controlled in production; ephemeral container keys are not acceptable for protected MFA data.
- Customer data is ownership-scoped. Cross-customer orders, manual payments, gateway configuration, store settings, status changes, and invoice voiding require existing management permissions.

## Known Risks

- The target SQL Server instance is reachable only from the user's Windows machine, so this environment cannot inspect its `__EFMigrationsHistory` or apply migrations. This does not change the additive migration decision.
- No production market-price provider or secret-delivery mechanism has been selected. Phase 4 supplies the provider contract, safe ingestion pipeline, worker scheduling, and fake provider tests without inventing a vendor integration.
- No production payment gateway adapter or secret-delivery mechanism has been selected. Phase 5 supplies the provider contract and safe orchestration without inventing a vendor integration.
- Refunds and returns have materially different inventory, payment, and fiscal consequences; implementing them before the business rules are confirmed would create unsafe generic behavior.

## Next Step

Verify Phase 5 with restore, Release build, the full test suite, `HasPendingModelChanges`, and migration application on the target SQL Server. After inspection and commit/push, begin Phase 6 durable outbox/inbox processing and authorized real-time notifications without folding refund, printing, or desktop scope into that phase.
