import type { LucideIcon } from "lucide-react";
import { AlertTriangle, ArrowUpLeft, Inbox, LoaderCircle, Plus, RefreshCw, X } from "lucide-react";
import type { PropsWithChildren, ReactNode } from "react";
import { activeNumberLocale, formatTomansFromRials } from "../../lib/money";

export function formatMoney(valueInRials: number): string {
  return formatTomansFromRials(valueInRials);
}

export function formatDate(value?: string | null): string {
  if (!value) return "—";
  const locale = activeNumberLocale() === "fa-IR" ? "fa-IR-u-ca-persian" : "en-US";
  return new Intl.DateTimeFormat(locale, {
    year: "numeric",
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  }).format(new Date(value));
}

export function translateStatus(status: string): string {
  const labels: Record<string, string> = {
    Active: "فعال",
    Inactive: "غیرفعال",
    Pending: "در انتظار",
    Overdue: "معوق",
    Draft: "پیش‌نویس",
    AwaitingPayment: "در انتظار پرداخت",
    PaymentReview: "بررسی پرداخت",
    Paid: "پرداخت‌شده",
    Processing: "در حال پردازش",
    Completed: "تکمیل‌شده",
    Cancelled: "لغوشده",
    Refunded: "بازپرداخت‌شده",
    Issued: "صادرشده",
    Voided: "باطل‌شده",
    Verified: "تأییدشده",
    RequiresReview: "نیازمند بررسی",
    Failed: "ناموفق",
    Open: "باز",
    Call: "تماس",
    Message: "پیام",
    Meeting: "جلسه",
    FollowUp: "پیگیری",
    Note: "یادداشت",
    Owner: "مالک",
    Admin: "مدیر",
    Customer: "مشتری",
    Cash: "نقدی",
    PointOfSale: "کارت‌خوان",
    BankTransfer: "حواله بانکی",
    CardToCard: "کارت‌به‌کارت",
    FixedPrice: "قیمت ثابت",
    WeightBased: "قیمت وزنی",
    MarketBased: "نرخ لحظه‌ای",
    ManualReview: "بررسی دستی",
    Gold18K: "طلای ۱۸ عیار",
    Gold24K: "طلای ۲۴ عیار",
  };
  return labels[status] ?? status;
}

export function PageHeader({
  icon: Icon,
  title,
  description,
  actionLabel,
  onAction,
  secondary,
}: {
  icon: LucideIcon;
  title: string;
  description: string;
  actionLabel?: string;
  onAction?: () => void;
  secondary?: ReactNode;
}) {
  return (
    <header className="module-heading fade-up">
      <div className="module-title-wrap">
        <span className="module-title-icon"><Icon size={21} /></span>
        <div>
          <p className="eyebrow gold-text">مدیریت وندوم</p>
          <h1>{title}</h1>
          <p>{description}</p>
        </div>
      </div>
      <div className="module-heading-actions">
        {secondary}
        {actionLabel && onAction && (
          <button className="primary-button" type="button" onClick={onAction}>
            <Plus size={17} /> {actionLabel}
          </button>
        )}
      </div>
    </header>
  );
}

export function ReferencePageHeader({
  eyebrow,
  title,
  description,
  actionLabel,
  onAction,
  secondary,
}: {
  eyebrow: string;
  title: string;
  description: string;
  actionLabel?: string;
  onAction?: () => void;
  secondary?: ReactNode;
}) {
  return (
    <header className="reference-page-heading fade-up">
      <div>
        <p className="eyebrow gold-text">{eyebrow}</p>
        <h1>{title}</h1>
        <p>{description}</p>
      </div>
      {(secondary || (actionLabel && onAction)) && (
        <div className="module-heading-actions">
          {secondary}
          {actionLabel && onAction && (
            <button className="primary-button" type="button" onClick={onAction}>
              <Plus size={17} /> {actionLabel}
            </button>
          )}
        </div>
      )}
    </header>
  );
}

