# Phase 4: Catalog, pricing, market prices, and inventory

## Scope

Phase 4 implements the product, gold-pricing, market-price, and inventory workflows needed before orders can become authoritative sales. It extends the shared Phase 2 database with one additive migration; `InitialDomainModel` is not changed. Customer addresses, order and invoice price snapshots, payment gateways, atomic invoice numbering, refunds, durable outbox dispatch, SignalR, and printing remain in their roadmap phases.

Controllers map HTTP contracts only. Catalog, pricing, and inventory use cases are exposed by Application interfaces and implemented in Infrastructure. The deterministic price calculator remains in Application and has no EF Core or ASP.NET Core dependency.

## Additive data model

Migration `AddPhase4CatalogPricingInventory` adds seven tables:

- `catalog.ProductCategories`: hierarchical category name, unique slug, display order, active state, and self-reference with restricted deletion.
- `catalog.GoldProductDetails`: one gold definition per product variant, including karat, gross/net/stone/other weights, wage policy, profit, tax, and variable-weight behavior.
- `pricing.ProductPricingRules`: effective-dated fixed, weight-based, market-based, or manual-review rules.
- `pricing.MarketPriceSources`: non-secret provider metadata, priority, health timestamps, and a reference to external configuration.
- `pricing.MarketPriceSnapshots`: append-only validated quotes identified by source, price type, capture time, and raw-payload hash.
- `pricing.PriceCalculationSnapshots`: append-only itemized calculation results referencing the exact rule and optional market snapshot.
- `inventory.InventoryUnits`: optional physical-piece tracking with unique serial/barcode, actual weights, karat, acquisition cost, current warehouse, and lifecycle status.

The migration also adds optional category links to products, optional unit links and reservation balances to the existing stock ledger, an alternate inventory-item key used to enforce unit location consistency, and filtered indexes for active unit reservations. All new relationships use `NO ACTION`/restrict behavior. Monetary values are `bigint` Iranian rials; weights and percentages use explicit decimal precision; mutable aggregate rows use `rowversion`. Wage rules expose one logical value through the API but persist it in mutually exclusive typed columns: `bigint` for fixed/per-gram rials and `decimal(9,4)` for a percentage.

Existing Phase 2 `ProductVariant` columns are retained for migration compatibility. A pre-Phase-4 variant with no `GoldProductDetail` remains readable and is returned with a null gold detail. Its first administrative update creates the authoritative detail. Pricing rules and physical-unit receipts are rejected until that detail exists, avoiding guessed backfill of karat or weight.

## Pricing policy

The server selects the one active rule effective at its current UTC time. The public request cannot supply the evaluation time or a final total. Variable-weight variants may supply actual gross and net weights; fixed-weight variants must use catalog weights.

For market pricing, the gold component is:

```text
market sell price per gram × net gold weight × item karat / quote reference karat
```

`Gold18K` uses reference karat 18 and `Gold24K` uses 24. Weight-based pricing uses the rule's fixed per-gram price for the variant's own karat. A fixed-price rule treats its value as the complete final price and does not apply a second wage, profit, or tax markup. A manual-review rule cannot produce an automatic price.

For calculated weight or market prices:

```text
wage   = fixed rials | rials per gross gram | percentage of gold value
profit = (gold value + wage) × profit percentage
tax    = (wage + profit) × tax percentage
total  = gold value + wage + profit + tax
```

Each component is independently rounded to a whole rial with `MidpointRounding.AwayFromZero`. The persisted calculation snapshot records all components, inputs, the rule, the selected market snapshot, calculation time, and the named rounding policy. Phase 5 will copy the required immutable values into order and invoice items.

## Market-price ingestion

`IMarketPriceProvider` is the vendor boundary. A provider returns typed quotes and a hash; credentials stay in environment variables or a deployment secret manager. Database rows store only a non-sensitive configuration reference. Full raw payloads and credential values are neither persisted nor logged.

The ingestion service:

- resolves only active, registered provider codes;
- applies a bounded per-attempt timeout and exponential retry;
- rejects non-positive, inverted-spread, stale, or future-dated quotes;
- stores rejected snapshots for audit without making them eligible for pricing;
- deduplicates by source, price type, and payload hash;
- selects a fresh valid quote deterministically by source priority, capture time, and identifier;
- writes only provider code, snapshot count, and exception type to structured logs.

