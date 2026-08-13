# Implementation roadmap

## Current Phase

Phase 7C-A implementation: paid-invoice preview, audited document correction,
PDF export, and local Desktop printing. Device enrollment and device-bound
printer discovery remain Phase 7C-B.

The user confirmed on 2026-08-10 that the Phase 5 restore, build, complete test suite, and database verification passed. After the Phase 6 cancellation/scope fixes, the user also reported the complete 97-test suite passing on the target .NET 8 environment and authorized the next phase. SQL Server concurrent-claim evidence, final migration/model verification, commit, and push must still be recorded in the Phase 6 completion report rather than being silently assumed.

Phase 7A introduced no database-model change and integrated the existing Phase 3 authentication and Phase 6 SignalR contracts with the shared React client and WPF/WebView2 host. Phase 7B connects every management route to authorized data and adds the missing supplier and CRM persistence. Phase 7C-A reuses the existing invoice, audit, permission, and print-log model to complete local document output without another migration.

The committed migration chain is:

1. `20260731190255_InitialDomainModel`
2. `20260731213000_AddPhase4CatalogPricingInventory`
3. `20260801130000_AddPhase5OrdersPaymentsInvoices`
4. `20260811143000_AddPhase7BusinessDirectories`

The first three shared migrations remain immutable. Phase 7B is represented only by the fourth additive migration; every future database change must likewise use a new migration rather than rewriting history.

## Completed Requirements

### Phase 1: Foundation

- Clean Architecture project boundaries and composition roots.
- Validated configuration, structured logging, correlation IDs, Problem Details, CORS, security headers, and health endpoints.
- Release build and foundation tests recorded in `docs/architecture/phase-1-foundation.md`.

### Phase 2: Domain model and SQL Server foundation

- Initial business model plus ASP.NET Core Identity persistence, with explicit schemas and names rather than generic `Data`, `Details`, or `Logs` tables.
- Catalog `Product` and `ProductVariant`, aggregate `InventoryItem`, append-only `StockMovement`, `StockReservation`, sales, payment, invoice, device, outbox, audit, setting, and idempotency foundations.
- SQL Server `NO ACTION` relationships, check constraints, indexes, explicit decimal precision, rial money columns, soft deletion, UTC audit fields, and `rowversion` concurrency.
- Shared migration `InitialDomainModel` in `GoldInvoice.Infrastructure/Persistence/Migrations`.
- Database decisions recorded in `docs/architecture/phase-2-data-model.md`.
- The later gold-commerce additions were made through the additive Phase 4 migration; the shared Phase 2 migration was not rewritten.

### Phase 3: Identity and authorization implementation

- Owner, Admin, and Customer roles; stable permission catalog and permission policies.
- Idempotent permission and Owner-role seeding, explicit Owner bootstrap with no default credential, and serialized bootstrap creation.
- Identity password policy, lockout, short-lived JWT access tokens, live session/security-stamp validation, hashed refresh tokens, token families, rotation, reuse detection, and session revocation.
- TOTP enrollment, protected authenticator keys and recovery-code payloads, recovery codes, privileged-user MFA enforcement, login-attempt tracking, and security-event classification.
- Pure administration policies prevent an Admin managing an Owner, granting permissions the actor does not hold, and removing the final active Owner.
- Unit and integration coverage for refresh-token rotation/reuse, session revocation, Owner MFA, permission seeding, bootstrap idempotency, and core Owner/Admin authorization policies.
- Phase details are recorded in `docs/architecture/phase-3-identity-security.md`.

### Phase 4: Catalog, pricing, and inventory

