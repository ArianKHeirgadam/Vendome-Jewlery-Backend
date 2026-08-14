import type {
  MarketPrice,
  OperationalSnapshot,
} from "../operations/operations.types";
import type {
  DashboardSnapshot,
  PerformanceMetric,
  ProfileSummary,
} from "./dashboard.types";
import { activeNumberLocale, formatTomansFromRials } from "../../lib/money";

function formatMoney(value: number): string {
  return formatTomansFromRials(value);
}

function actualProfit(invoice: OperationalSnapshot["invoices"][number]): number {
  const knownItems = invoice.items.filter((item) => item.grossProfitRials != null);
  if (!knownItems.length) return 0;
  const knownRevenue = knownItems.reduce((sum, item) => sum + item.lineTotalRials, 0);
  const allocatedDiscount = invoice.subtotalRials > 0
    ? invoice.discountRials * (knownRevenue / invoice.subtotalRials)
    : 0;
  return knownItems.reduce((sum, item) => sum + (item.grossProfitRials ?? 0), 0) - allocatedDiscount;
}

function sameDay(value: string | null | undefined, date: Date): boolean {
  if (!value) return false;
  const candidate = new Date(value);
  return candidate.getFullYear() === date.getFullYear() &&
    candidate.getMonth() === date.getMonth() &&
    candidate.getDate() === date.getDate();
}

function sameMonth(value: string | null | undefined, date: Date): boolean {
  if (!value) return false;
  const candidate = new Date(value);
  return candidate.getFullYear() === date.getFullYear() &&
    candidate.getMonth() === date.getMonth();
}

function trend(current: number, previous: number): Pick<PerformanceMetric, "trend" | "direction"> {
  if (previous <= 0) {
    return { trend: current > 0 ? "جدید" : "بدون تغییر", direction: "neutral" };
  }
  const change = ((current - previous) / previous) * 100;
  const percentFormatter = new Intl.NumberFormat(activeNumberLocale(), { maximumFractionDigits: 1 });
  return {
    trend: `${percentFormatter.format(Math.abs(change))}٪`,
    direction: change > 0 ? "up" : change < 0 ? "down" : "neutral",
  };
}

function startOfDay(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate());
}