The Worker polls configured sources and expires overdue stock reservations. No production provider is registered until a vendor and its secret-delivery mechanism are selected. Integration tests use a fake provider.

Settings under `MarketPrices` control poll interval, timeout, retry count/base delay, maximum quote age, and future clock skew. Options are bounded and validated at startup.

## Inventory safety

Aggregate inventory remains in `InventoryItems`; individually tracked jewelry is represented by `InventoryUnits`. Receiving a physical unit increments its aggregate item in the same transaction. The unit's product, variant, warehouse, karat, and fixed/variable weight rules are validated before persistence. Acquisition cost is never returned by inventory read contracts.

Every receipt, adjustment, reservation, release, confirmation, expiration, or transfer writes an append-only `StockMovement` in the same database transaction. The movement records both on-hand and reserved deltas and balances. Manual adjustments also retain their existing immutable adjustment row.

Reservations are bounded to 1–1440 minutes and default to 15 minutes at the HTTP boundary. A physical-unit reservation must have quantity one. Confirming an expired reservation is forbidden. Confirmation atomically reduces on-hand and reserved stock and marks a tracked unit sold; release or expiration restores availability. Filtered unique indexes prevent two active reservations for the same order/item or physical unit.

Client rowversions plus SQL Server rowversion checks reject stale aggregate operations. Database check constraints prevent negative on-hand/reserved balances or reserved stock above on-hand stock. Aggregate transfers are rejected while live physical units exist for the source item; tracked pieces must move through the unit-transfer workflow.

## HTTP endpoints

Catalog routes under `/api/v1/catalog`:

- `GET|POST /categories`, `GET|PUT|DELETE /categories/{categoryId}`
- `GET|POST /products`, `GET|PUT /products/{productId}`
- `POST /products/{productId}/variants`, `GET|PUT /variants/{variantId}`

Pricing routes under `/api/v1/pricing`:

- `GET /rules/variant/{productVariantId}`, `POST /rules`, `DELETE /rules/{ruleId}`
- `GET|POST /market/sources`, `POST /market/sources/{providerCode}/poll`
- `GET /market/latest/{priceType}`, `POST /calculate`

Inventory routes under `/api/v1/inventory`:

- `GET|POST /warehouses`, `GET|PUT /warehouses/{warehouseId}`
- `GET /items/{inventoryItemId}`, `GET /items/{inventoryItemId}/movements`
- `POST /receipts`, `POST /items/{inventoryItemId}/adjustments`
- `POST /units`, `GET /units/{inventoryUnitId}`, `GET /units/lookup?identifier=...`
- `POST /reservations`, `POST /reservations/{reservationId}/release|confirm`
- `POST /transfers`, `POST /unit-transfers`

All routes require authentication and their existing Products or Inventory permission policy. Catalog and movement lists enforce a maximum page size of 100.

## Phase 3 security closure

Owner bootstrap is serialized across SQL Server instances with a serializable transaction and a transaction-owned application lock. Role and permission seeding remains idempotent inside the same boundary.

Authenticator keys and recovery-code payloads written through Identity are protected at rest with ASP.NET Core Data Protection and a stable application name. Existing plaintext Phase 3 values are protected on their first successful read. Production deployments must persist one access-controlled Data Protection key ring shared by all API instances and back it with an appropriate key-encryption mechanism; an ephemeral container key ring will make protected MFA data unreadable after restart.

## Applying the migration

Supply the connection string only through the current process or a secret provider:

```bash
dotnet tool restore
dotnet ef database update \
  --project GoldInvoice.Infrastructure \
  --startup-project GoldInvoice.Infrastructure \
  --context GoldInvoiceDbContext
```

The design-time factory reads `ConnectionStrings__GoldInvoice`. Do not edit `InitialDomainModel`, remove rows from `__EFMigrationsHistory`, or drop a database containing data.

## Verification boundary

Unit and integration coverage includes formula components and rounding, manual-review rejection, invalid gold weights, category-cycle validation, legacy-variant compatibility, provider retry and duplicate control, overlapping-rule rejection, persisted price snapshots, aggregate overselling prevention, expired reservations, physical-unit double-sale prevention, barcode lookup, unit transfer, stock-ledger entries, SQL Server metadata, filtered unique indexes, no-cascade relationships, and MFA-token protection.

The target SQL Server instance is on the user's Windows machine and cannot be reached from the execution environment. Migration application and inspection of that database's `__EFMigrationsHistory` therefore remain a local verification step.