- Hierarchical `ProductCategory` with unique slug, restricted parent/product relationships, ordering, activation, and optimistic concurrency.
- `GoldProductDetail` at the sellable `ProductVariant` boundary, with explicit decimal weights, karat validation, wage/profit/tax inputs, stone and variable-weight flags, and no `float`/`double` storage.
- Effective-dated `ProductPricingRule` supporting fixed, weight-based, market-based, and manual-review methods without hard-coding a formula in controllers or persistence entities.
- Deterministic backend pricing with separately auditable calculation components, immutable calculation snapshots, whole-rial `MidpointRounding.AwayFromZero`, and no authoritative client total.
- `MarketPriceSource` and append-only `MarketPriceSnapshot`, provider boundaries, non-secret configuration references, payload hashes instead of raw sensitive responses, quote validation, duplicate protection, retry/timeout behavior, safe selection of the latest valid quote, and a fake provider for tests.
- Aggregate `InventoryItem` and optional physical-piece `InventoryUnit` support, including unique optional serial/barcode identifiers, actual weights and karat, warehouse ownership, acquisition cost, lifecycle status, and concurrency protection.
- Atomic reservations, releases, transfers, append-only stock movements, individually tracked piece sales, and overselling protection for both aggregate and piece-tracked stock.
- Use cases cover identical counted stock, similar pieces with different weights, stone-bearing products, fixed and market/weight pricing, transfers, reservation/cancellation, barcode sale, and the physical-unit lifecycle needed by a future confirmed return workflow.
- Worker polling for market prices and reservation expiration without a production provider being invented.
- Additive migration `AddPhase4CatalogPricingInventory`; no Phase 2 migration rewrite.
- Phase details are recorded in `docs/architecture/phase-4-catalog-pricing-inventory.md`.

### Phase 5: Orders, payments, and invoices implementation

- Customer-owned, soft-deletable `CustomerAddress` rows with one active default per customer, ownership enforcement, and rowversion-protected updates.
- Independent order and invoice address snapshots so editing or deleting a saved address never rewrites history.
- Typed `Store.Profile` JSON in the existing `configuration.SystemSettings` table, copied into order and invoice store snapshots; no unused one-row `StoreProfile` table.
- Idempotent order creation with backend-generated totals and complete product, physical unit, weight, karat, wage, profit, tax, market quote, calculation, customer, address, and store snapshots.
- Serializable reservation of aggregate inventory and physical units with matching ledger entries, plus atomic cancellation/release behavior.
- Configurable `PaymentGateway` rows containing only non-secret configuration references and a pluggable `IPaymentGatewayProvider` boundary.
- Idempotent online initiation, bounded provider timeout, validated HTTPS redirect handling, masked metadata, authenticated and rate-limited callbacks, duplicate callback boundaries, and explicit `RequiresReview` handling for ambiguous callbacks.
- Manual payment paths use the same inventory confirmation, order transition, and invoice transaction; review-state payments cannot be silently replaced.
- Atomic, concurrency-protected `InvoiceSequence`, unique invoice number/order/payment boundaries, immutable financial/item/store snapshots, audited buyer/delivery print-field corrections, explicit voiding, and no number reuse.
- Tests are present for duplicate callbacks, duplicate invoice prevention, idempotent order/payment behavior, sequence monotonicity, filtered indexes, no-cascade relationships, snapshot consistency, and pending EF model changes.
- Additive migration `AddPhase5OrdersPaymentsInvoices`; earlier migrations remain unchanged.
- Phase details are recorded in `docs/architecture/phase-5-orders-payments-invoices.md`.

## Phase 5 Completion Record

### Phase 5 verification gate

Reported successful by the user on the target environment. The commands and database checks remain below as the reproducible closure procedure.

- Run `dotnet tool restore` and confirm the pinned `dotnet-ef` 8.0.29 tool restores.
- Run `dotnet restore VendomeJewleryInvoiceManagement.sln`.
- Run a Release build with warnings and errors reported without modifying source-generated migration history.
- Run the full Unit and Integration test suites; the repository currently contains 84 `[Fact]`/`[Theory]` tests.
- Confirm `GoldInvoiceDbContext.Database.HasPendingModelChanges()` remains false.
- Run `dotnet ef migrations list` and confirm the three migrations through Phase 5.
- Inspect the target database's `dbo.__EFMigrationsHistory` before applying any migration.
- Apply only pending committed migrations to the intended non-production database; never drop a database, delete migration-history rows, or rewrite an applied/shared migration.
- For any database containing real data, take and verify an appropriate backup before applying an additive migration.
- Record Restore, Build, Test, migration-list, migration-application, and database-history results in the Phase 5 completion report.

