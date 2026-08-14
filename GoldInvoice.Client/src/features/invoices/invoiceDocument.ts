import type { Invoice } from "../operations/operations.types";
import { activeNumberLocale, formatTomansFromRials } from "../../lib/money";

function escapeHtml(value: unknown): string {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

function money(valueInRials: number): string {
  return formatTomansFromRials(valueInRials);
}

function optional(value?: string | null): string {
  return value?.trim() ? escapeHtml(value) : "—";
}

function invoiceRows(
  invoice: Invoice,
  integerFormatter: Intl.NumberFormat,
  decimalFormatter: Intl.NumberFormat,
): string {
  return invoice.items.map((item, index) => `
    <tr>
      <td>${integerFormatter.format(index + 1)}</td>
      <td class="description-cell">
        <strong>${escapeHtml(item.productName)}</strong>
        <span>${escapeHtml(item.variantName)}</span>
      </td>
      <td class="ltr">${escapeHtml(item.sku)}</td>
      <td>${item.karat ? integerFormatter.format(item.karat) : "—"}</td>
      <td>${item.grossWeightGrams ? decimalFormatter.format(item.grossWeightGrams) : "—"}</td>
      <td>${integerFormatter.format(item.quantity)}</td>
      <td>${money(item.unitPriceRials)}</td>
      <td><strong>${money(item.lineTotalRials)}</strong></td>
    </tr>
  `).join("");
}

export function invoiceFileName(invoice: Invoice): string {
  return `Vendome-${invoice.invoiceNumber.replaceAll(/[^a-zA-Z0-9_-]/g, "-")}.pdf`;
}

export function buildInvoiceDocumentHtml(invoice: Invoice): string {
  const english = activeNumberLocale() === "en-US";
  const locale = english ? "en-US" : "fa-IR";
  const integerFormatter = new Intl.NumberFormat(locale, { maximumFractionDigits: 0 });
  const decimalFormatter = new Intl.NumberFormat(locale, { maximumFractionDigits: 3 });
  const dateFormatter = new Intl.DateTimeFormat(english ? "en-US" : "fa-IR-u-ca-persian", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
  const copy = english ? {
    invoice: "Sales invoice",
    voided: "Voided",
    paid: "Paid",
    gallery: "Vendome Gallery",
    buyer: "Buyer information",
    customerName: "Customer name",
    phone: "Phone",
    nationalId: "National ID",
    address: "Address",
    postalCode: "Postal code",
    seller: "Seller and document information",
    store: "Store",
    issueDate: "Issue date",
    orderNumber: "Order number",
    economicId: "Economic ID",
    items: "Invoice items",
    row: "#",
    description: "Description",
    sku: "SKU",
    karat: "Karat",
    weight: "Weight (g)",
    quantity: "Quantity",
    unitPrice: "Unit price",
    amount: "Amount",
    paymentStatus: "Payment status: Verified",
    paymentNote: "This invoice was issued after final payment verification. Changes to printable details are recorded with a reason in the audit trail.",
    subtotal: "Items subtotal",
    discount: "Discount",
    shipping: "Shipping",
    total: "Final total",
  } : {
    invoice: "فاکتور فروش",
    voided: "باطل‌شده",
    paid: "پرداخت‌شده",
    gallery: "گالری وندوم",
    buyer: "اطلاعات خریدار",
    customerName: "نام مشتری",
    phone: "تلفن",
    nationalId: "شناسه ملی",
    address: "نشانی",
    postalCode: "کد پستی",
    seller: "اطلاعات فروشنده و سند",
    store: "فروشگاه",
    issueDate: "تاریخ صدور",
    orderNumber: "شماره سفارش",
    economicId: "شناسه اقتصادی",
    items: "اقلام فاکتور",
    row: "ردیف",
    description: "شرح کالا",
    sku: "کد کالا",
    karat: "عیار",
    weight: "وزن (گرم)",
    quantity: "تعداد",
    unitPrice: "قیمت واحد",
    amount: "مبلغ",
    paymentStatus: "وضعیت پرداخت: تأییدشده",
    paymentNote: "این فاکتور پس از تأیید نهایی پرداخت صادر شده است. هر اصلاح اطلاعات چاپی با دلیل و سابقه در سامانه ثبت می‌شود.",
    subtotal: "جمع اقلام",
    discount: "تخفیف",
    shipping: "هزینه ارسال",
    total: "مبلغ نهایی",
  };
  const address = invoice.address;
  const store = invoice.store;
  const statusLabel = invoice.status === "Voided" ? copy.voided : copy.paid;
  const addressText = address
    ? `${escapeHtml(address.province)}${english ? ", " : "، "}${escapeHtml(address.city)}${english ? ", " : "، "}${escapeHtml(address.addressLine)}`
    : "—";

  return `<!doctype html>
<html lang="${english ? "en" : "fa"}" dir="${english ? "ltr" : "rtl"}">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width,initial-scale=1" />
  <title>${copy.invoice} ${escapeHtml(invoice.invoiceNumber)}</title>
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
    .invoice-heading { text-align: ${english ? "right" : "left"}; }
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
    .description-cell { text-align: ${english ? "left" : "right"}; }
    .description-cell strong, .description-cell span { display: block; }
    .description-cell span { margin-top: 1mm; color: #7b8594; font-size: 8px; }
    .ltr { direction: ltr; }
    .summary-wrap { display: flex; align-items: flex-start; justify-content: space-between; margin-top: 7mm; gap: 8mm; }
    .payment-note { flex: 1; padding: 4mm; color: #596579; background: #fbf8f2; ${english ? "border-left" : "border-right"}: 1.2mm solid #c7a971; line-height: 1.9; }
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
    ${invoice.status === "Voided" ? `<div class="void-watermark">${copy.voided}</div>` : ""}
    <header class="header">
      <div class="brand">
        <div class="brand-mark">V</div>
        <div><h1>VENDOME</h1><p>${optional(store?.tradeName || copy.gallery)}</p></div>
      </div>
      <div class="invoice-heading">
        <h2>${copy.invoice}</h2>
        <span class="number">${escapeHtml(invoice.invoiceNumber)}</span>
        <span class="status">${statusLabel}</span>
      </div>
    </header>

    <section class="meta-grid">
      <div class="info-box">
        <h3>${copy.buyer}</h3>
        <div class="info-row"><span>${copy.customerName}</span><strong>${optional(invoice.customerNameSnapshot)}</strong></div>
        <div class="info-row"><span>${copy.phone}</span><strong class="ltr">${optional(address?.phoneNumber)}</strong></div>
        <div class="info-row"><span>${copy.nationalId}</span><strong class="ltr">${optional(invoice.customerNationalIdSnapshot)}</strong></div>
        <div class="info-row"><span>${copy.address}</span><strong>${addressText}</strong></div>
        <div class="info-row"><span>${copy.postalCode}</span><strong class="ltr">${optional(address?.postalCode)}</strong></div>
      </div>
      <div class="info-box">
        <h3>${copy.seller}</h3>
        <div class="info-row"><span>${copy.store}</span><strong>${optional(store?.legalName || store?.tradeName)}</strong></div>
        <div class="info-row"><span>${copy.phone}</span><strong class="ltr">${optional(store?.phoneNumber)}</strong></div>
        <div class="info-row"><span>${copy.issueDate}</span><strong>${escapeHtml(dateFormatter.format(new Date(invoice.issuedAt)))}</strong></div>
        <div class="info-row"><span>${copy.orderNumber}</span><strong class="ltr">${escapeHtml(invoice.orderId.slice(0, 8).toUpperCase())}</strong></div>
        <div class="info-row"><span>${copy.economicId}</span><strong class="ltr">${optional(store?.economicCode)}</strong></div>
      </div>
    </section>

    <table aria-label="${copy.items}">
      <thead><tr><th>${copy.row}</th><th>${copy.description}</th><th>${copy.sku}</th><th>${copy.karat}</th><th>${copy.weight}</th><th>${copy.quantity}</th><th>${copy.unitPrice}</th><th>${copy.amount}</th></tr></thead>
      <tbody>${invoiceRows(invoice, integerFormatter, decimalFormatter)}</tbody>
    </table>

    <section class="summary-wrap">
      <div class="payment-note">
        <strong>${copy.paymentStatus}</strong>
        ${copy.paymentNote}
      </div>
      <div class="totals">
        <div class="total-row"><span>${copy.subtotal}</span><strong>${money(invoice.subtotalRials)}</strong></div>
        <div class="total-row"><span>${copy.discount}</span><strong>${money(invoice.discountRials)}</strong></div>
        <div class="total-row"><span>${copy.shipping}</span><strong>${money(invoice.shippingRials)}</strong></div>
        <div class="total-row grand"><span>${copy.total}</span><strong>${money(invoice.grandTotalRials)}</strong></div>
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
