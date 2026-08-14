import { useState } from "react";
import type {
  CategoryShare,
  RevenuePoint,
} from "./dashboard.types";
import { activeNumberLocale, formatTomansFromRials } from "../../lib/money";

interface RevenueChartProps {
  values: RevenuePoint[];
}

function toLocalizedDigits(value: number) {
  if (activeNumberLocale() === "en-US") return value.toString().replace("-", "−");
  return value.toString()
    .replace("-", "−")
    .replace(/\d/g, (digit) => "۰۱۲۳۴۵۶۷۸۹"[Number(digit)]);
}

function chartPoints(
  values: RevenuePoint[],
  key: "revenue" | "profit",
  width: number,
  height: number,
  min: number,
  max: number,
) {
  const range = Math.max(max - min, 1);
  return values.map((point, index) => ({
    x: (index / Math.max(values.length - 1, 1)) * width,
    y: ((max - point[key]) / range) * height,
  }));
}

function smoothPath(points: Array<{ x: number; y: number }>) {
  if (points.length < 2) return "";

  return points.slice(1).reduce((path, point, index) => {
    const previous = points[index];
    const controlX = (previous.x + point.x) / 2;
    return `${path} C ${controlX} ${previous.y}, ${controlX} ${point.y}, ${point.x} ${point.y}`;
  }, `M ${points[0].x} ${points[0].y}`);
}

export function RevenueChart({ values }: RevenueChartProps) {
  const [activeIndex, setActiveIndex] = useState<number | null>(null);
  const width = 640;
  const height = 220;
  const max = Math.max(
    ...values.flatMap((point) => [point.revenue, point.profit]),
    1,
  );
  const min = Math.min(...values.flatMap((point) => [point.revenue, point.profit]), 0);
  const revenue = chartPoints(values, "revenue", width, height, min, max);
  const profit = chartPoints(values, "profit", width, height, min, max);
  const revenuePath = smoothPath(revenue);
  const profitPath = smoothPath(profit);
  const zeroY = ((max - 0) / Math.max(max - min, 1)) * height;
  const areaPath = `${revenuePath} L ${width} ${zeroY} L 0 ${zeroY} Z`;
  const activePoint = activeIndex === null ? null : values[activeIndex];
  const activeRevenue = activeIndex === null ? null : revenue[activeIndex];
  const activeProfit = activeIndex === null ? null : profit[activeIndex];
  const tooltipLeft = activeRevenue
    ? Math.min(84, Math.max(16, (activeRevenue.x / width) * 100))
    : 50;

  return (
    <div className="revenue-chart" role="group" aria-label="نمودار فروش و سود هفت ماه گذشته" onPointerLeave={() => setActiveIndex(null)}>
      <svg viewBox="0 0 700 285" preserveAspectRatio="none">
        <defs>
          <linearGradient id="revenue-area" x1="0" x2="0" y1="0" y2="1">
            <stop offset="0%" stopColor="var(--gold)" stopOpacity="0.34" />
            <stop offset="100%" stopColor="var(--gold)" stopOpacity="0.02" />
          </linearGradient>
        </defs>
        <g transform="translate(34 14)">
          {[0, 55, 110, 165, 220].map((y, index) => (
            <g key={y}>
              <line className="chart-grid-line" x1="0" x2={width} y1={y} y2={y} />
              <text className="chart-y-label" x="-9" y={y + 4} textAnchor="end">
                {toLocalizedDigits(Math.round(max - ((max - min) * index) / 4))}
              </text>
            </g>
          ))}
          <path d={areaPath} fill="url(#revenue-area)" />
          <path className="chart-line chart-line--gold" d={revenuePath} />
          <path className="chart-line chart-line--navy" d={profitPath} />
          {activePoint && activeRevenue && activeProfit && (
            <g className="chart-active-markers" aria-hidden="true">
              <line className="chart-hover-guide" x1={activeRevenue.x} x2={activeRevenue.x} y1="0" y2={height} />
              <circle className="chart-point chart-point--profit" cx={activeProfit.x} cy={activeProfit.y} r="5" />
              <circle className="chart-point chart-point--sales" cx={activeRevenue.x} cy={activeRevenue.y} r="5" />
            </g>
          )}
          {values.map((point, index) => (
            <text
              className="chart-axis-label"
              x={revenue[index].x}
              y="251"
              textAnchor="middle"
              key={point.month}
            >
              {point.month}
            </text>
          ))}
          {values.map((point, index) => {
            const segment = width / Math.max(values.length - 1, 1);
            const start = index === 0 ? 0 : revenue[index].x - segment / 2;
            const end = index === values.length - 1 ? width : revenue[index].x + segment / 2;
            return <rect
              className="chart-hit-area"
              x={start}
              y="0"
              width={Math.max(1, end - start)}
              height={height}
              key={`hit-${point.month}`}
              onPointerEnter={() => setActiveIndex(index)}
              onPointerMove={() => setActiveIndex(index)}
            />;
          })}
        </g>
      </svg>
      {activePoint && (
        <div className="chart-tooltip" style={{ left: `${tooltipLeft}%` }} role="status">
          <strong>{activePoint.month}</strong>
          <span><i className="chart-legend-dot chart-legend-dot--profit" /> سود <b>{toLocalizedDigits(Number(activePoint.profit.toFixed(1)))}</b></span>
          <span><i className="chart-legend-dot chart-legend-dot--sales" /> فروش <b>{toLocalizedDigits(Number(activePoint.revenue.toFixed(1)))}</b></span>
          <small>میلیون تومان</small>
        </div>
      )}
    </div>
  );
}

