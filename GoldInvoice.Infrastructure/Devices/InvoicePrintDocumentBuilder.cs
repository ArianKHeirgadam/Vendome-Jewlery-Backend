using System.Globalization;
using System.Text;
using GoldInvoice.Domain.Invoicing;

namespace GoldInvoice.Infrastructure.Devices;

internal static class InvoicePrintDocumentBuilder
{
    public static string Build(
        Invoice invoice,
        IReadOnlyList<InvoiceItem> items,
        InvoiceAddressSnapshot? address,
        InvoiceStoreSnapshot? store,
        InvoicePrintJob job)
    {
        var body = new StringBuilder();
        body.AppendLine("<!doctype html>");
        body.AppendLine("<html lang=\"fa\" dir=\"rtl\">");
        body.AppendLine("<head>");
        body.AppendLine("  <meta charset=\"utf-8\" />");
        body.AppendLine($"  <title>فاکتور {Escape(invoice.InvoiceNumber)}</title>");
        body.AppendLine("  <style>");
        body.AppendLine("    @page { size: A4 portrait; margin: 10mm; }");
        body.AppendLine("    * { box-sizing: border-box; }");
        body.AppendLine("    html, body { margin: 0; padding: 0; color: #273244; background: #ffffff; }");
        body.AppendLine("    body { font-family: Tahoma, 'Segoe UI', sans-serif; font-size: 10px; line-height: 1.8; -webkit-print-color-adjust: exact; print-color-adjust: exact; }");
        body.AppendLine("    .sheet { min-height: 270mm; padding: 6mm; }");
        body.AppendLine("    .ltr { direction: ltr; unicode-bidi: embed; }");
        body.AppendLine("    .header { display: flex; align-items: flex-start; justify-content: space-between; padding-bottom: 5mm; border-bottom: 1px solid #dddddd; }");
        body.AppendLine("    .brand h1 { margin: 0; color: #172844; font-size: 20px; letter-spacing: .1em; }");
        body.AppendLine("    .brand span { display: block; color: #717783; font-size: 8px; }");
        body.AppendLine("    .meta { text-align: left; }");
        body.AppendLine("    .meta .number { color: #9a7740; font-size: 12px; font-weight: 700; }");
        body.AppendLine("    .meta .date { color: #717783; font-size: 8.5px; }");
        body.AppendLine("    .party { display: grid; grid-template-columns: 1fr 1fr; gap: 4mm; margin-top: 5mm; }");
        body.AppendLine("    .party .card { padding: 3.5mm; border: 1px solid #e5e2db; border-radius: 2mm; }");
        body.AppendLine("    .party h3 { margin: 0 0 2mm; color: #9a7740; font-size: 10px; }");
        body.AppendLine("    .party p { margin: .6mm 0; color: #273244; font-size: 8.5px; }");
        body.AppendLine("    table { width: 100%; margin-top: 5mm; border-collapse: collapse; table-layout: fixed; }");
        body.AppendLine("    th { padding: 2mm 1mm; color: #ffffff; background: #172844; font-size: 7.6px; text-align: center; }");
        body.AppendLine("    td { padding: 2.2mm 1mm; border-bottom: 1px solid #e8e9ec; color: #3b4657; font-size: 8px; text-align: center; }");
        body.AppendLine("    .product { text-align: right; }");
        body.AppendLine("    .money { text-align: left; white-space: nowrap; }");
        body.AppendLine("    .summary { display: grid; grid-template-columns: 1fr 60mm; gap: 6mm; margin-top: 6mm; }");
        body.AppendLine("    .notes { padding: 3mm; color: #626b78; background: #f9f7f2; border-right: 1mm solid #c7a971; font-size: 8px; }");
        body.AppendLine("    .totals { padding: 3mm; border: 1px solid #e5e2db; border-radius: 2mm; }");
        body.AppendLine("    .total { display: flex; justify-content: space-between; padding: 1.5mm 0; border-bottom: 1px dashed #e5e2db; font-size: 8.5px; }");
        body.AppendLine("    .total.grand { color: #ffffff; background: #172844; padding: 2.5mm; border-radius: 1.5mm; font-size: 10px; }");
        body.AppendLine("    .signatures { display: grid; grid-template-columns: 1fr 1fr; gap: 16mm; margin-top: 10mm; padding: 0 6mm; }");
        body.AppendLine("    .signature { min-height: 16mm; text-align: center; border-top: 1px solid #cfd3da; }");
        body.AppendLine("    .signature span { position: relative; top: 1.5mm; padding: 0 3mm; color: #858b95; background: #ffffff; font-size: 7.5px; }");
        body.AppendLine("    .footer { display: flex; justify-content: space-between; margin-top: 6mm; padding-top: 2mm; color: #8a9099; border-top: 1px solid #e5e2db; font-size: 7px; }");
        body.AppendLine("  </style>");
        body.AppendLine("</head>");
        body.AppendLine("<body>");
        body.AppendLine("  <div class=\"sheet\">");

        AppendHeader(body, invoice, store);
        AppendParties(body, invoice, address, store);
        AppendItems(body, items);
        AppendSummary(body, invoice, store);
        AppendSignatures(body);
        AppendFooter(body, invoice, store);

        body.AppendLine("  </div>");
        body.AppendLine("</body>");
        body.AppendLine("</html>");
        return body.ToString();
    }

