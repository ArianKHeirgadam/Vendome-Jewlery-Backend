import type { Invoice } from "../operations/operations.types";

const rialFormatter = new Intl.NumberFormat("fa-IR", { maximumFractionDigits: 0 });
const decimalFormatter = new Intl.NumberFormat("fa-IR", { maximumFractionDigits: 3 });
const dateFormatter = new Intl.DateTimeFormat("fa-IR-u-ca-persian", {
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
  hour: "2-digit",
  minute: "2-digit",
});

function escapeHtml(value: unknown): string {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

function rials(value: number): string {
  return `${rialFormatter.format(value)} ریال`;
}

function optional(value?: string | null): string {
  return value?.trim() ? escapeHtml(value) : "—";
}

function invoiceRows(invoice: Invoice): string {
  return invoice.items.map((item, index) => `
    <tr>
      <td>${rialFormatter.format(index + 1)}</td>
      <td class="description-cell">
        <strong>${escapeHtml(item.productName)}</strong>
        <span>${escapeHtml(item.variantName)}</span>
      </td>
      <td class="ltr">${escapeHtml(item.sku)}</td>
      <td>${item.karat ? rialFormatter.format(item.karat) : "—"}</td>
      <td>${item.grossWeightGrams ? decimalFormatter.format(item.grossWeightGrams) : "—"}</td>
      <td>${rialFormatter.format(item.quantity)}</td>
      <td>${rials(item.unitPriceRials)}</td>
      <td><strong>${rials(item.lineTotalRials)}</strong></td>
    </tr>
  `).join("");
}

export function invoiceFileName(invoice: Invoice): string {
  return `Vendome-${invoice.invoiceNumber.replaceAll(/[^a-zA-Z0-9_-]/g, "-")}.pdf`;
}

export function buildInvoiceDocumentHtml(invoice: Invoice): string {
  const address = invoice.address;
  const store = invoice.store;
  const statusLabel = invoice.status === "Voided" ? "باطل‌شده" : "پرداخت‌شده";
  const addressText = address
    ? `${escapeHtml(address.province)}، ${escapeHtml(address.city)}، ${escapeHtml(address.addressLine)}`
    : "—";

  return `<!doctype html>
<html lang="fa" dir="rtl">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width,initial-scale=1" />
  <title>فاکتور ${escapeHtml(invoice.invoiceNumber)}</title>
  <style>
    @page { size: A4 portrait; margin: 0; }
    * { box-sizing: border-box; }
    html, body { margin: 0; padding: 0; background: #e7e9ed; color: #17263d; }
    body { font-family: Tahoma, "Segoe UI", sans-serif; font-size: 11px; -webkit-print-color-adjust: exact; print-color-adjust: exact; }
    .page { position: relative; width: 210mm; min-height: 297mm; margin: 12px auto; padding: 15mm 14mm 22mm; background: #fff; box-shadow: 0 12px 45px rgba(13, 27, 48, .18); overflow: hidden; }
    .page::before { content: ""; position: absolute; inset: 0 0 auto; height: 7mm; background: #142844; }
    .page::after { content: ""; position: absolute; top: 7mm; right: 0; width: 52mm; height: 1.4mm; background: #c7a971; }
    .header { display: flex; align-items: flex-start; justify-content: space-between; margin-top: 1mm; padding-bottom: 8mm; border-bottom: 1px solid #d9dee6; }
    .brand { display: flex; align-items: center; gap: 4mm; }
    .brand-mark { display: grid; width: 17mm; height: 17mm; place-items: center; color: #142844; border: 1.2mm solid #c7a971; border-radius: 50%; font-family: Georgia, serif; font-size: 20px; }
    .brand h1 { margin: 0; color: #142844; font-family: Georgia, serif; font-size: 21px; letter-spacing: .12em; }
    .brand p { margin: 1.5mm 0 0; color: #748094; font-size: 10px; }
    .invoice-heading { text-align: left; }
    .invoice-heading h2 { margin: 0; font-size: 20px; font-weight: 800; }
    .invoice-heading .number { display: block; margin-top: 1.5mm; color: #9a7740; font-size: 12px; font-weight: 700; direction: ltr; }
    .status { display: inline-block; margin-top: 2.5mm; padding: 1.4mm 3mm; color: ${invoice.status === "Voided" ? "#922c2c" : "#27644b"}; background: ${invoice.status === "Voided" ? "#fbeaea" : "#e9f5ef"}; border-radius: 20px; font-size: 9px; }
    .meta-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 4mm; margin: 7mm 0; }
    .info-box { padding: 4.5mm; background: #f6f7f9; border: 1px solid #e1e4e9; border-radius: 3mm; }
    .info-box h3 { margin: 0 0 3mm; color: #9a7740; font-size: 10px; }
    .info-row { display: grid; grid-template-columns: 26mm 1fr; margin-top: 1.7mm; line-height: 1.75; }
    .info-row span:first-child { color: #7b8594; }
    .info-row strong { font-weight: 600; overflow-wrap: anywhere; }
    table { width: 100%; border-collapse: collapse; table-layout: fixed; }
    thead { display: table-header-group; }
    tr { break-inside: avoid; page-break-inside: avoid; }
    th { padding: 3mm 1.5mm; color: #fff; background: #142844; font-size: 9px; font-weight: 600; }
    td { padding: 3mm 1.5mm; border-bottom: 1px solid #e2e5ea; text-align: center; vertical-align: middle; font-size: 9px; overflow-wrap: anywhere; }
    tbody tr:nth-child(even) { background: #fafafa; }
    th:nth-child(1), td:nth-child(1) { width: 8mm; }
    th:nth-child(2), td:nth-child(2) { width: 43mm; }
    th:nth-child(3), td:nth-child(3) { width: 24mm; }
    th:nth-child(4), td:nth-child(4) { width: 13mm; }
    th:nth-child(5), td:nth-child(5) { width: 18mm; }
    th:nth-child(6), td:nth-child(6) { width: 13mm; }
    th:nth-child(7), td:nth-child(7), th:nth-child(8), td:nth-child(8) { width: 29mm; }
    .description-cell { text-align: right; }
    .description-cell strong, .description-cell span { display: block; }
    .description-cell span { margin-top: 1mm; color: #7b8594; font-size: 8px; }
    .ltr { direction: ltr; }
    .summary-wrap { display: flex; align-items: flex-start; justify-content: space-between; margin-top: 7mm; gap: 8mm; }
    .payment-note { flex: 1; padding: 4mm; color: #596579; background: #fbf8f2; border-right: 1.2mm solid #c7a971; line-height: 1.9; }
    .payment-note strong { display: block; color: #17263d; }
    .totals { width: 70mm; }
    .total-row { display: flex; justify-content: space-between; padding: 2.2mm 0; border-bottom: 1px solid #e3e6eb; }
    .total-row span { color: #6c7788; }
    .total-row.grand { margin-top: 2mm; padding: 3mm; color: #fff; background: #142844; border: 0; border-radius: 2mm; font-size: 12px; }
    .total-row.grand span { color: #d9c49b; }
    .footer { position: absolute; right: 14mm; bottom: 10mm; left: 14mm; display: flex; justify-content: space-between; padding-top: 3mm; color: #7b8594; border-top: 1px solid #e1e4e9; font-size: 8px; }
    .void-watermark { position: absolute; top: 132mm; right: 30mm; z-index: 2; color: rgba(146, 44, 44, .14); font-size: 48px; font-weight: 900; transform: rotate(-24deg); pointer-events: none; }
    @media print {
      html, body { background: #fff; }
      .page { width: 210mm; min-height: 297mm; margin: 0; box-shadow: none; }
    }
  </style>
</head>
<body>
  <main class="page">
    ${invoice.status === "Voided" ? '<div class="void-watermark">باطل‌شده</div>' : ""}
    <header class="header">
      <div class="brand">
        <div class="brand-mark">V</div>
        <div><h1>VENDOME</h1><p>${optional(store?.tradeName || "گالری وندوم")}</p></div>
      </div>
      <div class="invoice-heading">
        <h2>فاکتور فروش</h2>
        <span class="number">${escapeHtml(invoice.invoiceNumber)}</span>
        <span class="status">${statusLabel}</span>
      </div>
    </header>

    <section class="meta-grid">
      <div class="info-box">
        <h3>اطلاعات خریدار</h3>
        <div class="info-row"><span>نام مشتری</span><strong>${optional(invoice.customerNameSnapshot)}</strong></div>
        <div class="info-row"><span>شماره تلفن</span><strong class="ltr">${optional(address?.phoneNumber)}</strong></div>
        <div class="info-row"><span>شناسه ملی</span><strong class="ltr">${optional(invoice.customerNationalIdSnapshot)}</strong></div>
        <div class="info-row"><span>نشانی</span><strong>${addressText}</strong></div>
        <div class="info-row"><span>کد پستی</span><strong class="ltr">${optional(address?.postalCode)}</strong></div>
      </div>
      <div class="info-box">
        <h3>اطلاعات فروشنده و سند</h3>
        <div class="info-row"><span>فروشگاه</span><strong>${optional(store?.legalName || store?.tradeName)}</strong></div>
        <div class="info-row"><span>تلفن</span><strong class="ltr">${optional(store?.phoneNumber)}</strong></div>
        <div class="info-row"><span>تاریخ صدور</span><strong>${escapeHtml(dateFormatter.format(new Date(invoice.issuedAt)))}</strong></div>
        <div class="info-row"><span>شماره سفارش</span><strong class="ltr">${escapeHtml(invoice.orderId.slice(0, 8).toUpperCase())}</strong></div>
        <div class="info-row"><span>شناسه اقتصادی</span><strong class="ltr">${optional(store?.economicCode)}</strong></div>
      </div>
    </section>

    <table aria-label="اقلام فاکتور">
      <thead><tr><th>ردیف</th><th>شرح کالا</th><th>کد کالا</th><th>عیار</th><th>وزن (گرم)</th><th>تعداد</th><th>قیمت واحد</th><th>مبلغ</th></tr></thead>
      <tbody>${invoiceRows(invoice)}</tbody>
    </table>

    <section class="summary-wrap">
      <div class="payment-note">
        <strong>وضعیت پرداخت: تأییدشده</strong>
        این فاکتور پس از تأیید نهایی پرداخت صادر شده است. هر اصلاح اطلاعات چاپی با دلیل و سابقه در سامانه ثبت می‌شود.
      </div>
      <div class="totals">
        <div class="total-row"><span>جمع اقلام</span><strong>${rials(invoice.subtotalRials)}</strong></div>
        <div class="total-row"><span>تخفیف</span><strong>${rials(invoice.discountRials)}</strong></div>
        <div class="total-row"><span>هزینه ارسال</span><strong>${rials(invoice.shippingRials)}</strong></div>
        <div class="total-row grand"><span>مبلغ نهایی</span><strong>${rials(invoice.grandTotalRials)}</strong></div>
      </div>
    </section>

    <footer class="footer">
      <span>${optional(store?.addressLine)}</span>
      <span class="ltr">${escapeHtml(invoice.invoiceNumber)}</span>
    </footer>
  </main>
</body>
</html>`;
}
