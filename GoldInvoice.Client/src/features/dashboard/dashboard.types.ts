export type TrendDirection = "up" | "down" | "neutral";

export interface ProfileSummary {
  displayName: string;
  role: string;
  initials: string;
}

export interface QuickOperation {
  id: string;
  title: string;
  description: string;
  meta: string;
  path: string;
}

export interface PerformanceMetric {
  id: string;
  label: string;
  value: string;
  hint: string;
  trend: string;
  direction: TrendDirection;
}

export interface MarketQuote {
  label: string;
  value: string;
  unit?: string;
  trend?: string;
  direction?: TrendDirection;
}

export interface RevenuePoint {
  month: string;
  revenue: number;
  profit: number;
}

export interface CategoryShare {
  categoryId?: string;
  label: string;
  value: number;
  revenueRials: number;
  productCount: number;
  invoiceCount: number;
}

export interface TransactionItem {
  id: string;
  customer: string;
  detail: string;
  amount: string;
  positive: boolean;
}

export interface UpcomingPayment {
  id: string;
  title: string;
  dueDate: string;
  amount: string;
}

export interface DashboardSnapshot {
  profile: ProfileSummary;
  quickOperations: QuickOperation[];
  metrics: PerformanceMetric[];
  market: {
    updatedAt: string;
    goldPrices: MarketQuote[];
    trading: MarketQuote[];
    currencies: MarketQuote[];
    isOpen: boolean;
    hours: string;
    dailyTrend: number[];
    weeklyTrend: number[];
  };
  revenue: RevenuePoint[];
  categories: CategoryShare[];
  transactions: TransactionItem[];
  upcomingPayments: UpcomingPayment[];
}
