# Phase 2: Domain and database model

## Scope

Phase 2 adds the complete initial SQL Server model, the domain entity types, EF Core configurations, persistence registration, database readiness monitoring, audit timestamps, optimistic concurrency, and the `InitialDomainModel` migration. It does not implement login, JWT issuance, permission evaluation, MFA, product endpoints, inventory workflows, payment processing, or background outbox processing; those behaviors belong to later phases.

The domain project remains free of EF Core, ASP.NET Core, SQL Server, and Identity dependencies. `ApplicationUser` and `ApplicationRole` are persistence identities in Infrastructure. Business entities refer to users by `Guid`, and Infrastructure maps those references to Identity foreign keys.

## Storage conventions

- SQL Server is the authoritative store. EF Core and SQL Server packages remain on `8.0.29`, and every project remains on `net8.0`.
- All business identifiers are `uniqueidentifier` values created before persistence. `RolePermission` is the only business join entity with a composite primary key.
- Every modeled business entity has `CreatedAt`, `UpdatedAt`, `CreatedBy`, and `UpdatedBy`. Mutable rows also have a SQL Server `rowversion`; the save interceptor writes audit times with `TimeProvider` and normalizes every `DateTimeOffset` value to UTC.
- Monetary values are `bigint` values in Iranian rials. Gold weights are `decimal(18,3)` grams. Purity is an integer from 1 through 1000.
- Product, product-variant, product-image, and warehouse rows use soft deletion and a default EF query filter. Financial, stock-ledger, and audit records are protected from hard deletion in the persistence boundary.
- Every mapped relationship uses `NO ACTION`. Removing a user, product, order, payment, or invoice cannot cascade into financial or historical data.
- Secrets are not stored in `SystemSetting`. Sensitive settings retain only a secret-manager reference. Refresh tokens, device identifiers, login identifiers, idempotency keys, and callback payloads are represented by hashes or sanitized values.
- The base configuration contains no deployable connection string. Production must provide `ConnectionStrings__GoldInvoice`; the Development files contain only a credential-free LocalDB profile.

## Schemas

| Schema | Responsibility |
|---|---|
| `security` | Identity, permissions, sessions, token hashes, trusted devices, and security telemetry |
| `catalog` | Products, variants, and object-storage image metadata |
| `inventory` | Warehouses, stock balances, reservations, movements, and adjustments |
| `sales` | Orders and immutable order snapshots |
| `billing` | Payments, attempts, and deduplicated callbacks |
| `invoicing` | Invoice snapshots, invoice lines, and print history |
| `devices` | Registered desktop clients |
| `integration` | Transactional outbox state |
| `audit` | Append-only audit records |
| `configuration` | Non-secret settings and secret references |
| `platform` | Idempotency state |

## Entity matrix

Unless a row states otherwise, its primary key is `Id`, audit fields are required except nullable actor identifiers, `RowVersion` is a SQL Server concurrency token, and all foreign keys use `NO ACTION`.

