# Phase 7C-A: Invoice documents, PDF export, and printing

## Scope

This increment completes the paid-invoice document workflow without changing the
authoritative order or payment transaction. A verified payment still issues one
invoice atomically. The Desktop client now opens that invoice automatically and
provides preview, controlled correction, PDF export, and direct printing.

Secure device enrollment, printer discovery, device-bound printer profiles, and
durable cross-device dispatch remain a separate Phase 7C-B increment.

## Document workflow

1. A manual or verified online payment moves the order to `Paid` and issues the
   invoice in the existing serializable transaction.
2. The React client refreshes authoritative API data and navigates to
   `/invoices?open={invoiceId}`.
3. The invoice opens in an A4, RTL document preview. The printable document
   contains seller and buyer snapshots, phone number, address, item identity,
   SKU, karat, weight, quantity, unit price, line total, discount, shipping, and
   final total. Wage, tax, and manufacturing-cost breakdowns are not displayed.
4. `Download PDF` sends the escaped document to the WPF host. WebView2 renders
   the same document and saves it through a Windows PDF save dialog.
5. `Print` records a print request, renders the same document in a dedicated
   preview window, sends the requested copies to the Windows default printer,
   and records success or a sanitized failure code.

Printer selection and named-printer profiles remain Phase 7C-B. Until then the
operator selects the intended Windows default printer before direct printing.

## Controlled invoice correction

Financial fields, invoice number, payment, order, store snapshot, and item lines
remain locked after issuance. An authorized manager can correct only the buyer
name/national identifier and the printed recipient, phone, postal, city,
province, and address fields. Correction requires the current invoice
`rowversion` and a bounded reason. Old and new values plus the reason are written
to the existing append-only audit log in the same transaction.

A voided invoice cannot be corrected or printed. Correcting financial values
requires the existing explicit void/reissue business workflow rather than a
silent rewrite.

## HTTP endpoints

- `PUT /api/v1/invoices/{invoiceId}/document` requires `Orders.Manage`.
- `POST /api/v1/invoices/{invoiceId}/print-jobs` requires `Invoices.Print`.
- `POST /api/v1/invoices/{invoiceId}/print-jobs/{printJobId}/complete` requires
  `Invoices.Print`; only the requesting user can complete that attempt.

The service independently verifies that the invoice is issued and belongs to a
verified payment. A successful earlier print makes the next request a reprint;
reprints require `Invoices.Reprint` and a reason. Copies are limited to 1-20.
Only one recent unacknowledged attempt is allowed. If the Desktop process stops,
an attempt older than five minutes is closed with the sanitized
`PRINT_ACK_TIMEOUT` code before a new request is accepted.

## Desktop boundary

Invoice HTML is generated only from API snapshots with every dynamic string
HTML-escaped. The WPF bridge accepts only `preview`, `save`, and `print`, limits
the payload size, disables script in the document WebView, blocks external
navigation, sanitizes filenames, and never returns a local file path to React.
PDF output uses A4 portrait settings with backgrounds and without browser
headers or footers.

## Template replacement

`GoldInvoice.Client/src/features/invoices/invoiceDocument.ts` is the single
document-template boundary. The current design is a production-safe temporary
Vendome A4 layout. When the approved sample PDF is supplied, field coordinates,
typography, repeated item rows, and overflow behavior are changed only inside
this boundary; order, payment, correction, download, and print orchestration do
not change.

## Persistence and migration

No schema change is required. This increment reuses the existing
`invoicing.InvoicePrintLogs`, `audit.AuditLogs`, invoice snapshots, permissions,
and rowversion columns. The committed migration chain remains unchanged through
`AddPhase7BusinessDirectories`.

## Verification

Regression coverage verifies that paid invoices allow only audited document
correction without changing invoice number, items, or totals, and that first
prints and authorized reprints retain separate print attempts. The React
TypeScript check, production build, and dependency audit are part of
`scripts/verify-phase7b.ps1`; the full .NET/WPF build and tests run on the target
Windows .NET 8 environment.
