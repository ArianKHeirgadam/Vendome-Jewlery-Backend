import { ArrowUpLeft, ArrowUpRight, Plus, TrendingDown, TrendingUp } from "lucide-react";
import { CategoryChart, RevenueChart } from "./DashboardCharts";
import type { DashboardSnapshot } from "./dashboard.types";

interface DashboardPageProps {
  snapshot: DashboardSnapshot;
  onAction: () => void;
}

function formatPersianDate(date: Date) {
  const formatter = new Intl.DateTimeFormat("fa-IR-u-ca-persian", {
    weekday: "long",
    day: "numeric",
    month: "long",
    year: "numeric",
  });
  const parts = formatter.formatToParts(date);
  const value = (type: Intl.DateTimeFormatPartTypes) =>
    parts.find((part) => part.type === type)?.value ?? "";

  return `${value("weekday")}، ${value("day")} ${value("month")} ${value("year")}`;
}

export function DashboardPage({ snapshot, onAction }: DashboardPageProps) {
  return (
    <main className="dashboard-main" dir="rtl">
      <div className="dashboard-heading fade-up">
        <div>
          <p className="eyebrow gold-text">{formatPersianDate(new Date())}</p>
          <h1>سلام، علی</h1>
          <p className="dashboard-subtitle">
            نمایی آرام از مِزون؛ معاملات امروز، روند این ماه و آنچه منتظر امضای شماست.
          </p>
        </div>
        <button className="primary-button" type="button" onClick={onAction}>
          <Plus size={18} strokeWidth={1.7} aria-hidden="true" />
          فاکتور جدید
        </button>
      </div>

      <section className="dashboard-section" aria-labelledby="quick-operations-title">
        <h2 className="section-title" id="quick-operations-title">
          عملیات سریع
        </h2>
        <div className="quick-grid">
          {snapshot.quickOperations.map((operation, index) => (
            <button
              className="lux-card quick-card fade-up"
              style={{ animationDelay: `${index * 45}ms` }}
              type="button"
              onClick={onAction}
              key={operation.id}
            >
              <span className="card-gold-rule" />
              <ArrowUpLeft className="quick-arrow" size={16} strokeWidth={1.4} />
              <h3>{operation.title}</h3>
              <p>{operation.description}</p>
              <small>{operation.meta}</small>
            </button>
          ))}
        </div>
      </section>

      <section className="dashboard-section" aria-labelledby="performance-title">
        <h2 className="section-title" id="performance-title">
          عملکرد
        </h2>
        <div className="metric-grid">
          {snapshot.metrics.map((metric, index) => (
            <article
              className="lux-card metric-card fade-up"
              style={{ animationDelay: `${index * 35}ms` }}
              key={metric.id}
            >
              <p>{metric.label}</p>
              <strong className="metric-value numeric" dir="ltr">
                {metric.value}
              </strong>
              <div className="metric-footer">
                <span>{metric.hint}</span>
                <span className={`trend trend--${metric.direction}`}>
                  {metric.direction === "up" ? (
                    <TrendingUp size={12} />
                  ) : (
                    <TrendingDown size={12} />
                  )}
                  {metric.trend}
                </span>
              </div>
            </article>
          ))}
        </div>
      </section>

      <section className="insights-grid" aria-label="تحلیل عملکرد">
        <article className="lux-card insight-card revenue-card">
          <header className="card-header">
            <div>
              <h2>درآمد و سود</h2>
              <p>هفت ماه گذشته، به هزار</p>
            </div>
          </header>
          <RevenueChart values={snapshot.revenue} />
        </article>

        <article className="lux-card insight-card category-card">
          <header className="card-header">
            <div>
              <h2>ترکیب دسته‌بندی</h2>
              <p>سهم از درآمد ماهانه</p>
            </div>
          </header>
          <CategoryChart values={snapshot.categories} />
        </article>

        <article className="lux-card insight-card transactions-card">
          <header className="card-header">
            <div>
              <h2>تراکنش‌های اخیر</h2>
            </div>
          </header>
          <div className="detail-list">
            {snapshot.transactions.map((transaction) => (
              <div className="detail-row" key={transaction.id}>
                <div>
                  <strong>{transaction.customer}</strong>
                  <p>{transaction.detail}</p>
                </div>
                <span
                  className={`numeric amount ${transaction.positive ? "amount--positive" : "amount--negative"}`}
                  dir="ltr"
                >
                  {transaction.amount}
                </span>
              </div>
            ))}
          </div>
        </article>

        <article className="lux-card insight-card payments-card">
          <header className="card-header">
            <div>
              <h2>پرداخت‌های پیش‌رو</h2>
            </div>
          </header>
          <div className="detail-list">
            {snapshot.upcomingPayments.map((payment) => (
              <div className="detail-row payment-row" key={payment.id}>
                <div>
                  <strong>{payment.title}</strong>
                  <p>{payment.dueDate}</p>
                </div>
                <span className="numeric amount" dir="ltr">
                  {payment.amount}
                </span>
              </div>
            ))}
          </div>
          <button className="text-button" type="button" onClick={onAction}>
            مشاهده همه
            <ArrowUpRight size={14} />
          </button>
        </article>
      </section>
    </main>
  );
}
