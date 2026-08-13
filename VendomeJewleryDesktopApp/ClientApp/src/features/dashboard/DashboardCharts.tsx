import type {
  CategoryShare,
  RevenuePoint,
} from "./dashboard.types";

interface RevenueChartProps {
  values: RevenuePoint[];
}

function toPersianDigits(value: number) {
  return value.toString().replace(/\d/g, (digit) => "۰۱۲۳۴۵۶۷۸۹"[Number(digit)]);
}

function chartPoints(
  values: RevenuePoint[],
  key: "revenue" | "profit",
  width: number,
  height: number,
) {
  const max = 3600;
  return values.map((point, index) => ({
    x: (index / (values.length - 1)) * width,
    y: height - (point[key] / max) * height,
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
  const width = 640;
  const height = 220;
  const revenue = chartPoints(values, "revenue", width, height);
  const profit = chartPoints(values, "profit", width, height);
  const revenuePath = smoothPath(revenue);
  const profitPath = smoothPath(profit);
  const areaPath = `${revenuePath} L ${width} ${height} L 0 ${height} Z`;

  return (
    <div className="revenue-chart" role="img" aria-label="نمودار درآمد و سود هفت ماه گذشته">
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
                {["۳٬۶۰۰", "۲٬۷۰۰", "۱٬۸۰۰", "۹۰۰", "۰"][index]}
              </text>
            </g>
          ))}
          <path d={areaPath} fill="url(#revenue-area)" />
          <path className="chart-line chart-line--gold" d={revenuePath} />
          <path className="chart-line chart-line--navy" d={profitPath} />
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
        </g>
      </svg>
    </div>
  );
}

export function CategoryChart({ values }: { values: CategoryShare[] }) {
  const max = Math.max(...values.map((item) => item.value));

  return (
    <div className="category-chart">
      <div className="category-bars" aria-hidden="true">
        {values.map((item) => (
          <div className="category-bar-wrap" key={item.label}>
            <div
              className="category-bar"
              style={{ height: `${Math.max((item.value / max) * 126, 20)}px` }}
            />
            <span>{item.label}</span>
          </div>
        ))}
      </div>
      <div className="category-progress-list">
        {values.slice(0, 3).map((item) => (
          <div className="category-progress" key={item.label}>
            <div>
              <span>{item.label}</span>
              <strong>{toPersianDigits(item.value)}٪</strong>
            </div>
            <div className="progress-track">
              <i style={{ width: `${item.value}%` }} />
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