export function FeatureNavigationCard({
  title,
  description,
  onClick,
  meta,
}: {
  title: string;
  description: string;
  onClick: () => void;
  meta?: string;
}) {
  return (
    <button className="lux-card reference-feature-card fade-up" type="button" onClick={onClick}>
      <span className="card-gold-rule" />
      <ArrowUpLeft className="reference-feature-arrow" size={16} strokeWidth={1.4} />
      <h2>{title}</h2>
      <p>{description}</p>
      {meta && <small>{meta}</small>}
    </button>
  );
}

export function RefreshButton({
  refreshing,
  onClick,
}: {
  refreshing: boolean;
  onClick: () => void;
}) {
  return (
    <button className="secondary-button" type="button" onClick={onClick} disabled={refreshing}>
      <RefreshCw className={refreshing ? "spin" : ""} size={16} />
      به‌روزرسانی
    </button>
  );
}

export function LoadingState() {
  return (
    <div className="module-state lux-card">
      <LoaderCircle className="spin" size={26} />
      <strong>در حال دریافت اطلاعات واقعی…</strong>
      <span>داده‌ها از API و دیتابیس خوانده می‌شوند.</span>
    </div>
  );
}

export function ErrorState({ message, onRetry }: { message: string; onRetry: () => void }) {
  return (
    <div className="module-state module-state--error lux-card">
      <AlertTriangle size={25} />
      <strong>دریافت اطلاعات کامل نشد</strong>
      <span>{message}</span>
      <button className="secondary-button" type="button" onClick={onRetry}>تلاش دوباره</button>
    </div>
  );
}

export function EmptyState({ title, description }: { title: string; description: string }) {
  return (
    <div className="module-state lux-card">
      <Inbox size={27} />
      <strong>{title}</strong>
      <span>{description}</span>
    </div>
  );
}

export function StatusBadge({ status }: { status: string }) {
  const positive = ["Active", "Paid", "Completed", "Issued", "Verified"].includes(status);
  const negative = ["Inactive", "Cancelled", "Voided", "Failed", "Refunded"].includes(status);
  return (
    <span className={`status-badge ${positive ? "is-positive" : negative ? "is-negative" : "is-pending"}`}>
      {translateStatus(status)}
    </span>
  );
}

export function MetricTile({ label, value, hint }: { label: string; value: string; hint: string }) {
  return (
    <article className="lux-card module-metric">
      <span>{label}</span>
      <strong>{value}</strong>
      <small>{hint}</small>
    </article>
  );
}

export function TableCard({ children }: PropsWithChildren) {
  return <section className="lux-card table-card fade-up">{children}</section>;
}

export function Modal({
  open,
  title,
  description,
  onClose,
  children,
}: PropsWithChildren<{
  open: boolean;
  title: string;
  description?: string;
  onClose: () => void;
}>) {
  if (!open) return null;
  return (
    <div className="modal-layer" role="presentation" onMouseDown={onClose}>
      <section
        className="modal-card"
        role="dialog"
        aria-modal="true"
        aria-labelledby="modal-title"
        onMouseDown={(event) => event.stopPropagation()}
      >
        <header>
          <div>
            <h2 id="modal-title">{title}</h2>
            {description && <p>{description}</p>}
          </div>
          <button type="button" aria-label="بستن" onClick={onClose}><X size={19} /></button>
        </header>
        <div className="modal-body">{children}</div>
      </section>
    </div>
  );
}

export function FormField({
  label,
  hint,
  children,
  wide = false,
}: PropsWithChildren<{ label: string; hint?: string; wide?: boolean }>) {
  return (
    <label className={`form-field ${wide ? "form-field--wide" : ""}`}>
      <span>{label}</span>
      {children}
      {hint && <small>{hint}</small>}
    </label>
  );
}

export function FormActions({
  saving,
  submitLabel,
  onCancel,
}: {
  saving: boolean;
  submitLabel: string;
  onCancel: () => void;
}) {
  return (
    <div className="form-actions">
      <button className="secondary-button" type="button" onClick={onCancel}>انصراف</button>
      <button className="primary-button" type="submit" disabled={saving}>
        {saving && <LoaderCircle className="spin" size={16} />}
        {saving ? "در حال ثبت…" : submitLabel}
      </button>
    </div>
  );
}

export function InlineError({ message }: { message: string | null }) {
  return message ? <div className="inline-error"><AlertTriangle size={15} />{message}</div> : null;
}
