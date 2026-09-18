import { ArrowUpLeft, Info, Search, UserRoundPlus } from "lucide-react";
import type { DashboardSnapshot } from "./dashboard.types";

interface DashboardPageProps {
  snapshot: DashboardSnapshot;
  onNavigate: (path: string) => void;
}

function statusLabel(status: string): string {
  if (status === "Paid") return "پرداخت‌شده";
  if (status === "Overdue") return "معوق";
  if (status === "Voided") return "باطل";
  return "در انتظار";
}

export function DashboardPage({ snapshot, onNavigate }: DashboardPageProps) {
  const quotes = snapshot.market.goldPrices.slice(0, 2);
  const recent = snapshot.transactions.slice(0, 3);
  const quickOperations = snapshot.quickOperations.slice(0, 6);
  const metrics = snapshot.metrics.slice(0, 4);

  return (
    <main className="dashboard-lovable" dir="rtl">
      <header className="dashboard-lovable__header">
        <div>
          <p className="dashboard-eyebrow">مدیریت وندوم</p>
          <h1>سلام، {snapshot.profile.displayName}</h1>
          <p>نمایی آرام از میز مدیریت؛ معاملات امروز، روند این ماه و آنچه منتظر امضای شماست.</p>
        </div>
        <div className="dashboard-period" aria-label="بازه داشبورد">
          <button className="is-active" type="button">امروز</button>
          <button type="button">این هفته</button>
        </div>
      </header>

      <section className="dashboard-index-bar" aria-label="نرخ‌های زنده بازار">
        <strong>نرخ طلای زنده</strong>
        {quotes.map((quote) => (
          <span key={quote.label}>{quote.label}: <b>{quote.value || "ثبت نشده"}</b></span>
        ))}
        <span className="dashboard-index-info">
          <Info size={12} /> به‌روزرسانی {snapshot.market.updatedAt || "ثبت نشده"}
        </span>
      </section>

      <div className="dashboard-actions">
        <button className="dashboard-new-invoice" type="button" onClick={() => onNavigate("/orders/new")}>
          <span>+</span> فاکتور جدید
        </button>
      </div>

      <section className="dashboard-quick-section">
        <header><h2>عملیات سریع</h2></header>
        <div className="dashboard-quick-grid">
          {quickOperations.map((operation) => (
            <button className="dashboard-quick-card" type="button" key={operation.id} onClick={() => onNavigate(operation.path)}>
              <i />
              <ArrowUpLeft size={18} />
              <h3>{operation.title}</h3>
              <p>{operation.description}</p>
              <small>{operation.meta}</small>
            </button>
          ))}
        </div>
      </section>

      <section className="dashboard-performance">
        <header><h2>عملکرد</h2></header>
        <div className="dashboard-metrics-grid">
          {metrics.map((metric) => (
            <article className="dashboard-metric-card" key={metric.id}>
              <span>{metric.label}</span>
              <strong>{metric.value}</strong>
              <small>{metric.hint}</small>
              <em className={metric.direction}>{metric.direction === "up" ? "↗" : metric.direction === "down" ? "↘" : "—"} {metric.trend}</em>
            </article>
          ))}
        </div>
      </section>

      <section className="dashboard-transaction-row">
        <article className="dashboard-session-card">
          <div>
            <h2>شروع تراکنش جدید</h2>
            <p>فرآیند ثبت را با اسکن بارکد یا جست‌وجوی نام مشتری آغاز کنید.</p>
          </div>
          <button type="button" onClick={() => onNavigate("/orders/new")}>
            <b>کلیک ۱</b> شروع فرم فاکتور
          </button>
        </article>

        <article className="dashboard-customer-card">
          <h2>جست‌وجوی خودکار مشتری</h2>
          <p>دسترسی سریع به مشخصات، مانده‌های پرداخت‌نشده و اطلاعات مشتری.</p>
          <label className="dashboard-search">
            <Search size={16} />
            <input
              type="search"
              placeholder="نام یا شماره موبایل..."
              list="dashboard-customers"
              aria-label="جست‌وجوی مشتری"
            />
            <datalist id="dashboard-customers">
              {snapshot.customerSuggestions.map((name) => <option value={name} key={name} />)}
            </datalist>
          </label>
          <div className="dashboard-autofill-note">
            <strong>سیستم تکمیل خودکار</strong>
            <span>با جست‌وجوی نام، اطلاعات لازم برای فاکتور آماده می‌شود.</span>
          </div>
        </article>
      </section>

      <section className="dashboard-recent-card">
        <header><h2>آخرین تراکنش‌ها</h2></header>
        <div className="dashboard-recent-table">
          <div className="dashboard-table-row dashboard-table-head">
            <span>شناسه فاکتور</span>
            <span>مشتری</span>
            <span>اقلام فلزی</span>
            <span>مبلغ کل</span>
            <span>وضعیت</span>
          </div>
          {recent.length ? recent.map((transaction) => (
            <div className="dashboard-table-row" key={transaction.id}>
              <span className="numeric">{transaction.id}</span>
              <strong>{transaction.customer}</strong>
              <span>{transaction.detail}</span>
              <span className="numeric">{transaction.amount}</span>
              <span>
                <em className={`dashboard-status ${transaction.status === "Paid" ? "is-paid" : transaction.status === "Overdue" ? "is-overdue" : ""}`}>
                  {statusLabel(transaction.status)}
                </em>
              </span>
            </div>
          )) : <div className="dashboard-empty-row">تراکنش اخیری ثبت نشده است.</div>}
        </div>
      </section>

      <button className="dashboard-mobile-new-customer" type="button" onClick={() => onNavigate("/customers")}>
        <UserRoundPlus size={16} /> مشتری جدید
      </button>
    </main>
  );
}
