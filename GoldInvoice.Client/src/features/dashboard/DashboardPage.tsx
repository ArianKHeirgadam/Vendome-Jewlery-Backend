import { ArrowUpRight, ArrowUpLeft, Minus, Plus, TrendingDown, TrendingUp } from "lucide-react";
import { CategoryChart, RevenueChart } from "./DashboardCharts";
import type { DashboardSnapshot } from "./dashboard.types";

interface DashboardPageProps {
  snapshot: DashboardSnapshot;
  onNavigate: (path: string) => void;
}

function formatEnglishDate(date: Date) {
  return new Intl.DateTimeFormat("en-US", {
    weekday: "long",
    day: "numeric",
    month: "long",
    year: "numeric",
  }).format(date);
}

export function DashboardPage({ snapshot, onNavigate }: DashboardPageProps) {
  const firstName = snapshot.profile.displayName.trim().split(/\s+/)[0] || "there";

  return (
    <main className="dashboard-main" dir="ltr">
      <div className="dashboard-heading fade-up">
        <div>
          <p className="eyebrow gold-text">{formatEnglishDate(new Date())}</p>
          <h1>Hello, {firstName}</h1>
          <p className="dashboard-subtitle">
            A calm view of the maison: today&apos;s transactions, this month&apos;s performance and everything awaiting your attention.
          </p>
        </div>
        <button className="primary-button" type="button" onClick={() => onNavigate("/orders/new")}>
          <Plus size={18} strokeWidth={1.7} aria-hidden="true" />
          New invoice
        </button>
      </div>

      <section className="dashboard-section" aria-labelledby="quick-operations-title">
        <h2 className="section-title" id="quick-operations-title">Quick actions</h2>
        <div className="quick-grid">
          {snapshot.quickOperations.map((operation, index) => (
            <button
              className="lux-card quick-card fade-up"
              style={{ animationDelay: `${index * 45}ms` }}
              type="button"
              onClick={() => onNavigate(operation.path)}
              key={operation.id}
            >
              <span className="card-gold-rule" />
              <ArrowUpRight className="quick-arrow" size={16} strokeWidth={1.4} />
              <h3>{operation.title}</h3>
              <p>{operation.description}</p>
              <small>{operation.meta}</small>
            </button>
          ))}
        </div>
      </section>

      <section className="dashboard-section" aria-labelledby="performance-title">
        <h2 className="section-title" id="performance-title">Performance</h2>
        <div className="metric-grid">
          {snapshot.metrics.map((metric, index) => (
            <article
              className="lux-card metric-card fade-up"
              style={{ animationDelay: `${index * 35}ms` }}
              key={metric.id}
            >
              <p>{metric.label}</p>
              <strong className="metric-value numeric" dir="ltr">{metric.value}</strong>
              <div className="metric-footer">
                <span>{metric.hint}</span>
                <span className={`trend trend--${metric.direction}`}>
                  {metric.direction === "up" ? (
                    <TrendingUp size={12} />
                  ) : metric.direction === "down" ? (
                    <TrendingDown size={12} />
                  ) : (
                    <Minus size={12} />
                  )}
                  {metric.trend}
                </span>
              </div>
            </article>
          ))}
        </div>
      </section>

      <section className="insights-grid" aria-label="Performance insights">
        <article className="lux-card insight-card revenue-card">
          <header className="card-header">
            <div>
              <h2>Sales and profit</h2>
              <p>Actual sales and profit after purchase cost and discounts, in million toman</p>
            </div>
          </header>
          <RevenueChart values={snapshot.revenue} />
        </article>

        <article className="lux-card insight-card category-card">
          <header className="card-header">
            <div>
              <h2>Category mix</h2>
              <p>Share of monthly revenue</p>
            </div>
          </header>
          <CategoryChart values={snapshot.categories} onNavigate={onNavigate} />
        </article>

        <article className="lux-card insight-card transactions-card">
          <header className="card-header">
            <div><h2>Recent transactions</h2></div>
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
            <div><h2>Upcoming payments</h2></div>
          </header>
          <div className="detail-list">
            {snapshot.upcomingPayments.map((payment) => (
              <div className="detail-row payment-row" key={payment.id}>
                <div>
                  <strong>{payment.title}</strong>
                  <p>{payment.dueDate}</p>
                </div>
                <span className="numeric amount" dir="ltr">{payment.amount}</span>
              </div>
            ))}
          </div>
          <button className="text-button" type="button" onClick={() => onNavigate("/accounting")}>
            View all
            <ArrowUpRight size={14} />
          </button>
        </article>
      </section>
    </main>
  );
}
