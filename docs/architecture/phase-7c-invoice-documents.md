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

## Phase 7C-B: device-bound printing

### Scope

This increment completes device-bound printing across the earlier backend
foundation. Secure Desktop-device enrollment, device-owned printers and print
profiles, and a durable, idempotent, retryable `InvoicePrintJob` workflow are
added through the additive migration
`20260820142605_AddPhase7CBDeviceBoundPrinting`. The new
`GoldInvoice.PrintAgent` executable polls the server for signed print jobs,
renders the server-supplied printable HTML in a hidden WebView2, prints to the
system default printer, and reports one-way signed results. The agent is a
separate .NET 8 Windows project added to the solution.

### Device enrollment and authorization

- Registration tokens are short-lived, single-use, and stored only as a
  SHA-256 hash. Enrollment is anonymous but requires a still-valid, unused
  token; reuse or expiry is rejected.
- A new Desktop device starts `pending` and is inert until an authorized
  administrator `approves` it. `IsActive` defaults to `false`; the check
  constraint `CK_DesktopDevices_State` permits exactly pending, approved-active,
  and revoked states.
- The device supplies a public key (PEM). The server computes the SHA-256
  thumbprint of the decoded DER SPKI and stores both; the client never supplies
  the thumbprint.
- After approval the device signs every poll, document, completion, and
  heartbeat request. The server verifies RSA PKCS1/SHA256 against the stored
  public key with a five-minute replay window. Boss/Admin UI or the API revokes
  a device to cut off the agent immediately.
- Poll and completion endpoints are anonymous but require a valid device
  signature; management endpoints (enrollment tokens, approval, printers,
  profiles, job dispatch, retry) require the new explicit permissions
  `DesktopDevicesManage`, `DevicePrintersManage`, `DevicePrintProfilesManage`,
  and `InvoicePrintJobsView`. Customers are denied all device, printer, job,
  and result-reporting APIs.

### Signed payloads

Signature payloads are `{operation}|{…}|{timestamp:o}` with the timestamp and a
Base64 RSA signature sent on the query string or in the body:

- `heartbeat|{deviceId:N}|{timestamp:o}`
- `poll|{deviceId:N}|{timestamp:o}`
- `document|{jobId:N}|{deviceId:N}|{timestamp:o}`
- `complete|{jobId:N}|{deviceId:N}|{timestamp:o}|{Succeeded}|{PrinterName}|{FailureCode}`

The agent stores its base URL, device id, and private key in the current user's
LocalApplicationData under DPAPI protection.

### Printers and profiles

- `DevicePrinter` is scoped to a `DesktopDeviceId`; the system printer name is
  unique per device, and at most one device printer may be `IsDefault` and
  enabled at a time (filtered unique index
  `IX_DevicePrinters_DesktopDeviceId_IsDefault`).
- `PrintProfile` captures paper size, orientation, copy count (1-20), color
  mode, and typed margins, with at most one enabled default per device
  (`IX_PrintProfiles_DesktopDeviceId_IsDefault`).
- Before dispatch the server verifies the printer belongs to the approved
  device and is enabled.

### Durable job workflow

- `POST /api/v1/invoices/{invoiceId}/device-print-jobs` records a durable
  `InvoicePrintJob` with an idempotency key. A repeated key for the same invoice
  returns the existing job; reusing a key for a different invoice or creating a
  second pending job is a conflict.
- The agent files only `Requested` jobs and reports either `Succeeded` or the
  sanitized failure code. Failure codes are limited to
  `PRINTER_UNAVAILABLE`, `PRINTER_OFFLINE`, `OUT_OF_PAPER`,
  `PRINTER_JAM`, `PRINT_CANCELLED`, and `GENERIC_FAILURE`; raw operating-system
  text is rejected.
- Every attempt writes an immutable `InvoicePrintLog` row. Completion targets
  the newest `Requested` attempt and never rewrites a terminal attempt; a retry
  (`RetryCount++`) appends a new attempt only from `Failed`. Reprints require
  the reprint permission and a reason and never erase earlier attempts. The
  `IX_InvoicePrintLogs_PrintJobId` index is non-unique to allow multiple
  attempts per job.
- Document GET is signed and returns the server-rendered printable A4 RTL HTML
  built only from authoritative snapshot fields with every dynamic string
  HTML-escaped and scripts disabled.

### Agent

`GoldInvoice.PrintAgent` runs `enroll --server … --token … [--name …]` to bind
its generated RSA identity, then `run` to poll every ten seconds, print each
pending job through a hidden WPF WebView2 `PrintAsync` to the system default
printer, and complete with a signed report. `CoreWebView2PrintStatus` is mapped
to the sanitized failure codes above.

### Verification

`GoldInvoice.IntegrationTests/PhaseSevenCBDevicePrintingTests.cs` covers
enrollment lifecycle and token replay, printer ownership/enabled checks,
idempotency-key reuse, cross-invoice and duplicate-pending conflicts,
signature authorization including impostor, stale, and replayed requests,
sanitized failure codes, retry plus immutable log history, reprint approval,
one-default-printer/profile enforcement, and signed document retrieval. The
full suite is 112 tests, all passing.
