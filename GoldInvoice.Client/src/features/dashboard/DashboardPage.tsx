import { Info, Search, UserRoundPlus } from "lucide-react";
import type { DashboardSnapshot } from "./dashboard.types";

interface DashboardPageProps {
  snapshot: DashboardSnapshot;
  onNavigate: (path: string) => void;
}

export function DashboardPage({ snapshot, onNavigate }: DashboardPageProps) {
  const quotes = snapshot.market.goldPrices.slice(0, 2);
  const recent = snapshot.transactions.slice(0, 3);
  const customerNames = recent.map((item) => item.customer);

  return (
    <main className="dashboard-lovable" dir="ltr">
      <header className="dashboard-lovable__header">
        <div>
          <h1>Dashboard Hub</h1>
          <p>Weekly volume, metal rates, and fast-action shortcuts</p>
        </div>
        <div className="dashboard-period" aria-label="Dashboard period">
          <button className="is-active" type="button">Today</button>
          <button type="button">This Week</button>
        </div>
      </header>

      <section className="dashboard-index-bar" aria-label="Live index rates">
        <strong>LIVE INDEX RATES:</strong>
        {quotes.map((quote) => (
          <span key={quote.label}>
            {quote.label}: <b>{quote.value || "—"}</b>
          </span>
        ))}
        <span className="dashboard-index-info">
          <Info size={12} /> Updated {snapshot.market.updatedAt || "—"}
        </span>
      </section>

      <button
        className="dashboard-new-invoice"
        type="button"
        onClick={() => onNavigate("/orders/new")}
      >
        New Invoice Form
      </button>

      <section className="dashboard-transaction-row">
        <article className="dashboard-session-card">
          <div>
            <h2>Start New Transaction Session</h2>
            <p>Initiate workflow with a single barcode scan or name search</p>
          </div>
          <button type="button" onClick={() => onNavigate("/orders/new")}>
            <b>Click 1</b> Initiates Invoice Form
          </button>
        </article>

        <article className="dashboard-customer-card">
          <h2>Autofill Customer Search</h2>
          <p>Quick lookup profile details, unpaid ledgers &amp; custom purity sizes</p>
          <label className="dashboard-search">
            <Search size={16} />
            <input
              type="search"
              placeholder="Enter name or mobile..."
              list="dashboard-customers"
              aria-label="Search customer"
            />
            <datalist id="dashboard-customers">
              {customerNames.map((name) => <option value={name} key={name} />)}
            </datalist>
          </label>
          <div className="dashboard-autofill-note">
            <strong>AUTOFILL SYSTEM</strong>
            <span>Searching a name here triggers persistent auto-population of invoicing fields, reducing checkout clicks to 1 tap.</span>
          </div>
        </article>
      </section>

      <section className="dashboard-recent-card">
        <header>
          <h2>Recent Transactions</h2>
        </header>
        <div className="dashboard-recent-table">
          <div className="dashboard-table-row dashboard-table-head">
            <span>INV-ID</span>
            <span>CUSTOMER</span>
            <span>METAL ITEMS</span>
            <span>TOTAL</span>
            <span>STATUS</span>
          </div>
          {recent.length ? recent.map((transaction) => (
            <div className="dashboard-table-row" key={transaction.id}>
              <span className="numeric">{transaction.id.slice(0, 8).toUpperCase()}</span>
              <strong>{transaction.customer}</strong>
              <span>{transaction.detail}</span>
              <span className="numeric">{transaction.amount}</span>
              <span>
                <em className={`dashboard-status ${transaction.positive ? "is-paid" : "is-overdue"}`}>
                  {transaction.positive ? "Paid" : "Overdue"}
                </em>
              </span>
            </div>
          )) : (
            <div className="dashboard-empty-row">No recent transactions.</div>
          )}
        </div>
      </section>

      <button
        className="dashboard-mobile-new-customer"
        type="button"
        onClick={() => onNavigate("/customers")}
      >
        <UserRoundPlus size={16} /> New Customer
      </button>
    </main>
  );
}