Verification commands:

```bash
dotnet tool restore
dotnet restore VendomeJewleryInvoiceManagement.sln
dotnet build VendomeJewleryInvoiceManagement.sln --configuration Release --no-restore
dotnet test VendomeJewleryInvoiceManagement.sln --configuration Release --no-build
dotnet ef migrations list \
  --project GoldInvoice.Infrastructure \
  --startup-project GoldInvoice.Infrastructure \
  --context GoldInvoiceDbContext
```

Target-database history check:

```sql
SELECT MigrationId, ProductVersion
FROM dbo.__EFMigrationsHistory
ORDER BY MigrationId;
```

### Retrospective follow-up assigned to Phase 3

The new requirement set exposed two Phase 3 behaviors that are not evidenced by the current API/application surface. Only these missing behaviors are added; the completed identity model is not rebuilt.

- Add permission-protected user/role administration use cases that can grant and revoke `RolePermission` rows while applying the existing anti-escalation and final-Owner policies inside the transaction.
- Complete the `TrustedDevice` lifecycle: enrollment/recognition rules, bounded trust expiration, revocation, session association, ownership checks, and tests. A database entity alone is not completion of this behavior.
- Add explicit Owner/Admin/Customer endpoint-level authorization tests for these administration paths.
- This follow-up remains assigned to the security module and is not silently represented as Phase 6 work. Any required schema change must be a new additive migration; existing security migrations remain immutable.

## Phase 6 Implemented Requirements

### Phase 6: Worker, outbox, and SignalR

#### Existing foundation to reuse

- Reuse the existing `integration.OutboxMessages` table and `OutboxMessage` entity; do not create a duplicate outbox table.
- Reuse the existing Worker, which already polls market prices and expires reservations; separate schedules and failures so one workload cannot starve the others.
- The pre-Phase-6 outbox status, retry, lock, and dead-letter columns were schema preparation only; the current Phase 6 work activates them without duplicating the table.

#### Durable event production

- Write an Outbox message in the same SQL transaction as `InvoiceCreated`, `InventoryChanged`, `OrderStatusChanged`, and `MarketPriceUpdated` state changes.
- Use stable versioned message contracts, event IDs, occurrence timestamps, aggregate identifiers, correlation/causation metadata, and payloads containing no secrets or unnecessary personal data.
- Do not publish directly from controllers or before the business transaction commits.
- Add an `InboxMessage`/consumer-idempotency store only when an actual durable consumer requires it; define its consumer/message uniqueness boundary instead of creating an unused table.

#### Reliable dispatch

- Claim batches atomically with lock IDs and lock expiry so multiple Worker instances cannot process the same message concurrently.
- Recover abandoned locks after expiry, renew locks/heartbeat for long processing, and release cleanly during graceful shutdown.
- Implement bounded exponential backoff, sanitized failure recording, maximum attempts, and a terminal dead-letter state.
- Make handlers idempotent and distinguish transient failures from permanent invalid messages.
- Provide permission-protected inspection/reprocess operations for dead-letter messages; reprocessing must be auditable and must not reset history silently.
- Add tests for concurrent claims, lock expiry/recovery, retry timing, dead-letter transition, duplicate delivery, handler idempotency, cancellation, and graceful shutdown.

#### SignalR

- Add authorized SignalR connections and stable groups for `User`, `Role`, and approved `Device` identities.
- Resolve current roles/permissions/device state on connection and prevent clients from joining arbitrary groups.
- Publish only post-commit outbox events; SignalR is not a replacement for SQL Server or durable queueing.
- Treat real-time messages as hints. After reconnect, clients recover authoritative missed state through bounded API queries/cursors rather than assuming delivery continuity.
- Add connection authorization, group-isolation, reconnect-recovery, and sensitive-payload tests.

