# Phase 5: Orders, payments, and invoices

## Scope

Phase 5 turns the Phase 4 catalog, pricing, and inventory model into an authoritative sales workflow. It adds customer addresses, protected order and invoice snapshots, payment orchestration, callback idempotency, atomic invoice numbering, unpaid-order cancellation, and invoice voiding. Financial, item, and store snapshots remain immutable; Phase 7C-A later adds only audited correction of buyer/delivery print fields. The shared migrations from Phases 2 and 4 remain unchanged; all database changes are in the additive migration `AddPhase5OrdersPaymentsInvoices`.

Returns and refunds are intentionally deferred until the business defines eligibility, stock disposition, partial-return behavior, gateway refund behavior, and fiscal-document requirements. No generic `DiscountPolicy` table is created without a confirmed rule model.

## Additive data model

The migration adds six tables:

- `sales.CustomerAddresses`: soft-deletable customer-owned addresses with one active default per customer.
- `sales.OrderStoreSnapshots`: one immutable copy of the seller identity per order.
- `billing.PaymentGateways`: gateway/provider metadata and a reference to external secret-backed configuration; credentials are never stored in this table.
- `invoicing.InvoiceSequences`: one concurrency-protected counter per configured series.
- `invoicing.InvoiceAddressSnapshots`: the protected delivery snapshot attached to
  an invoice; Phase 7C-A permits only audited printed-address corrections.
- `invoicing.InvoiceStoreSnapshots`: an immutable copy of the order seller identity attached to an invoice.

Existing order and invoice items gain nullable Phase 5 snapshot columns so legacy rows remain readable. New rows copy the exact gross/net weight, karat, market unit rate, gold value, wage, profit, tax, final price, rounding policy, physical-unit reference, and price-calculation reference. SQL constraints reject partial snapshots and require the snapshotted components to equal the unit price.

Payments gain a typed method, optional gateway reference, hashed idempotency key, cancellation time, and redirect metadata for attempts. `RequiresReview` is an explicit payment and order state for authentic callbacks that cannot safely be accepted automatically. Filtered unique indexes allow at most one pending, processing, or review payment per order and prevent duplicate provider references.

All new foreign keys use `NO ACTION`. Mutable rows retain SQL Server `rowversion`. Invoice items, address/store snapshots, order items, order history, callbacks, and stock movements are append-only or protected from hard deletion through the existing persistence policies.

## Store identity

The current store identity is stored under the stable key `Store.Profile` in the existing `configuration.SystemSettings` table. Its data type is `json:StoreProfile.v1`, and the Infrastructure service validates and maps it to a typed document. This avoids an unused one-row table while keeping API and domain callers independent from raw JSON.

Creating an order requires a valid Store Profile. The order copies the profile fields, and the invoice copies the order snapshot; later settings changes therefore never rewrite historical documents.

## Order workflow

Authenticated customers can manage only their own addresses and orders. Staff access to another customer requires an existing management permission. Reads use the same ownership boundary and return `404` for inaccessible resources.

Order creation requires an `Idempotency-Key` and runs in a serializable transaction. The server:

1. validates the active customer, address, inventory rowversions, product/variant/gold detail, and current Store Profile;
2. calculates every line through the backend Phase 4 pricing service;
3. persists complete item, address, customer, and store snapshots;
4. reserves aggregate stock or the selected physical unit and writes matching stock-ledger entries;
5. moves the order from `Pending` to `AwaitingPayment` and stores status history;
6. completes the hashed idempotency record in the same transaction.

Client totals are never accepted. Customers cannot supply discounts or shipping charges; those fields require management access, and the resulting payable total must remain positive until a separate free-order workflow is defined. Aggregate lines are unique per inventory item, while different physical units from the same SKU can be ordered as separate lines. Filtered reservation indexes preserve both rules without blocking multi-piece orders.

An unpaid order may be cancelled. Cancellation releases every active reservation, restores tracked units, writes stock movements, cancels non-final payments, and records status history atomically. Order-linked reservations cannot be confirmed or released through the generic inventory-management endpoints; only payment, order cancellation, or the expiration worker may finalize them. Orders in payment review require staff cancellation. Paid orders are not cancelled by this workflow.

## Payment workflow

`IPaymentGatewayProvider` is the external-provider boundary. A configured `PaymentGateway` contains a provider code and a non-secret `ConfigurationReference`. A deployment adapter resolves that reference through environment configuration or a secret manager. No production provider is invented or registered in this repository.

Online initiation has two database phases around the provider call. The first transaction creates one payment and attempt after checking ownership, order state, live reservations, and active-payment uniqueness. The provider call has a bounded timeout. The final database phase stores only authority/request identifiers, a validated HTTPS redirect URL, and masked metadata. Replaying the same idempotency key returns the original redirect; reusing it for different input is rejected. If the process stopped between those phases, the same key resumes the pending attempt with the same `PaymentId`; every production adapter must therefore pass that identifier to the vendor as its initiation idempotency key.