    private static void AppendHeader(StringBuilder body, Invoice invoice, InvoiceStoreSnapshot? store)
    {
        body.AppendLine("    <header class=\"header\">");
        body.AppendLine("      <div class=\"brand\">");
        body.AppendLine($"        <h1>VENDOME</h1>");
        body.AppendLine($"        <span>{Escape(store?.TradeName ?? "گالری وندوم")}</span>");
        body.AppendLine($"        <span>{Escape(store?.LegalName ?? string.Empty)}</span>");
        body.AppendLine("      </div>");
        body.AppendLine("      <div class=\"meta\">");
        body.AppendLine("        <div>فاکتور فروش</div>");
        body.AppendLine($"        <div class=\"number ltr\">{Escape(invoice.InvoiceNumber)}</div>");
        body.AppendLine($"        <div class=\"date\">{Escape(invoice.IssuedAt.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture))}</div>");
        body.AppendLine("      </div>");
        body.AppendLine("    </header>");
    }

    private static void AppendParties(
        StringBuilder body,
        Invoice invoice,
        InvoiceAddressSnapshot? address,
        InvoiceStoreSnapshot? store)
    {
        var recipient = address?.RecipientName ?? invoice.CustomerNameSnapshot ?? "—";
        body.AppendLine("    <section class=\"party\">");
        body.AppendLine("      <div class=\"card\"><h3>اطلاعات خریدار</h3>");
        body.AppendLine($"        <p>نام: {Escape(invoice.CustomerNameSnapshot ?? "—")}</p>");
        body.AppendLine($"        <p>تحویل‌گیرنده: {Escape(recipient)}</p>");
        body.AppendLine($"        <p>تلفن: <span class=\"ltr\">{Escape(address?.PhoneNumber ?? "—")}</span></p>");
        body.AppendLine($"        <p>شناسه ملی: <span class=\"ltr\">{Escape(invoice.CustomerNationalIdSnapshot ?? "—")}</span></p>");
        body.AppendLine($"        <p>نشانی: {Escape(address is null ? "—" : $"{address.Province}، {address.City}، {address.AddressLine}")}</p>");
        body.AppendLine("      </div>");
        body.AppendLine("      <div class=\"card\"><h3>اطلاعات فروشنده</h3>");
        body.AppendLine($"        <p>نام تجاری: {Escape(store?.TradeName ?? "—")}</p>");
        body.AppendLine($"        <p>نام حقوقی: {Escape(store?.LegalName ?? "—")}</p>");
        body.AppendLine($"        <p>شناسه ملی: <span class=\"ltr\">{Escape(store?.NationalId ?? "—")}</span></p>");
        body.AppendLine($"        <p>کد اقتصادی: <span class=\"ltr\">{Escape(store?.EconomicCode ?? "—")}</span></p>");
        body.AppendLine($"        <p>تلفن: <span class=\"ltr\">{Escape(store?.PhoneNumber ?? "—")}</span></p>");
        body.AppendLine("      </div>");
        body.AppendLine("    </section>");
    }