export function buildDashboardSnapshot(
  data: OperationalSnapshot,
  profile: ProfileSummary,
): DashboardSnapshot {
  const now = new Date();
  const locale = activeNumberLocale();
  const numberFormatter = new Intl.NumberFormat(locale, { maximumFractionDigits: 0 });
  const yesterday = new Date(startOfDay(now).getTime() - 86_400_000);
  const activeInvoices = data.invoices.filter((invoice) => invoice.status !== "Voided");
  const issuedToday = activeInvoices.filter((invoice) => sameDay(invoice.issuedAt, now));
  const issuedYesterday = activeInvoices.filter((invoice) => sameDay(invoice.issuedAt, yesterday));
  const issuedThisMonth = activeInvoices.filter((invoice) => sameMonth(invoice.issuedAt, now));
  const previousMonth = new Date(now.getFullYear(), now.getMonth() - 1, 1);
  const issuedPreviousMonth = activeInvoices.filter((invoice) => sameMonth(invoice.issuedAt, previousMonth));
  const salesToday = issuedToday.reduce((sum, invoice) => sum + invoice.grandTotalRials, 0);
  const salesYesterday = issuedYesterday.reduce((sum, invoice) => sum + invoice.grandTotalRials, 0);
  const profitToday = issuedToday.reduce((sum, invoice) => sum + actualProfit(invoice), 0);
  const profitYesterday = issuedYesterday.reduce((sum, invoice) => sum + actualProfit(invoice), 0);
  const monthlyRevenue = issuedThisMonth.reduce((sum, invoice) => sum + invoice.grandTotalRials, 0);
  const previousRevenue = issuedPreviousMonth.reduce((sum, invoice) => sum + invoice.grandTotalRials, 0);
  const pendingOrders = data.orders.filter((order) =>
    !["Paid", "Completed", "Cancelled", "Refunded"].includes(order.status),
  );
  const inventoryAvailable = data.inventoryItems.reduce((sum, item) => sum + item.quantityAvailable, 0);
  const receivables = pendingOrders.reduce((sum, order) => sum + order.grandTotalRials, 0);
  const latestPurchaseFor = (variantId: string) => data.supplierPurchases
    .filter((purchase) => purchase.productVariantId === variantId)
    .sort((left, right) => new Date(right.purchasedAt).getTime() - new Date(left.purchasedAt).getTime())[0];
  const inventoryValue = data.inventoryItems.reduce((sum, item) =>
    sum + (latestPurchaseFor(item.productVariantId)?.sellingUnitPriceRials ?? item.averageUnitCostRials) * item.quantityAvailable,
  0);
  const customerDeposits = data.orders.reduce((sum, order) => {
    const verified = data.payments
      .filter((payment) => payment.orderId === order.id && payment.status === "Verified")
      .reduce((paymentSum, payment) => paymentSum + payment.amountRials, 0);
    return sum + Math.max(0, verified - order.grandTotalRials);
  }, 0);

  const metrics: PerformanceMetric[] = [
    {
      id: "today-sales",
      label: "فروش امروز",
      value: formatMoney(salesToday),
      hint: `${numberFormatter.format(issuedToday.length)} فاکتور`,
      ...trend(salesToday, salesYesterday),
    },
    {
      id: "today-profit",
      label: "سود امروز",
      value: formatMoney(profitToday),
      hint: "فروش منهای قیمت خرید و تخفیف",
      ...trend(profitToday, profitYesterday),
    },
    {
      id: "monthly-revenue",
      label: "درآمد ماهانه",
      value: formatMoney(monthlyRevenue),
      hint: `${numberFormatter.format(issuedThisMonth.length)} فاکتور`,
      ...trend(monthlyRevenue, previousRevenue),
    },
    {
      id: "pending-orders",
      label: "سفارش‌های در انتظار",
      value: numberFormatter.format(pendingOrders.length),
      hint: "نیازمند پرداخت یا تکمیل",
      trend: "زنده",
      direction: "neutral",
    },
    {
      id: "issued-invoices",
      label: "فاکتورهای صادرشده",
      value: numberFormatter.format(data.invoices.length),
      hint: `${numberFormatter.format(issuedThisMonth.length)} در ماه جاری`,
      trend: "واقعی",
      direction: "neutral",
    },
    {
      id: "inventory-value",
      label: "ارزش انبار",
      value: formatMoney(inventoryValue),
      hint: `${numberFormatter.format(inventoryAvailable)} قطعه قابل فروش`,
      trend: "زنده",
      direction: "neutral",
    },
    {
      id: "receivables",
      label: "مطالبات وصول‌نشده",
      value: formatMoney(receivables),
      hint: `${numberFormatter.format(pendingOrders.length)} سفارش`,
      trend: "زنده",
      direction: "neutral",
    },
    {
      id: "customer-deposits",
      label: "سپرده مشتریان",
      value: formatMoney(customerDeposits),
      hint: "مازاد پرداخت تأییدشده",
      trend: "واقعی",
      direction: "neutral",
    },
  ];

  const revenue = Array.from({ length: 7 }, (_, index) => {
    const date = new Date(now.getFullYear(), now.getMonth() - 6 + index, 1);
    const monthlyInvoices = activeInvoices.filter((invoice) => sameMonth(invoice.issuedAt, date));
    return {
      month: new Intl.DateTimeFormat(locale === "fa-IR" ? "fa-IR-u-ca-persian" : "en-US", { month: "short" }).format(date),
      revenue: monthlyInvoices.reduce((sum, invoice) => sum + invoice.grandTotalRials, 0) / 10_000_000,
      profit: monthlyInvoices.reduce((sum, invoice) => sum + actualProfit(invoice), 0) / 10_000_000,
    };
  });

  const orderItems = new Map(data.orders.flatMap((order) => order.items).map((item) => [item.id, item]));
  const variants = new Map(data.products.flatMap((product) =>
    product.variants.map((variant) => [variant.id, product] as const),
  ));
  const categoryRevenue = new Map<string, { revenue: number; products: Set<string>; invoices: Set<string> }>();
  for (const invoice of issuedThisMonth) {
    const netRatio = invoice.subtotalRials > 0
      ? Math.max(0, invoice.subtotalRials - invoice.discountRials) / invoice.subtotalRials
      : 0;
    for (const item of invoice.items) {
      const orderItem = item.orderItemId ? orderItems.get(item.orderItemId) : undefined;
      const product = orderItem ? variants.get(orderItem.productVariantId) : undefined;
      const categoryId = product?.productCategoryId || "uncategorized";
      const current = categoryRevenue.get(categoryId) || {
        revenue: 0,
        products: new Set<string>(),
        invoices: new Set<string>(),
      };
      current.revenue += item.lineTotalRials * netRatio;
      if (product) current.products.add(product.id);
      current.invoices.add(invoice.id);
      categoryRevenue.set(categoryId, current);
    }
  }
  const totalCategorized = Math.max(
    Array.from(categoryRevenue.values()).reduce((sum, category) => sum + category.revenue, 0),
    1,
  );
  const categories = Array.from(categoryRevenue.entries())
    .map(([categoryId, values]) => ({
      categoryId: categoryId === "uncategorized" ? undefined : categoryId,
      label: data.categories.find((category) => category.id === categoryId)?.name || "بدون دسته‌بندی",
      value: Math.round((values.revenue / totalCategorized) * 100),
      revenueRials: Math.round(values.revenue),
      productCount: values.products.size,
      invoiceCount: values.invoices.size,
    }))
    .sort((left, right) => right.revenueRials - left.revenueRials)
    .slice(0, 4)
  if (!categories.length) categories.push({ categoryId: undefined, label: "بدون فروش ماهانه", value: 0, revenueRials: 0, productCount: 0, invoiceCount: 0 });

  const gold18 = data.marketPrices.find((price) => price.priceType === "Gold18K");
  const gold24 = data.marketPrices.find((price) => price.priceType === "Gold24K");
  const currency = data.marketPrices.find((price) => price.priceType === "Currency");
  const latestMarketDate = data.marketPrices
    .map((price) => new Date(price.capturedAt))
    .sort((left, right) => right.getTime() - left.getTime())[0];
  const quote = (price: MarketPrice | undefined, side: "buy" | "sell") =>
    price ? formatMoney(side === "buy" ? price.buyPriceRials : price.sellPriceRials) : "ثبت نشده";

  return {
    profile,
    quickOperations: [
      { id: "daily-operations", title: "عملیات روزانه", description: "باز کردن صندوق، مرور فعالیت روز و بستن دفاتر.", meta: `${numberFormatter.format(issuedToday.length)} فاکتور امروز`, path: "/reports/sales" },
      { id: "trust-funds", title: "وجوه امانی", description: "سپرده‌های امانی مشتریان، تخصیص‌ها و آزادسازی‌ها.", meta: formatMoney(customerDeposits), path: "/accounting/trust-funds" },
      { id: "installments", title: "اقساط", description: "فروش اقساطی، جدول پرداخت و وضعیت وصول.", meta: `${numberFormatter.format(pendingOrders.length)} در انتظار`, path: "/accounting/installments" },
      { id: "invoice-settlement", title: "تسویه فاکتور", description: "دریافت وجه، تسویه مانده‌ها و مرور تاریخچه پرداخت.", meta: formatMoney(receivables), path: "/orders?settle=1" },
      { id: "bank-interest", title: "سود بانکی", description: "سود دریافتی سپرده‌ها و سود پرداختی تسهیلات.", meta: "اطلاعات مالی واقعی", path: "/accounting/bank-interest" },
      { id: "financial-workspace", title: locale === "en-US" ? "Assets & Expenses" : "دارایی‌ها و هزینه‌ها", description: locale === "en-US" ? "Inventory value and financial ledgers for Houman and Ali." : "موجودی انبار و دفتر مالی هومن و علی.", meta: formatMoney(inventoryValue), path: "/financial-workspace" },
    ],
    metrics,
    market: {
      updatedAt: latestMarketDate
        ? new Intl.DateTimeFormat(locale, { hour: "2-digit", minute: "2-digit" }).format(latestMarketDate)
        : "نرخی ثبت نشده",
      goldPrices: [
        { label: "طلای ۱۸ عیار", value: quote(gold18, "sell") },
        { label: "طلای ۲۴ عیار (محاسباتی)", value: quote(gold24, "sell") },
      ],
      trading: [
        { label: "خرید طلای ۱۸", value: quote(gold18, "buy") },
        { label: "فروش طلای ۱۸", value: quote(gold18, "sell") },
      ],
      currencies: [
        { label: "دلار تهران خرید", value: quote(currency, "buy") },
        { label: "دلار تهران فروش", value: quote(currency, "sell") },
      ],
      isOpen: now.getHours() >= 9 && now.getHours() < 17,
      hours: "باز ۰۹:۰۰ · بسته ۱۷:۰۰",
      dailyTrend: [0, 0, 0, 0, 0, 0, 0],
      weeklyTrend: [0, 0, 0, 0, 0, 0, 0],
    },
    revenue,
    categories,
    transactions: data.invoices.slice(0, 5).map((invoice) => ({
      id: invoice.id,
      customer: invoice.customerNameSnapshot || "مشتری ثبت‌شده",
      detail: `${new Intl.DateTimeFormat(locale, { hour: "2-digit", minute: "2-digit" }).format(new Date(invoice.issuedAt))} · فاکتور ${invoice.invoiceNumber}`,
      amount: `+${formatMoney(invoice.grandTotalRials)}`,
      positive: invoice.status !== "Voided",
    })),
    upcomingPayments: pendingOrders.slice(0, 4).map((order) => ({
      id: order.id,
      title: `سفارش ${order.orderNumber} · ${order.customerNameSnapshot || "مشتری"}`,
      dueDate: "در انتظار تسویه",
      amount: formatMoney(order.grandTotalRials),
    })),
  };
}