function formatMoney(value: number) {
  return formatTomansFromRials(value);
}

export function CategoryChart({
  values,
  onNavigate,
}: {
  values: CategoryShare[];
  onNavigate?: (path: string) => void;
}) {
  const [activeIndex, setActiveIndex] = useState(0);
  const max = Math.max(...values.map((item) => item.value), 1);
  const active = values[Math.min(activeIndex, Math.max(values.length - 1, 0))];

  return (
    <div className="category-chart">
      <div className="category-bars" role="list" aria-label="سهم درآمد دسته‌بندی‌ها">
        {values.map((item, index) => (
          <button
            className={`category-bar-wrap ${index === activeIndex ? "is-active" : ""}`}
            type="button"
            role="listitem"
            aria-pressed={index === activeIndex}
            aria-label={`${item.label}، ${toLocalizedDigits(item.value)} درصد`}
            key={item.label}
            onClick={() => setActiveIndex(index)}
          >
            <div
              className="category-bar"
              style={{ height: `${Math.max((item.value / max) * 126, 20)}px` }}
            />
            <span>{item.label}</span>
          </button>
        ))}
      </div>
      {active && (
        <div className="category-selection" role="status">
          <div><small>درآمد این ماه</small><strong>{formatMoney(active.revenueRials)}</strong></div>
          <span>{toLocalizedDigits(active.invoiceCount)} فاکتور · {toLocalizedDigits(active.productCount)} محصول</span>
          {active.categoryId && onNavigate && (
            <button type="button" onClick={() => onNavigate(`/products?categoryId=${active.categoryId}`)}>
              مشاهده محصولات
            </button>
          )}
        </div>
      )}
      <div className="category-progress-list">
        {values.slice(0, 3).map((item, index) => (
          <button className="category-progress" type="button" key={item.label} onClick={() => setActiveIndex(index)}>
            <div>
              <span>{item.label}</span>
              <strong>{toLocalizedDigits(item.value)}٪</strong>
            </div>
            <div className="progress-track">
              <i style={{ width: `${item.value}%` }} />
            </div>
          </button>
        ))}
      </div>
    </div>
  );
}