    private static void AppendItems(StringBuilder body, IReadOnlyList<InvoiceItem> items)
    {
        body.AppendLine("    <table aria-label=\"اقلام فاکتور\">");
        body.AppendLine("      <thead><tr><th>ردیف</th><th>شرح کالا / کد</th><th>عیار</th><th>وزن کل</th><th>وزن خالص</th><th>تعداد</th><th>قیمت واحد</th><th>مبلغ</th></tr></thead>");
        body.AppendLine("      <tbody>");
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            body.AppendLine("        <tr>");
            body.AppendLine($"          <td>{i + 1}</td>");
            body.AppendLine($"          <td class=\"product\">{Escape(item.ProductName)}<br /><small class=\"ltr\">{Escape(item.Sku)}</small></td>");
            body.AppendLine($"          <td>{item.Karat?.ToString(CultureInfo.InvariantCulture) ?? "—"}</td>");
            body.AppendLine($"          <td>{item.WeightGrams.ToString("0.###", CultureInfo.InvariantCulture)}</td>");
            body.AppendLine($"          <td>{item.NetGoldWeightGrams?.ToString("0.###", CultureInfo.InvariantCulture) ?? "—"}</td>");
            body.AppendLine($"          <td>{item.Quantity}</td>");
            body.AppendLine($"          <td class=\"money\">{FormatMoney(item.UnitPriceRials)}</td>");
            body.AppendLine($"          <td class=\"money\">{FormatMoney(item.LineTotalRials)}</td>");
            body.AppendLine("        </tr>");
        }

        body.AppendLine("      </tbody>");
        body.AppendLine("    </table>");
    }

    private static void AppendSummary(StringBuilder body, Invoice invoice, InvoiceStoreSnapshot? store)
    {
        body.AppendLine("    <section class=\"summary\">");
        body.AppendLine("      <div class=\"notes\"><strong>توضیحات سند</strong><p>این فاکتور به‌صورت سیستمی توسط سامانه مدیریت وندوم صادر شده است.</p></div>");
        body.AppendLine("      <div class=\"totals\">");
        body.AppendLine($"        <div class=\"total\"><span>جمع اقلام</span><span>{FormatMoney(invoice.SubtotalRials)}</span></div>");
        body.AppendLine($"        <div class=\"total\"><span>تخفیف</span><span>{FormatMoney(invoice.DiscountRials)}</span></div>");
        body.AppendLine($"        <div class=\"total\"><span>هزینه ارسال</span><span>{FormatMoney(invoice.ShippingRials)}</span></div>");
        body.AppendLine($"        <div class=\"total grand\"><span>مبلغ نهایی</span><span>{FormatMoney(invoice.GrandTotalRials)}</span></div>");
        body.AppendLine("      </div>");
        body.AppendLine("    </section>");
    }

    private static void AppendSignatures(StringBuilder body)
    {
        body.AppendLine("    <section class=\"signatures\">");
        body.AppendLine("      <div class=\"signature\"><span>مهر و امضای فروشنده</span></div>");
        body.AppendLine("      <div class=\"signature\"><span>امضای خریدار / تحویل‌گیرنده</span></div>");
        body.AppendLine("    </section>");
    }

    private static void AppendFooter(StringBuilder body, Invoice invoice, InvoiceStoreSnapshot? store)
    {
        body.AppendLine("    <footer class=\"footer\">");
        body.AppendLine($"      <span>{Escape(store?.TradeName ?? "وندوم")}</span>");
        body.AppendLine($"      <span class=\"ltr\">{Escape(invoice.InvoiceNumber)}</span>");
        body.AppendLine("    </footer>");
    }

    private static string FormatMoney(long rials) =>
        rials.ToString("N0", CultureInfo.InvariantCulture) + " تومان";

    private static string Escape(string? value) => (value ?? string.Empty)
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("'", "&#039;");
}