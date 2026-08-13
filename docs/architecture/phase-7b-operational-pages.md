# Phase 7B: Operational desktop pages

Phase 7B completes the authenticated management surface before printer/device work. Every sidebar item now resolves to a real client route. Existing Phase 4/5 services remain authoritative for catalog, inventory, orders, payments, invoices, customer addresses, pricing, and store settings.

## Data rules

- Dashboard totals, charts, market cards, and recent activity are computed only from authenticated API responses. The earlier visual mock is not used at runtime.
- The greeting uses `GET /api/v1/auth/me`; the full database `DisplayName` remains in the profile menu and its first whitespace-delimited part is used in the greeting.
- Customers and employees are Identity users resolved through roles. Customer/Admin creation uses `UserManager`; temporary passwords are never persisted by React.
- Suppliers and CRM interactions are the only new business entities because no suitable persistent model existed. They use one additive migration, rowversion concurrency, audit fields, bounded values, permissions, and SQL Server constraints.
- Inventory and payment list endpoints expose existing entities and apply the same authorization and page-size limits as their detail endpoints.
- Global search is performed over the already-authorized page data. It does not bypass endpoint ownership or permission checks.

## New routes

- `GET|POST /api/v1/people/customers`
- `GET|POST /api/v1/people/employees`
- `GET|POST|PUT|DELETE /api/v1/suppliers`
- `GET|POST /api/v1/crm/interactions`
- `POST /api/v1/crm/interactions/{id}/status`
- `GET /api/v1/inventory/items`
- `GET /api/v1/payments`

The additive migration is `20260811143000_AddPhase7BusinessDirectories`. Earlier migrations are unchanged.
The bundled migration script explicitly resolves `ConnectionStrings__GoldInvoice`
before invoking EF Core so the design-time factory cannot redirect the update to
its fallback database.

## Client workflow

The Desktop client supports product plus variant/pricing creation, warehouse creation and stock receipt, customer and address creation, order creation with inventory reservation, manual settlement and automatic invoice issuance, employee administration, supplier registration, CRM follow-up, store-profile editing, session inspection, and cross-module search.

Local invoice preview, PDF export, and print acknowledgement are completed in
Phase 7C-A. Device enrollment, printer discovery, and device-bound durable
dispatch remain Phase 7C-B.
