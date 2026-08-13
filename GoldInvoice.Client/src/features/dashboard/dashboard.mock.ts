import type { DashboardSnapshot } from "./dashboard.types";

/**
 * Compatibility fixture for installations upgraded in-place from the original
 * dashboard package. Production builds use buildDashboardSnapshot.ts and never
 * render this object; keeping this file prevents an obsolete overlay copy from
 * breaking TypeScript compilation.
 */
export const dashboardMock: DashboardSnapshot = {
  profile: {
    displayName: "کاربر وندوم",
    role: "کاربر",
    initials: "و",
  },
  quickOperations: [
    {
      id: "new-invoice",
      title: "فاکتور جدید",
      description: "ساخت سفارش، تسویه و صدور فاکتور رسمی.",
      meta: "شروع عملیات",
      path: "/orders/new",
    },
    {
      id: "new-customer",
      title: "مشتری جدید",
      description: "ثبت مشتری و اطلاعات تماس او.",
      meta: "ثبت مشتری",
      path: "/customers?new=1",
    },
    {
      id: "new-product",
      title: "کالای جدید",
      description: "ثبت محصول، مدل طلا و مشخصات قیمت‌گذاری.",
      meta: "ثبت محصول",
      path: "/products?new=1",
    },
    {
      id: "inventory-receipt",
      title: "ورود به انبار",
      description: "ثبت رسید موجودی برای مدل‌های ثبت‌شده.",
      meta: "ثبت موجودی",
      path: "/inventory?receipt=1",
    },
    {
      id: "settlement",
      title: "تسویه سفارش",
      description: "ثبت پرداخت و صدور خودکار فاکتور.",
      meta: "تسویه",
      path: "/orders?settle=1",
    },
    {
      id: "daily-report",
      title: "گزارش روز",
      description: "مرور فروش، سود و وضعیت عملیات امروز.",
      meta: "مشاهده گزارش",
      path: "/reports",
    },
  ],
  metrics: [],
  market: {
    updatedAt: "نرخی ثبت نشده",
    goldPrices: [],
    trading: [],
    currencies: [],
    isOpen: false,
    hours: "باز ۰۹:۰۰ · بسته ۱۷:۰۰",
    dailyTrend: [],
    weeklyTrend: [],
  },
  revenue: [],
  categories: [],
  transactions: [],
  upcomingPayments: [],
};