#### Market-price scheduling

- Preserve periodic provider polling, validation, latest-valid selection, timeout, and retry rules from Phase 4.
- Emit `MarketPriceUpdated` only after a valid snapshot is committed, and prevent one provider failure from stopping reservation expiration or outbox dispatch.

Phase 6 implementation details are recorded in `docs/architecture/phase-6-worker-outbox-signalr.md`. The existing Outbox table is sufficient, so this phase currently has no EF Core migration and no Inbox table.

## Current and Deferred Requirements

### Phase 7: Desktop and printing

Phase 7A now provides the shared React source tree, WPF/WebView2 host, complete existing authentication state machine, DPAPI-protected Desktop refresh-token storage, API endpoint configuration, authorized SignalR connection, event de-duplication, and reconnect cursor recovery. Its decisions and verification gate are recorded in `docs/architecture/phase-7a-desktop-client-integration.md`.

Phase 7B now completes the operational management pages, authenticated data loading, directories, suppliers, and CRM. Phase 7C-A completes automatic post-payment invoice opening, an A4 RTL preview, audited correction of non-financial print fields, PDF export, and local default-printer output. Its decisions are recorded in `docs/architecture/phase-7c-invoice-documents.md`. The remaining device-bound requirements below are Phase 7C-B.

#### Existing foundation to reuse

- Reuse `devices.DesktopDevices` and `invoicing.InvoicePrintLogs`; do not create duplicate device or generic log tables.
- Phase 7C-A activates local `InvoicePrintLog` request/result tracking. Treat
  `DesktopDevice` and the missing printer/profile/job entities as foundations for
  the still-deferred device-bound workflow.

#### Device and printer model

- Complete secure Desktop device enrollment using short-lived registration tokens, explicit approval, public-key/thumbprint binding, heartbeat/last-seen state, revocation, and least-privilege device authorization.
- Add `DevicePrinter` with a required `DesktopDeviceId`, system printer name scoped to that device, display name, type, default/enabled state, last-seen time, audit fields, and rowversion.
- Add `PrintProfile` with paper size, orientation, copy count, color setting, typed margin settings, default state, audit fields, and rowversion.
- Enforce at most one active default printer per device and one active default profile for the intended scope.

#### Durable print workflow

- Add `InvoicePrintJob` linked to invoice, approved Desktop device, device printer, and print profile, with status, requester, timestamps, retry count, sanitized failure fields, idempotency key, and rowversion.
- Keep print jobs durable and idempotent. The Backend records a request; only the Desktop application can report that the operating-system print completed or failed.
- Preserve every `InvoicePrintLog` attempt: only its one-way
  `Requested -> Succeeded|Failed` completion may update it. Reprints require a
  reason and the requesting/approving user, and never erase earlier attempts.
- Verify the printer belongs to the selected Desktop device and is currently enabled before dispatch.
- Hide operating-system and printer-sensitive failure details from Customers.
- Deny Customer access to device, printer, job-management, and result-reporting APIs.
- Add tests for device approval/revocation, printer ownership, duplicate job keys, result-report authorization, retries, reprint approval, and immutable log history.

### Phase 8: Hardening and production readiness

- Review actual SQL Server query plans, missing-index suggestions, duplicate/overlapping indexes, and hot-query regressions before adding or removing indexes.
- Enforce pagination and a maximum page size on every list endpoint; add query-performance tests for representative data volumes.
- Define and implement retention policies for audit logs, security events, callbacks, idempotency data, outbox/inbox data, and operational history while preserving legal/fiscal records.
- Document and test database backups, point-in-time/restore procedures, integrity checks, migration rollback/forward-fix policy, recovery objectives, and disaster recovery.
- Use least-privilege SQL identities, encrypted database/network transport, protected persistent Data Protection keys, secret rotation, and cryptographic key rotation.
- Add dependency and vulnerability scanning, security-header review, rate-limit review, WAF guidance, origin protection, and safe deployment configuration checks.
- Complete liveness/readiness dependency checks, structured logs, metrics, tracing/correlation, dashboards, and actionable alerts.
- Run load, soak, concurrency, overselling, duplicate-callback, invoice-sequence, outbox-claim, and graceful-shutdown tests in a production-like environment.