Callback routes are anonymous because providers cannot present a user JWT, but they are request-size bounded and IP rate-limited. The selected provider adapter must authenticate the callback against external secret-backed configuration. The application stores a hash and masked representation, never the raw payload. Unique provider/external-ID and provider/payload-hash indexes provide the final duplicate boundary.

An authentic callback is accepted only when the gateway, payment/order states, authority, amount, and gateway payment reference match. Mismatches and expired inventory enter `RequiresReview`; they do not mark the order paid. A successful callback verifies the payment, confirms reservations, decrements on-hand inventory, marks physical units sold, writes ledger/history rows, and issues exactly one invoice in the same serializable transaction. A declined authentic callback may omit an amount but cannot supply a conflicting one.

Manual cash, point-of-sale, bank-transfer, and card-to-card payments require `Payments.Manage`. They use the same idempotency, reservation confirmation, order transition, and invoice issuance transaction as verified online payments. They cannot replace a payment or order in `RequiresReview`; an ambiguous online charge must remain visible for explicit reconciliation rather than being silently cancelled.

## Invoice workflow

Only a verified payment for the exact order total can issue an invoice. Unique order and payment indexes make issuance idempotent. A serializable transaction allocates the next number from the configured sequence and copies all order item, address, customer, and store snapshots.

The default format is `INV-0000000001`; `Invoicing:SequenceSeries` and `Invoicing:SequencePrefix` are startup-validated safe identifiers. A series keeps its original prefix after its first issuance, and a prefix can belong to only one series, preventing a configuration change from restarting an existing visible number range. The sequence row uses `rowversion`, and the invoice number, order, and payment each have a unique index.

Invoice number, financial totals, item lines, store identity, order/payment links,
and issue state remain immutable apart from the explicit `Issued` to `Voided`
transition. Phase 7C-A adds a narrowly scoped correction path for buyer and
delivery fields used on the printed document; it requires management permission,
a reason, a current invoice rowversion, and an append-only old/new audit entry.
Voiding still requires management permission, a reason, and a current invoice
rowversion. It does not delete the invoice or rewrite financial snapshots.

## HTTP endpoints

Customer addresses:

- `GET|POST /api/v1/customers/{customerId}/addresses`
- `GET|PUT|DELETE /api/v1/customers/{customerId}/addresses/{addressId}`

Orders:

- `GET|POST /api/v1/orders`
- `GET /api/v1/orders/{orderId}`
- `POST /api/v1/orders/{orderId}/cancel`
- `POST /api/v1/orders/{orderId}/status`

Payments:

- `GET|POST /api/v1/payments/gateways`
- `PUT /api/v1/payments/gateways/{gatewayId}`
- `GET /api/v1/payments/{paymentId}`
- `POST /api/v1/payments/initiate`
- `POST /api/v1/payments/manual`
- `POST /api/v1/payments/callbacks/{providerCode}`

Invoices and settings:

- `GET /api/v1/invoices`, `GET /api/v1/invoices/{invoiceId}`
- `POST /api/v1/invoices/{invoiceId}/void`
- `GET|PUT /api/v1/settings/store-profile`

Order creation and payment initiation/manual-payment routes require an `Idempotency-Key` header of 8–128 non-control characters. Mutations that update existing aggregate rows require their Base64 `rowversion`.

## Configuration

```json
{
  "Payments": {
    "ProviderTimeoutSeconds": 15,
    "MaximumGatewayConfigurationsPerProvider": 5
  },
  "Invoicing": {
    "SequenceSeries": "DEFAULT",
    "SequencePrefix": "INV"
  },
  "RateLimiting": {
    "PaymentCallbacks": {
      "Rule": {
        "PermitLimit": 60,
        "WindowSeconds": 60
      }
    }
  }
}
```

Provider credentials, signing secrets, and connection strings must remain in user secrets, environment variables, or a deployment secret manager. `ConfigurationReference` is a lookup name, not a credential value.

## Applying and verifying the migration

```bash
dotnet tool restore
dotnet ef database update AddPhase5OrdersPaymentsInvoices \
  --project GoldInvoice.Infrastructure \
  --startup-project GoldInvoice.Infrastructure \
  --context GoldInvoiceDbContext
```

Tests cover complete/partial snapshot validation, controlled status transitions, address and filtered-index metadata, duplicate order keys, duplicate manual payments, duplicate callbacks, one-invoice issuance, payment-only confirmation and line-level integrity of order reservations, review-state payment isolation, sequence monotonicity, unique invoice-number boundaries, no-cascade relationships, and pending-model-change detection.

The SQL Server instance remains on the user's Windows machine. Restore, Release build, test execution, migration application, and `__EFMigrationsHistory` inspection are therefore local verification steps before commit and push.
