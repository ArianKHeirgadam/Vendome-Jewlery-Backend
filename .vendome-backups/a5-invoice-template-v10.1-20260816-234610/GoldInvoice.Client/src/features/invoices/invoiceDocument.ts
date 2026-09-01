import type { Invoice } from "../operations/operations.types";

const integerFormatter = new Intl.NumberFormat("fa-IR", { maximumFractionDigits: 0 });
const decimalFormatter = new Intl.NumberFormat("fa-IR", { maximumFractionDigits: 3 });
const dateFormatter = new Intl.DateTimeFormat("fa-IR-u-ca-persian", {
  year: "numeric",
  month: "long",
  day: "numeric",
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

function optional(value?: string | null): string {
  return value?.trim() ? escapeHtml(value) : "—";
}

function money(valueInRials: number): string {
  return `${integerFormatter.format(Math.round(valueInRials / 10))} تومان`;
}

function weight(value?: number | null): string {
  return value == null ? "—" : `${decimalFormatter.format(value)} گرم`;
}

function shortId(value?: string | null): string {
  if (!value) return "—";
  return escapeHtml(value.replaceAll("-", "").slice(0, 10).toUpperCase());
}

function statusLabel(status: string): string {
  const labels: Record<string, string> = {
    Issued: "صادرشده",
    Paid: "پرداخت‌شده",
    Pending: "در انتظار پرداخت",
    Draft: "پیش‌نویس",
    Overdue: "معوق",
    Voided: "باطل‌شده",
    Cancelled: "لغوشده",
  };
  return labels[status] ?? status;
}

function statusClass(status: string): string {
  if (status === "Voided" || status === "Cancelled") return "status status--danger";
  if (status === "Draft" || status === "Pending" || status === "Overdue") return "status status--pending";
  return "status status--success";
}

function absolutizeCssUrls(cssText: string, baseUrl: string): string {
  return cssText.replace(/url\((['"]?)([^'")]+)\1\)/g, (_match, _quote, rawUrl: string) => {
    if (/^(data:|blob:|https?:|file:)/i.test(rawUrl)) return `url("${rawUrl}")`;
    try {
      return `url("${new URL(rawUrl, baseUrl).href}")`;
    } catch {
      return `url("${rawUrl}")`;
    }
  });
}

/**
 * Reuses the exact Vazirmatn / EB Garamond font-face declarations already
 * loaded by the desktop client. This keeps invoice typography aligned with
 * the application without shipping a second copy of the font files.
 */
function applicationFontCss(): string {
  if (typeof document === "undefined") return "";

  const declarations: string[] = [];
  for (const sheet of Array.from(document.styleSheets)) {
    let rules: CSSRuleList;
    try {
      rules = sheet.cssRules;
    } catch {
      continue;
    }

    const baseUrl = sheet.href || document.baseURI;
    for (const rule of Array.from(rules)) {
      const cssText = rule.cssText || "";
      if (!/^@font-face/i.test(cssText)) continue;
      if (!/(Vazirmatn|EB Garamond)/i.test(cssText)) continue;
      declarations.push(absolutizeCssUrls(cssText, baseUrl));
    }
  }

  return declarations.join("\n");
}

function invoiceRows(invoice: Invoice): string {
  return invoice.items.map((item, index) => `
    <tr>
      <td class="center row-number">${integerFormatter.format(index + 1)}</td>
      <td class="product-cell">
        <strong>${escapeHtml(item.productName)}</strong>
        <span>${escapeHtml(item.variantName)}</span>
        <small class="ltr">${escapeHtml(item.sku)}</small>
      </td>
      <td class="center">${item.karat == null ? "—" : integerFormatter.format(item.karat)}</td>
      <td class="center">${weight(item.grossWeightGrams)}</td>
      <td class="center">${weight(item.netGoldWeightGrams)}</td>
      <td class="center">${integerFormatter.format(item.quantity)}</td>
      <td class="money-cell">${money(item.unitPriceRials)}</td>
      <td class="money-cell strong-money">${money(item.lineTotalRials)}</td>
    </tr>
  `).join("");
}

export function invoiceFileName(invoice: Invoice): string {
  return `Vendome-${invoice.invoiceNumber.replaceAll(/[^a-zA-Z0-9_-]/g, "-")}.pdf`;
}

export function buildInvoiceDocumentHtml(invoice: Invoice): string {
  const address = invoice.address;
  const store = invoice.store;
  const addressText = address
    ? `${escapeHtml(address.province)}، ${escapeHtml(address.city)}، ${escapeHtml(address.addressLine)}`
    : "—";
  const storeAddress = store?.addressLine?.trim() ? escapeHtml(store.addressLine) : "—";
  const fonts = applicationFontCss();
  const documentStatus = statusLabel(invoice.status);
  const recipient = address?.recipientName?.trim() || invoice.customerNameSnapshot || "—";
  const voidReason = invoice.status === "Voided" && invoice.voidReason?.trim()
    ? `<div class="void-reason"><strong>دلیل ابطال:</strong> ${escapeHtml(invoice.voidReason)}</div>`
    : "";

  return `<!doctype html>
<html lang="fa" dir="rtl">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width,initial-scale=1" />
  <title>فاکتور ${escapeHtml(invoice.invoiceNumber)}</title>
  <style>
    ${fonts}
    @page { size: A4 portrait; margin: 10mm; }
    * { box-sizing: border-box; }
    html, body { margin: 0; padding: 0; color: #273244; background: #eceff3; }
    body {
      font-family: "Vazirmatn", Tahoma, "Segoe UI", sans-serif;
      font-size: 10px;
      line-height: 1.75;
      -webkit-print-color-adjust: exact;
      print-color-adjust: exact;
    }
    .ltr { direction: ltr; unicode-bidi: embed; }
    .document { width: min(210mm, calc(100% - 24px)); margin: 12px auto; background: #ffffff; box-shadow: 0 18px 55px rgba(17,31,56,.16); }
    .sheet { position: relative; min-height: 277mm; padding: 12mm; overflow: hidden; }
    .top-band { height: 5mm; margin: -12mm -12mm 8mm; background: #172844; }
    .gold-line { width: 38mm; height: 1.2mm; margin-top: -8mm; margin-right: auto; margin-bottom: 8mm; background: #c7a971; }

    .header { display: grid; grid-template-columns: 1fr auto; align-items: start; gap: 10mm; padding-bottom: 7mm; border-bottom: 1px solid #e1ded7; }
    .brand { display: flex; align-items: center; gap: 4mm; }
    .brand-mark { display: grid; width: 16mm; height: 16mm; place-items: center; color: #172844; border: 1.1mm solid #c7a971; border-radius: 50%; font-family: "EB Garamond", Georgia, serif; font-size: 20px; font-weight: 700; }
    .brand-copy h1 { margin: 0; color: #172844; font-family: "EB Garamond", Georgia, serif; font-size: 23px; line-height: 1; letter-spacing: .12em; }
    .brand-copy strong { display: block; margin-top: 1.8mm; color: #273244; font-size: 11px; }
    .brand-copy span { display: block; margin-top: .8mm; color: #717783; font-size: 8px; }

    .invoice-meta { min-width: 58mm; padding: 4mm 4.5mm; background: #f7f5ef; border: 1px solid #e1ded7; border-radius: 3mm; }
    .invoice-meta h2 { margin: 0 0 2mm; color: #172844; font-size: 18px; line-height: 1.35; }
    .invoice-number { display: block; color: #9a7740; font-size: 12px; font-weight: 700; direction: ltr; text-align: right; }
    .invoice-date { margin-top: 1.5mm; color: #717783; font-size: 8.5px; }
    .status { display: inline-flex; margin-top: 2mm; padding: 1mm 2.6mm; border-radius: 999px; font-size: 8px; font-weight: 700; }
    .status--success { color: #356b52; background: #edf7f1; border: 1px solid #cfe8d8; }
    .status--pending { color: #8a682f; background: #fbf5e8; border: 1px solid #ead9b6; }
    .status--danger { color: #9b4545; background: #fbeeee; border: 1px solid #efcaca; }

    .party-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 4mm; margin-top: 6mm; }
    .party-card { min-width: 0; padding: 4.5mm; background: #fbfbfa; border: 1px solid #e7e4dd; border-radius: 3mm; }
    .party-card h3 { display: flex; align-items: center; gap: 2mm; margin: 0 0 3mm; color: #9a7740; font-size: 10px; }
    .party-card h3::before { width: 7mm; height: .8mm; content: ""; background: #c7a971; }
    .info-list { display: grid; grid-template-columns: 1fr 1fr; gap: 1.6mm 5mm; }
    .info-item { min-width: 0; }
    .info-item.full { grid-column: 1 / -1; }
    .info-item span { display: block; margin-bottom: .5mm; color: #8a9099; font-size: 7.5px; }
    .info-item strong { display: block; color: #273244; font-size: 9px; font-weight: 600; overflow-wrap: anywhere; }

    .document-strip { display: grid; grid-template-columns: repeat(3, 1fr); margin: 5mm 0; border: 1px solid #e7e4dd; border-radius: 2.5mm; overflow: hidden; }
    .document-strip > div { padding: 2.5mm 3mm; background: #fff; border-left: 1px solid #e7e4dd; }
    .document-strip > div:last-child { border-left: 0; }
    .document-strip span { display: block; color: #8a9099; font-size: 7.5px; }
    .document-strip strong { display: block; margin-top: .6mm; color: #273244; font-size: 9px; }

    .items-title { display: flex; align-items: center; justify-content: space-between; margin: 5mm 0 2.5mm; }
    .items-title h3 { margin: 0; color: #172844; font-size: 11px; }
    .items-title span { color: #8a9099; font-size: 7.5px; }
    table { width: 100%; border-collapse: collapse; table-layout: fixed; }
    thead { display: table-header-group; }
    tr { break-inside: avoid; page-break-inside: avoid; }
    th { padding: 2.5mm 1.2mm; color: #f8f6ef; background: #172844; border-left: 1px solid rgba(255,255,255,.1); font-size: 7.6px; font-weight: 600; text-align: center; }
    th:first-child { border-radius: 0 2mm 0 0; }
    th:last-child { border-radius: 2mm 0 0 0; border-left: 0; }
    td { padding: 2.6mm 1.2mm; border-bottom: 1px solid #e8e9ec; color: #3b4657; font-size: 8px; vertical-align: middle; }
    tbody tr:nth-child(even) { background: #fbfbfa; }
    th:nth-child(1), td:nth-child(1) { width: 8mm; }
    th:nth-child(2), td:nth-child(2) { width: 47mm; }
    th:nth-child(3), td:nth-child(3) { width: 11mm; }
    th:nth-child(4), td:nth-child(4), th:nth-child(5), td:nth-child(5) { width: 20mm; }
    th:nth-child(6), td:nth-child(6) { width: 11mm; }
    th:nth-child(7), td:nth-child(7) { width: 27mm; }
    th:nth-child(8), td:nth-child(8) { width: 29mm; }
    .center { text-align: center; }
    .row-number { color: #9a7740; font-weight: 700; }
    .product-cell { text-align: right; }
    .product-cell strong { display: block; color: #273244; font-size: 8.6px; }
    .product-cell span { display: block; margin-top: .5mm; color: #717783; font-size: 7.4px; }
    .product-cell small { display: block; margin-top: .5mm; color: #a08a61; font-size: 6.8px; text-align: right; }
    .money-cell { direction: rtl; text-align: left; white-space: nowrap; font-variant-numeric: tabular-nums; }
    .strong-money { color: #172844; font-weight: 700; }

    .summary { display: grid; grid-template-columns: 1fr 68mm; align-items: start; gap: 8mm; margin-top: 6mm; }
    .notes { min-height: 35mm; padding: 4mm; color: #626b78; background: #f9f7f2; border-right: 1.1mm solid #c7a971; border-radius: 0 2.5mm 2.5mm 0; }
    .notes strong { display: block; margin-bottom: 1.5mm; color: #273244; font-size: 9px; }
    .notes p { margin: 0; font-size: 8px; line-height: 2; }
    .void-reason { margin-top: 2.5mm; color: #9b4545; }
    .totals { padding: 3.5mm 4mm; background: #fff; border: 1px solid #e1ded7; border-radius: 3mm; }
    .total-row { display: flex; align-items: center; justify-content: space-between; gap: 5mm; padding: 2mm 0; border-bottom: 1px dashed #dedbd4; }
    .total-row:last-child { border-bottom: 0; }
    .total-row span { color: #717783; font-size: 8px; }
    .total-row strong { color: #273244; font-size: 8.5px; white-space: nowrap; }
    .total-row.grand { margin: 2mm -1mm -1mm; padding: 3mm; color: #fff; background: #172844; border-radius: 2mm; }
    .total-row.grand span { color: #e4d7ba; font-size: 9px; }
    .total-row.grand strong { color: #fff; font-size: 11px; }

    .signatures { display: grid; grid-template-columns: 1fr 1fr; gap: 18mm; margin-top: 11mm; padding: 0 8mm; }
    .signature { min-height: 18mm; text-align: center; border-top: 1px solid #cfd3da; }
    .signature span { position: relative; top: 2mm; padding: 0 3mm; color: #858b95; background: #fff; font-size: 7.5px; }

    .footer { display: grid; grid-template-columns: 1fr auto; align-items: center; gap: 6mm; margin-top: 8mm; padding-top: 3mm; color: #8a9099; border-top: 1px solid #e1ded7; font-size: 7px; }
    .footer strong { color: #9a7740; font-weight: 600; }
    .void-watermark { position: fixed; top: 125mm; right: 30mm; z-index: 9; color: rgba(155,69,69,.10); font-size: 52px; font-weight: 900; transform: rotate(-24deg); pointer-events: none; }

    @media print {
      html, body { background: #fff; }
      .document { width: auto; margin: 0; box-shadow: none; }
      .sheet { min-height: 0; padding: 0; overflow: visible; }
      .top-band { margin: -10mm -10mm 8mm; }
      .gold-line { margin-top: -8mm; }
    }
  </style>
</head>
<body>
  <main class="document">
    <section class="sheet">
      ${invoice.status === "Voided" ? '<div class="void-watermark">باطل‌شده</div>' : ""}
      <div class="top-band"></div>
      <div class="gold-line"></div>

      <header class="header">
        <div class="brand">
          <div class="brand-mark">V</div>
          <div class="brand-copy">
            <h1>VENDOME</h1>
            <strong>${optional(store?.tradeName || "گالری وندوم")}</strong>
            <span>${optional(store?.legalName)}</span>
          </div>
        </div>
        <div class="invoice-meta">
          <h2>فاکتور فروش</h2>
          <span class="invoice-number">${escapeHtml(invoice.invoiceNumber)}</span>
          <div class="invoice-date">${escapeHtml(dateFormatter.format(new Date(invoice.issuedAt)))}</div>
          <span class="${statusClass(invoice.status)}">${escapeHtml(documentStatus)}</span>
        </div>
      </header>

      <section class="party-grid">
        <article class="party-card">
          <h3>اطلاعات خریدار</h3>
          <div class="info-list">
            <div class="info-item"><span>نام مشتری</span><strong>${optional(invoice.customerNameSnapshot)}</strong></div>
            <div class="info-item"><span>نام تحویل‌گیرنده</span><strong>${optional(recipient)}</strong></div>
            <div class="info-item"><span>شماره تلفن</span><strong class="ltr">${optional(address?.phoneNumber)}</strong></div>
            <div class="info-item"><span>شناسه ملی</span><strong class="ltr">${optional(invoice.customerNationalIdSnapshot)}</strong></div>
            <div class="info-item"><span>استان / شهر</span><strong>${address ? `${escapeHtml(address.province)} / ${escapeHtml(address.city)}` : "—"}</strong></div>
            <div class="info-item"><span>کد پستی</span><strong class="ltr">${optional(address?.postalCode)}</strong></div>
            <div class="info-item full"><span>نشانی</span><strong>${addressText}</strong></div>
          </div>
        </article>

        <article class="party-card">
          <h3>اطلاعات فروشنده</h3>
          <div class="info-list">
            <div class="info-item"><span>نام تجاری</span><strong>${optional(store?.tradeName)}</strong></div>
            <div class="info-item"><span>نام حقوقی</span><strong>${optional(store?.legalName)}</strong></div>
            <div class="info-item"><span>شناسه ملی</span><strong class="ltr">${optional(store?.nationalId)}</strong></div>
            <div class="info-item"><span>کد اقتصادی</span><strong class="ltr">${optional(store?.economicCode)}</strong></div>
            <div class="info-item"><span>شماره ثبت</span><strong class="ltr">${optional(store?.registrationNumber)}</strong></div>
            <div class="info-item"><span>تلفن</span><strong class="ltr">${optional(store?.phoneNumber)}</strong></div>
            <div class="info-item"><span>کد پستی</span><strong class="ltr">${optional(store?.postalCode)}</strong></div>
            <div class="info-item full"><span>نشانی فروشگاه</span><strong>${storeAddress}</strong></div>
          </div>
        </article>
      </section>

      <section class="document-strip">
        <div><span>شماره فاکتور</span><strong class="ltr">${escapeHtml(invoice.invoiceNumber)}</strong></div>
        <div><span>شناسه سفارش</span><strong class="ltr">${shortId(invoice.orderId)}</strong></div>
        <div><span>وضعیت سند</span><strong>${escapeHtml(documentStatus)}</strong></div>
      </section>

      <div class="items-title">
        <h3>شرح اقلام</h3>
        <span>تمام مبالغ به تومان است</span>
      </div>

      <table aria-label="اقلام فاکتور">
        <thead>
          <tr>
            <th>ردیف</th>
            <th>شرح کالا / کد</th>
            <th>عیار</th>
            <th>وزن کل</th>
            <th>وزن خالص</th>
            <th>تعداد</th>
            <th>قیمت واحد</th>
            <th>مبلغ</th>
          </tr>
        </thead>
        <tbody>${invoiceRows(invoice)}</tbody>
      </table>

      <section class="summary">
        <div class="notes">
          <strong>توضیحات سند</strong>
          <p>این فاکتور به‌صورت سیستمی توسط سامانه مدیریت وندوم صادر شده است. مشخصات کالا، مبالغ و اطلاعات طرفین مطابق اطلاعات ثبت‌شده در سامانه در زمان نمایش/چاپ درج می‌شوند.</p>
          ${voidReason}
        </div>
        <div class="totals">
          <div class="total-row"><span>جمع اقلام</span><strong>${money(invoice.subtotalRials)}</strong></div>
          <div class="total-row"><span>تخفیف</span><strong>${money(invoice.discountRials)}</strong></div>
          <div class="total-row"><span>هزینه ارسال</span><strong>${money(invoice.shippingRials)}</strong></div>
          <div class="total-row grand"><span>مبلغ نهایی</span><strong>${money(invoice.grandTotalRials)}</strong></div>
        </div>
      </section>

      <section class="signatures">
        <div class="signature"><span>مهر و امضای فروشنده</span></div>
        <div class="signature"><span>امضای خریدار / تحویل‌گیرنده</span></div>
      </section>

      <footer class="footer">
        <div><strong>${optional(store?.tradeName || "وندوم")}</strong> · ${storeAddress}</div>
        <div class="ltr">${escapeHtml(invoice.invoiceNumber)}</div>
      </footer>
    </section>
  </main>
</body>
</html>`;
}