### Unscheduled business-dependent requirements

- `ReturnRequest`, `ReturnItem`, and `Refund` remain deferred until eligibility, physical-unit disposition, partial return, payment-gateway refund, accounting, and fiscal-document rules are confirmed. Do not create empty generic tables.
- Actual Cloudinary/S3/object-storage upload remains deferred until a provider, authorization model, file-validation rules, lifecycle, and deletion/retention policy are selected. Existing image metadata continues to use `StorageKey`.

## Architecture Decisions

- The solution remains a .NET 8 modular monolith with Domain and Application independent of EF Core and ASP.NET Core.
- Existing names, schemas, namespaces, entity boundaries, and conventions are preserved. Before any new table is proposed, the current model and migration snapshot must be searched for the same concept under another name.
- Product boundaries remain explicit: `Product` is catalog identity, `ProductVariant` is the sellable boundary, `GoldProductDetail` holds gold-specific attributes, `InventoryItem` is aggregate warehouse stock, `InventoryUnit` is an optional physical piece, `StockMovement` is the append-only ledger, and `StockReservation` is temporary allocation.
- Product variants own gold detail, pricing rules, aggregate stock, and optional physical units. Duplicate facts are copied only into immutable order/invoice snapshots where historical reconstruction requires them.
- Application interfaces define use cases and provider boundaries. EF Core, transactions, retries, secret resolution, and external adapters remain in Infrastructure. Controllers contain transport mapping only.
- Pricing calculations remain independent of controllers and persistence entities, deterministic, unit-tested, componentized, and auditable. Client-provided final prices are never authoritative.
- Market quotes and price calculations are immutable audit inputs. Order and invoice items reference and copy the exact values required to reproduce a sale.
- Store identity remains a typed document in existing settings; orders and invoices own immutable copies. Payment providers remain Infrastructure adapters behind Application contracts.
- SignalR carries post-commit notifications only; SQL Server/outbox state and recovery APIs remain authoritative.
- Phase 7 printer entities are introduced only during Phase 7. Phase 8 operational changes are driven by measured production-like evidence rather than speculative schema churn.

## Database Decisions

- `InitialDomainModel`, `AddPhase4CatalogPricingInventory`, and `AddPhase5OrdersPaymentsInvoices` are committed and pushed and will never be edited, deleted, squashed, or removed from migration history.
- Whether `InitialDomainModel` was applied to a particular database is still checked through `dbo.__EFMigrationsHistory`, but commit/push status already makes the migration immutable.
- No database with real data is dropped. No applied migration-history row is deleted. Potentially destructive commands require an explicit target, backup/restore consideration, and a report before execution.
- Future changes are additive and backward-compatible where practical. A new EF Core migration must trace every schema change.
- New money columns use `bigint` Iranian rials. Weights and percentages use explicit decimal precision; `float` and `double` are prohibited.
- Foreign keys use `NO ACTION`/restrict semantics unless an explicitly documented aggregate-owned exception is proven safe. Category parents and categories with products never cascade-delete.
- SQL constraints and unique/filtered indexes provide final duplicate, state, and concurrency boundaries; Application validation provides useful errors before a constraint is reached.
- Existing immutable ledgers, market/calculation snapshots, order/invoice snapshots, callbacks, and print logs are append-only or protected from hard deletion.
- Outbox processing must reuse the existing table. An Inbox, printer, profile, or print-job table is added only in its assigned phase and only with a concrete workflow and uniqueness boundary.

## Security Decisions

