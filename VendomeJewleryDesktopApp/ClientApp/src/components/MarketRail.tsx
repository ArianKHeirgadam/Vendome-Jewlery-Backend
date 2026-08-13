import { RefreshCw, TrendingDown, TrendingUp } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import type {
  DashboardSnapshot,
  MarketQuote,
} from "../features/dashboard/dashboard.types";

interface MarketRailProps {
  market: DashboardSnapshot["market"];
}

function formatClock(date: Date) {
  return new Intl.DateTimeFormat("fa-IR", {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    hour12: false,
  }).format(date);
}

function QuoteRows({ quotes }: { quotes: MarketQuote[] }) {
  return (
    <div className="market-rows">
      {quotes.map((quote) => (
        <div className="market-row" key={quote.label}>
          <span className="market-label">{quote.label}</span>
          <span className="market-value-wrap">
            <strong className="numeric" dir="ltr">
              {quote.value}
            </strong>
            {quote.unit && <small>{quote.unit}</small>}
            {quote.trend && (
              <span className={`trend trend--${quote.direction}`}>
                {quote.direction === "down" ? (
                  <TrendingDown size={11} />
                ) : (
                  <TrendingUp size={11} />
                )}
                {quote.trend}
              </span>
            )}
          </span>
        </div>
      ))}
    </div>
  );
}

function Sparkline({ values }: { values: number[] }) {
  const points = values
    .map((value, index) => {
      const x = (index / (values.length - 1)) * 236;
      const min = Math.min(...values);
      const max = Math.max(...values);
      const y = 47 - ((value - min) / Math.max(max - min, 1)) * 34;
      return `${x},${y}`;
    })
    .join(" ");

  return (
    <svg className="sparkline" viewBox="0 0 236 58" aria-hidden="true">
      <polyline points={points} fill="none" vectorEffect="non-scaling-stroke" />
    </svg>
  );
}

function MarketClock({ date }: { date: Date }) {
  const seconds = date.getSeconds() * 6;
  const minutes = date.getMinutes() * 6 + date.getSeconds() * 0.1;
  const hours = (date.getHours() % 12) * 30 + date.getMinutes() * 0.5;

  return (
    <div className="analog-clock" aria-label="ساعت عقربه‌ای">
      {Array.from({ length: 12 }, (_, index) => (
        <i
          key={index}
          style={{ transform: `rotate(${index * 30}deg)` }}
        />
      ))}
      <span className="clock-hand clock-hand--hour" style={{ rotate: `${hours}deg` }} />
      <span
        className="clock-hand clock-hand--minute"
        style={{ rotate: `${minutes}deg` }}
      />
      <span
        className="clock-hand clock-hand--second"
        style={{ rotate: `${seconds}deg` }}
      />
      <b />
    </div>
  );
}

export function MarketRail({ market }: MarketRailProps) {
  const [now, setNow] = useState(() => new Date());

  useEffect(() => {
    const timer = window.setInterval(() => setNow(new Date()), 1000);
    return () => window.clearInterval(timer);
  }, []);

  const time = useMemo(() => formatClock(now), [now]);

  return (
    <aside className="market-rail" aria-label="بازار زنده">
      <div className="market-heading">
        <h2>بازار زنده</h2>
        <span>
          {market.updatedAt}
          <RefreshCw size={12} strokeWidth={1.45} aria-hidden="true" />
        </span>
      </div>

      <section className="market-card">
        <h3>قیمت طلا</h3>
        <QuoteRows quotes={market.goldPrices} />
      </section>

      <section className="market-card">
        <h3>خرید و فروش طلا</h3>
        <QuoteRows quotes={market.trading} />
      </section>

      <section className="market-card">
        <h3>نرخ ارز</h3>
        <QuoteRows quotes={market.currencies} />
      </section>

      <section className="market-card market-status-card">
        <h3>وضعیت بازار</h3>
        <div className="market-clock-row">
          <MarketClock date={now} />
          <div>
            <strong className="market-time">{time}</strong>
            <p>{market.hours}</p>
            <span className={`market-state ${market.isOpen ? "is-open" : ""}`}>
              <i /> {market.isOpen ? "بازار باز است" : "بازار بسته است"}
            </span>
          </div>
        </div>
      </section>

      <section className="market-card trend-card">
        <h3>روند فلزات گران‌بها</h3>
        <p>روزانه · ۱۸ عیار</p>
        <Sparkline values={market.dailyTrend} />
        <p>هفتگی · ۱۸ عیار</p>
        <Sparkline values={market.weeklyTrend} />
      </section>
    </aside>
  );
}