| Entity | Keys and relationships | Required and nullable data | Constraints and query indexes | Lifecycle |
|---|---|---|---|---|
| `ApplicationUser` | PK `Id`; principal for customer, actor, session, and device references | Required Identity fields when activated, `DisplayName`, `IsActive`; nullable `DeactivatedAt` | Identity normalized-name/email indexes; active state must agree with `DeactivatedAt` | No seed or hardcoded owner; `rowversion`; no cascade |
| `ApplicationRole` | PK `Id`; principal for user-role and role-permission rows | Required `Name`, `Description`; `IsSystem` defaults false | Unique normalized role name | `rowversion`; no cascade |
| `Permission` | PK `Id` | Required `Name`, `DisplayName`, `Group`; nullable `Description`; active defaults true | Unique `Name`; `(Group, IsActive)` | `rowversion`; no cascade |
| `RolePermission` | Composite PK `(RoleId, PermissionId)`; FKs to role, permission, nullable granting user | Required `GrantedAt`; nullable `GrantedBy` | Index on `PermissionId`; duplicate grants rejected by PK | Audited and versioned join row |
| `RefreshToken` | PK `Id`; FKs to user, session, nullable parent and replacement token | Required token hash, family, expiry; nullable used/revoked times and reason | Unique token hash; family and active-session indexes; expiry/lifecycle checks | Hash only; `rowversion`; no cascade |
| `UserSession` | PK `Id`; FKs to user and nullable trusted device | Required expiry, last-seen time, security-stamp snapshot; nullable revocation, IP, user-agent hash | `(UserId, RevokedAt, ExpiresAt)`; expiry and revocation checks | Revoked instead of deleted; `rowversion` |
| `TrustedDevice` | PK `Id`; FK to user | Required identifier hash, display name, trust expiry; nullable last-use/revocation | Unique `(UserId, DeviceIdentifierHash)`; active-device index; expiry check | Hash only; revoked instead of deleted |
| `LoginAttempt` | PK `Id`; nullable FK to user | Required identifier hash, success flag, occurrence; nullable failure, IP, user-agent hash | Identifier/time and IP/time indexes | Append-only; raw identifier is not stored |
| `SecurityEvent` | PK `Id`; nullable FKs to user and session | Required event type, severity, occurrence; nullable correlation, IP, sanitized JSON | User/time and severity/time indexes; allowed-severity check | Append-only |
| `Product` | PK `Id` | Required name and slug; nullable description; active defaults true | Unique live slug; live active/name index | Soft delete and query filter |
| `ProductVariant` | PK `Id`; FK to product; alternate key `(ProductId, Id)` | Required SKU, name, weight, purity, labor fee; nullable fixed price | Unique live SKU; product/active index; positive weight, purity, and nonnegative price checks | Soft delete and query filter |
| `ProductImage` | PK `Id`; FK to product; optional composite FK `(ProductId, ProductVariantId)` guarantees the variant belongs to the same product | Required storage key, content type, sort order; nullable variant and alt text | Unique storage key; ordered product index; one live primary image per product/variant | Soft delete and query filter |
| `Warehouse` | PK `Id` | Required code and name; active defaults true | Unique live code | Soft delete and query filter |
| `InventoryItem` | PK `Id`; FKs to warehouse and variant | Required on-hand and reserved quantities | Unique `(WarehouseId, ProductVariantId)`; variant/quantity index; quantities nonnegative and reserved not above on-hand | `rowversion`; direct quantity writes will be implemented only with stock movements in Phase 4 |
| `StockMovement` | PK `Id`; FK to inventory item | Required type, nonzero delta, resulting balance, occurrence; nullable reference and reason | Item/time and reference indexes; movement enum, nonzero delta, nonnegative balance checks | Append-only and hard-delete protected |
| `StockReservation` | PK `Id`; FKs to inventory item and order | Required key, quantity, status, expiry; nullable confirmation/release times | Unique key and `(OrderId, InventoryItemId)`; status/expiry index; positive quantity and expiry checks | `rowversion`; released, confirmed, or expired instead of deleted |
| `InventoryAdjustment` | PK `Id`; FKs to inventory item, unique stock movement, nullable approving user | Required nonzero delta and reason; nullable approver | Unique `StockMovementId`; item/time index | Append-only and hard-delete protected |
| `Order` | PK `Id`; FK to customer user | Required order number, status, and rial totals; nullable paid/cancelled times | Unique number; customer/time and status/time indexes; exact total equation and allowed-status checks | Financial row; hard-delete protected |
| `OrderItem` | PK `Id`; FKs to order and product variant | Required line number and complete product/price snapshot | Unique `(OrderId, LineNumber)`; weight, purity, quantity, and exact line-total checks | Financial snapshot; hard-delete protected |
| `OrderStatusHistory` | PK `Id`; FK to order and nullable changing user | Required target status and occurrence; nullable prior status and reason | Order/time index; allowed-status checks | Append-only and hard-delete protected |
| `OrderAddressSnapshot` | PK `Id`; unique FK to order | Required recipient, phone, province, city, postal code, and address | Unique `OrderId` enforces one snapshot per order | Historical snapshot; hard-delete protected |
| `Payment` | PK `Id`; FK to order | Required provider, status, positive rial amount; nullable gateway identifiers and lifecycle data | Provider/authority and provider/gateway IDs are conditionally unique; order/time index; amount/status checks | Financial row; hard-delete protected |
| `PaymentAttempt` | PK `Id`; FK to payment | Required attempt number, amount, status, start; nullable provider request, completion, failure, sanitized metadata | Unique `(PaymentId, AttemptNumber)` and non-null provider request ID; amount/number/status checks | Financial row; hard-delete protected |
| `PaymentCallback` | PK `Id`; nullable FK to payment | Required provider, external callback ID, payload hash, received time; nullable sanitized payload/result | Unique `(Provider, ExternalCallbackId)` and `(Provider, PayloadHash)`; verified/time index | Append-only and hard-delete protected; no raw sensitive payload |
| `Invoice` | PK `Id`; unique FK to order; FK to customer user | Required number, issue time, status, rial totals; nullable customer identifiers and void data | Unique order and invoice number; customer/time index; exact total, status, and void-state checks | Financial snapshot; hard-delete protected |
| `InvoiceItem` | PK `Id`; FK to invoice | Required line number, complete item snapshot, weight, purity, price, quantity | Unique `(InvoiceId, LineNumber)`; weight, purity, quantity, and exact line-total checks | Financial snapshot; hard-delete protected |
| `InvoicePrintLog` | PK `Id`; FKs to invoice, requesting user, nullable desktop device | Required status, copy count, reprint flag; nullable completion, printer, failure, reason | Invoice/time and device/status/time indexes; positive copies and allowed-status checks | Historical log; hard-delete protected |
| `DesktopDevice` | PK `Id`; FK to registering user | Required identifier hash, display name, active state; nullable key thumbprint and lifecycle times | Unique identifier hash; active/last-seen index; active state must agree with revocation | Revoked instead of deleted; `rowversion` |
| `OutboxMessage` | PK `Id` | Required type, JSON payload, occurrence, retry count, status; nullable processing, retry, error, and lock data | `(Status, NextRetryAt, OccurredAt)` claim index; filtered lock index; retry/status/processing checks | Mutable delivery state with `rowversion`; processing starts in Phase 6 |
| `AuditLog` | PK `Id`; nullable FK to actor user | Required action, target type/id, occurrence; nullable correlation, IP, sanitized before/after JSON | Target/time, actor/time, and correlation indexes | Append-only and hard-delete protected |
| `SystemSetting` | PK `Id` | Required key and data type; exactly one nullable field among value and secret reference | Unique key; exclusive value-source check | `rowversion`; secrets remain outside the database row |
| `IdempotencyRecord` | PK `Id` | Required scope, key hash, request hash, status, expiry; nullable response and lock data | Unique `(Scope, KeyHash)`; status/expiry index; status, expiry, and HTTP-code checks | Hash only; mutable state with `rowversion` |

The five ASP.NET Core Identity link tables (`UserClaims`, `UserLogins`, `UserRoles`, `UserTokens`, and `RoleClaims`) retain their framework-defined primary keys and uniqueness rules, are stored in `security`, and use `NO ACTION` foreign keys.

## Migration

The initial migration is `InitialDomainModel` in `GoldInvoice.Infrastructure/Persistence/Migrations`. It creates all schemas, tables, keys, constraints, indexes, and the model snapshot. The generated idempotent SQL was verified successfully, and EF reports no pending model changes.

Apply it only after supplying a target-specific connection string:

```bash
dotnet tool restore
dotnet ef database update \
  --project GoldInvoice.Infrastructure \
  --startup-project GoldInvoice.Api \
  --context GoldInvoiceDbContext
```

No database was seeded. Owner creation, role membership, permission seeding, and all authentication behavior remain Phase 3 work.