- Provider API keys, payment credentials, JWT keys, MFA secrets, Data Protection keys, registration tokens, connection strings, and other credentials never enter committed configuration or business tables in plain text.
- Market and payment source rows store only non-secret configuration references. Raw provider/callback payloads are represented only by a cryptographic hash and sanitized metadata when persistence is required.
- Authenticator keys and recovery-code payloads use ASP.NET Core Data Protection. Production key rings must be persistent, shared where necessary, access-controlled, backed up, and covered by rotation/recovery procedures.
- Refresh tokens remain hashed, family-linked, single-use, rotated, reuse-detected, and revocable through live sessions/security stamps.
- Customer data is ownership-scoped. Cross-customer operations, permission/role changes, manual payments, gateway configuration, store settings, status changes, invoice voiding, outbox reprocessing, and device/printer operations require explicit permissions.
- Admins cannot manage Owners or grant access they do not possess. The final active Owner cannot be disabled, demoted, or deleted, and these rules must execute transactionally in administration use cases.
- SignalR group membership is server-controlled. Device connections require active approved device identity; browser/user connections require current authenticated user state.
- Logs, dead-letter errors, callback metadata, printer failures, and health responses are sanitized and do not expose secrets or unnecessary personal/system information.

## Known Risks

- This environment has no `dotnet`, MSBuild, C# compiler, container runtime, or cached .NET SDK package. The complete Solution/WPF build, .NET tests, `dotnet ef migrations list`, and `HasPendingModelChanges` cannot be executed here. Phase 7B React checks, production build, and dependency audit do run here.
- The target SQL Server is reachable only from the user's Windows machine. SQL Server claim concurrency and the unchanged migration/model state must be verified there before Phase 6 closes.
- Phase 3 has persistence and pure policies for `RolePermission` and `TrustedDevice`, but no complete administration/trusted-device application/API workflow is visible. That retrospective work remains explicitly tracked outside Phase 6.
- There is no repository CI workflow, so Git history contains no independent Build/Test result for commit `2607d4d`.
- No production market-price provider, payment gateway adapter, or secret-delivery mechanism has been selected. The existing contracts and safe orchestration must not be mistaken for production integrations.
- Phase 6 implements the existing `OutboxMessage` lifecycle locally. Phase 7C-A
  activates local invoice print logs; `DesktopDevice` remains the forward
  foundation for Phase 7C-B device-bound printing.
- SignalR delivery is in-process. A multi-node API deployment requires a supported backplane or managed SignalR service; SQL Outbox claiming alone does not fan a notification out to connections on every API node.
- Refunds and returns have materially different inventory, payment, accounting, and fiscal consequences; implementing them before business rules are confirmed would create unsafe generic behavior.

## Next Step

1. Run restore, Release build, and the complete test suite on the target Windows/.NET 8 environment.
2. Start `GoldInvoice.Api` and `VendomeJewleryDesktopApp` together and verify password login, required MFA, first-login MFA enrollment, refresh rotation across restart, logout, and rejected/revoked sessions.
3. Disconnect and reconnect the API and verify that the React client receives only its user/role audiences, de-duplicates event IDs, and advances the recovery cursor.
4. Confirm `GoldInvoiceDbContext.Database.HasPendingModelChanges()` remains false and the migration list contains the three earlier migrations plus `AddPhase7BusinessDirectories`.
5. Record Phase 7C-A invoice-document verification, then implement Phase 7C-B device enrollment, printer discovery, and device-bound profiles through a new additive migration.

## Phase Completion Report Template

Every phase closes with a report containing:

- Phase completed
- Requirements completed
- Requirements deferred and their assigned phase
- Entities added
- Tables added
- Constraints added
- Indexes added
- EF Core migrations created
- Endpoints created
- Tests created
- Restore result
- Build result
- Test result
- Migration-list and `__EFMigrationsHistory` result
- Migration application result
- Remaining risks
- Commit SHA
- Push result
- Next phase
