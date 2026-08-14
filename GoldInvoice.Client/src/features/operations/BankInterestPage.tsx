import {
  BadgeDollarSign,
  CalendarDays,
  Landmark,
  LockKeyhole,
  PiggyBank,
  RefreshCw,
  TableProperties,
} from "lucide-react";
import { type FormEvent, useEffect, useMemo, useState } from "react";
import { rialsToTomans, tomansToRials } from "../../lib/money";
import { useOperations } from "./OperationsContext";
import {
  EmptyState,
  FormActions,
  FormField,
  formatMoney,
  InlineError,
  Modal,
  PageHeader,
  TableCard,
} from "./PagePrimitives";
import "./accounting-enhancements.css";

interface RouteProps {
  path: string;
  onNavigate: (path: string) => void;
  onNotice: (message: string) => void;
}

interface BankDeposit {
  id: string;
  bankName: string;
  title: string;
  accountNumber?: string | null;
  principalRials: number;
  annualInterestRatePercent: number;
  openedOn: string;
  maturityOn?: string | null;
  isActive: boolean;
  createdAt: string;
  closedAt?: string | null;
}

interface BankInterestEntry {
  id: string;
  depositId?: string | null;
  direction: "Received" | "Paid";
  bankName: string;
  occurredOn: string;
  amountRials: number;
  reference?: string | null;
  createdAt: string;
}

interface BankInterestSnapshot {
  deposits: BankDeposit[];
  entries: BankInterestEntry[];
}

function todayValue(): string {
  const now = new Date();
  return new Date(now.getTime() - now.getTimezoneOffset() * 60_000)
    .toISOString()
    .slice(0, 10);
}

function displayDate(value?: string | null): string {
  if (!value) return "—";
  return new Intl.DateTimeFormat(
    document.documentElement.lang === "en" ? "en-US" : "fa-IR-u-ca-persian",
    { year: "numeric", month: "short", day: "numeric" },
  ).format(new Date(`${value}T00:00:00`));
}

export function AccountingQuickLinks({
  onNavigate,
}: {
  onNavigate: (path: string) => void;
}) {
  const english = document.documentElement.lang === "en";

  return (
    <div className="accounting-quick-links">
      <button
        className="lux-card accounting-link-card"
        type="button"
        onClick={() => onNavigate("/accounting/installments")}
      >
        <TableProperties size={19} />
        <span>
          <strong>{english ? "Installments" : "اقساط"}</strong>
          <small>{english ? "Payment schedule and collections" : "جدول پرداخت و وضعیت وصول"}</small>
        </span>
      </button>

      <button
        className="lux-card accounting-link-card"
        type="button"
        onClick={() => onNavigate("/accounting/bank-interest")}
      >
        <BadgeDollarSign size={19} />
        <span>
          <strong>{english ? "Bank Interest" : "سود بانکی"}</strong>
          <small>{english ? "Deposits and interest ledger" : "سپرده‌ها و دفتر سود بانکی"}</small>
        </span>
      </button>
    </div>
  );
}

export function BankInterestPage({ onNotice }: RouteProps) {
  const { request } = useOperations();
  const [snapshot, setSnapshot] = useState<BankInterestSnapshot>({ deposits: [], entries: [] });
  const [depositOpen, setDepositOpen] = useState(false);
  const [entryOpen, setEntryOpen] = useState(false);
  const [saving, setSaving] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = async () => {
    setLoading(true);
    setError(null);
    try {
      setSnapshot(await request<BankInterestSnapshot>("/api/v1/finance/bank-interest"));
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "دریافت اطلاعات سود بانکی کامل نشد.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void load();
  }, [request]);

  const activeDeposits = snapshot.deposits.filter((item) => item.isActive);

  const receivedTotal = snapshot.entries
    .filter((item) => item.direction === "Received")
    .reduce((sum, item) => sum + item.amountRials, 0);

  const paidTotal = snapshot.entries
    .filter((item) => item.direction === "Paid")
    .reduce((sum, item) => sum + item.amountRials, 0);

  const principalTotal = activeDeposits
    .reduce((sum, item) => sum + item.principalRials, 0);

  const projectedAnnual = activeDeposits.reduce(
    (sum, item) =>
      sum + Math.round(item.principalRials * (item.annualInterestRatePercent / 100)),
    0,
  );

  const interestForDeposit = useMemo(() => {
    const map = new Map<string, number>();
    for (const entry of snapshot.entries) {
      if (!entry.depositId || entry.direction !== "Received") continue;
      map.set(entry.depositId, (map.get(entry.depositId) || 0) + entry.amountRials);
    }
    return map;
  }, [snapshot.entries]);

  const submitDeposit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const formElement = event.currentTarget;
    const form = new FormData(formElement);
    const principalTomans = Number(form.get("principalTomans") || 0);
    const rate = Number(form.get("annualRate") || 0);

    if (!Number.isSafeInteger(principalTomans) || principalTomans <= 0 || !Number.isFinite(rate) || rate < 0 || rate > 100) {
      setError("اصل سپرده و نرخ سود معتبر وارد کن.");
      return;
    }

    setSaving(true);
    setError(null);
    try {
      await request("/api/v1/finance/bank-interest/deposits", {
        method: "POST",
        body: JSON.stringify({
          bankName: String(form.get("bankName") || "").trim(),
          title: String(form.get("title") || "").trim(),
          accountNumber: String(form.get("accountNumber") || "").trim() || null,
          principalRials: tomansToRials(principalTomans),
          annualInterestRatePercent: rate,
          openedOn: String(form.get("openedOn") || ""),
          maturityOn: String(form.get("maturityOn") || "") || null,
        }),
      });
      formElement.reset();
      setDepositOpen(false);
      setSaving(false);
      onNotice("سپرده بانکی ثبت شد.");
      await load();
    } catch (caught) {
      setSaving(false);
      setError(caught instanceof Error ? caught.message : "ثبت سپرده بانکی کامل نشد.");
    }
  };

  const submitEntry = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const formElement = event.currentTarget;
    const form = new FormData(formElement);
    const amountTomans = Number(form.get("amountTomans") || 0);

    if (!Number.isSafeInteger(amountTomans) || amountTomans <= 0) {
      setError("مبلغ معتبر وارد کن.");
      return;
    }

    setSaving(true);
    setError(null);
    try {
      await request("/api/v1/finance/bank-interest/entries", {
        method: "POST",
        body: JSON.stringify({
          depositId: String(form.get("depositId") || "") || null,
          direction: String(form.get("direction") || ""),
          bankName: String(form.get("bankName") || "").trim(),
          occurredOn: String(form.get("occurredOn") || ""),
          amountRials: tomansToRials(amountTomans),
          reference: String(form.get("reference") || "").trim() || null,
        }),
      });
      formElement.reset();
      setEntryOpen(false);
      setSaving(false);
      onNotice("رکورد سود بانکی ثبت شد.");
      await load();
    } catch (caught) {
      setSaving(false);
      setError(caught instanceof Error ? caught.message : "ثبت رکورد سود بانکی کامل نشد.");
    }
  };

  const closeDeposit = async (deposit: BankDeposit) => {
    if (!deposit.isActive) return;
    if (!window.confirm(`سپرده «${deposit.title}» بسته شود؟`)) return;

    setError(null);
    try {
      await request(`/api/v1/finance/bank-interest/deposits/${deposit.id}/close`, {
        method: "POST",
      });
      onNotice("سپرده بسته شد.");
      await load();
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "بستن سپرده کامل نشد.");
    }
  };

  return (
    <main className="module-main bank-interest-page" dir="rtl">
      <PageHeader
        icon={Landmark}
        title="سود بانکی"
        description="سپرده‌های بانکی، سود دریافتی، سود پرداختی و خالص جریان سود."
        actionLabel="سپرده جدید"
        onAction={() => {
          setError(null);
          setDepositOpen(true);
        }}
        secondary={
          <div className="reference-inline-actions">
            <button
              className="secondary-button"
              type="button"
              onClick={() => {
                setError(null);
                setEntryOpen(true);
              }}
            >
              <BadgeDollarSign size={16} />
              ثبت سود
            </button>
            <button className="secondary-button" type="button" disabled={loading} onClick={() => void load()}>
              <RefreshCw className={loading ? "spin" : ""} size={16} />
              به‌روزرسانی
            </button>
          </div>
        }
      />

      <InlineError message={error} />

      <div className="module-metrics-grid">
        <article className="lux-card module-metric">
          <span>اصل سپرده فعال</span>
          <strong>{formatMoney(principalTotal)}</strong>
          <small>{activeDeposits.length} سپرده فعال</small>
        </article>
        <article className="lux-card module-metric">
          <span>سود دریافتی</span>
          <strong>{formatMoney(receivedTotal)}</strong>
          <small>ثبت‌شده در دفتر</small>
        </article>
        <article className="lux-card module-metric">
          <span>سود پرداختی</span>
          <strong>{formatMoney(paidTotal)}</strong>
          <small>هزینه سود ثبت‌شده</small>
        </article>
        <article className="lux-card module-metric">
          <span>برآورد سود سالانه</span>
          <strong>{formatMoney(projectedAnnual)}</strong>
          <small>بر اساس نرخ سپرده‌های فعال</small>
        </article>
      </div>

      <section className="bank-interest-section">
        <header className="bank-interest-section-heading">
          <div>
            <h2>سپرده‌های بانکی</h2>
            <p>اصل سپرده، نرخ سالانه، سررسید و سود دریافتی ثبت‌شده.</p>
          </div>
        </header>

        {snapshot.deposits.length ? (
          <TableCard>
            <div className="table-scroll">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>بانک</th>
                    <th>عنوان سپرده</th>
                    <th>اصل سپرده</th>
                    <th>نرخ سالانه</th>
                    <th>شروع</th>
                    <th>سررسید</th>
                    <th>سود دریافتی</th>
                    <th>وضعیت</th>
                    <th>عملیات</th>
                  </tr>
                </thead>
                <tbody>
                  {snapshot.deposits.map((deposit) => (
                    <tr key={deposit.id}>
                      <td><strong>{deposit.bankName}</strong></td>
                      <td>{deposit.title}{deposit.accountNumber ? <small>{deposit.accountNumber}</small> : null}</td>
                      <td>{formatMoney(deposit.principalRials)}</td>
                      <td>{deposit.annualInterestRatePercent.toLocaleString("fa-IR", { maximumFractionDigits: 2 })}٪</td>
                      <td>{displayDate(deposit.openedOn)}</td>
                      <td>{displayDate(deposit.maturityOn)}</td>
                      <td>{formatMoney(interestForDeposit.get(deposit.id) || 0)}</td>
                      <td><span className="finance-neutral-badge">{deposit.isActive ? "فعال" : "بسته‌شده"}</span></td>
                      <td>
                        <button
                          className="icon-action icon-action--gold"
                          type="button"
                          title="بستن سپرده"
                          aria-label={`بستن سپرده ${deposit.title}`}
                          disabled={!deposit.isActive}
                          onClick={() => void closeDeposit(deposit)}
                        >
                          <LockKeyhole size={15} />
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </TableCard>
        ) : (
          <EmptyState title="سپرده‌ای ثبت نشده" description="اولین سپرده بانکی را از دکمه «سپرده جدید» ثبت کن." />
        )}
      </section>

      <section className="bank-interest-section">
        <header className="bank-interest-section-heading">
          <div>
            <h2>دفتر سود بانکی</h2>
            <p>سودهای دریافتی و پرداختی به ترتیب تاریخ.</p>
          </div>
        </header>

        {snapshot.entries.length ? (
          <TableCard>
            <div className="table-scroll">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>تاریخ</th>
                    <th>نوع</th>
                    <th>بانک</th>
                    <th>سپرده</th>
                    <th>مبلغ</th>
                    <th>پیگیری</th>
                  </tr>
                </thead>
                <tbody>
                  {snapshot.entries.map((entry) => {
                    const deposit = snapshot.deposits.find((item) => item.id === entry.depositId);
                    return (
                      <tr key={entry.id}>
                        <td>{displayDate(entry.occurredOn)}</td>
                        <td><span className="finance-neutral-badge">{entry.direction === "Received" ? "دریافتی" : "پرداختی"}</span></td>
                        <td>{entry.bankName}</td>
                        <td>{deposit?.title || "—"}</td>
                        <td><strong className="bank-interest-amount">{formatMoney(entry.amountRials)}</strong></td>
                        <td>{entry.reference || "—"}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </TableCard>
        ) : (
          <EmptyState title="رکورد سودی ثبت نشده" description="سود دریافتی یا پرداختی را با دکمه «ثبت سود» اضافه کن." />
        )}
      </section>

      <Modal
        open={depositOpen}
        title="ثبت سپرده بانکی"
        description="اطلاعات اصل سپرده و نرخ سالانه را ثبت کن."
        onClose={() => {
          setDepositOpen(false);
          setError(null);
        }}
      >
        <form className="entity-form" onSubmit={(event) => void submitDeposit(event)}>
          <FormField label="نام بانک">
            <input name="bankName" maxLength={120} required />
          </FormField>
          <FormField label="عنوان سپرده">
            <input name="title" maxLength={160} placeholder="مثلاً سپرده کوتاه‌مدت" required />
          </FormField>
          <FormField label="شماره حساب / سپرده">
            <input name="accountNumber" maxLength={64} dir="ltr" />
          </FormField>
          <FormField label="اصل سپرده (تومان)">
            <input name="principalTomans" type="number" min="1" step="1" required />
          </FormField>
          <FormField label="نرخ سود سالانه (%)">
            <input name="annualRate" type="number" min="0" max="100" step="0.01" required />
          </FormField>
          <FormField label="تاریخ شروع">
            <input name="openedOn" type="date" defaultValue={todayValue()} required />
          </FormField>
          <FormField label="تاریخ سررسید">
            <input name="maturityOn" type="date" />
          </FormField>
          <InlineError message={error} />
          <FormActions
            saving={saving}
            submitLabel="ثبت سپرده"
            onCancel={() => {
              setDepositOpen(false);
              setError(null);
            }}
          />
        </form>
      </Modal>

      <Modal
        open={entryOpen}
        title="ثبت سود بانکی"
        description="سود دریافتی یا پرداختی را در دفتر مالی ثبت کن."
        onClose={() => {
          setEntryOpen(false);
          setError(null);
        }}
      >
        <form className="entity-form" onSubmit={(event) => void submitEntry(event)}>
          <FormField label="نوع">
            <select name="direction" defaultValue="Received" required>
              <option value="Received">سود دریافتی</option>
              <option value="Paid">سود پرداختی</option>
            </select>
          </FormField>
          <FormField label="سپرده مرتبط">
            <select name="depositId" defaultValue="">
              <option value="">بدون اتصال به سپرده</option>
              {activeDeposits.map((deposit) => (
                <option value={deposit.id} key={deposit.id}>{deposit.bankName} · {deposit.title}</option>
              ))}
            </select>
          </FormField>
          <FormField label="نام بانک">
            <input name="bankName" maxLength={120} required />
          </FormField>
          <FormField label="تاریخ">
            <input name="occurredOn" type="date" defaultValue={todayValue()} required />
          </FormField>
          <FormField label="مبلغ (تومان)">
            <input name="amountTomans" type="number" min="1" step="1" required />
          </FormField>
          <FormField label="شماره پیگیری / توضیح" wide>
            <input name="reference" maxLength={200} />
          </FormField>
          <InlineError message={error} />
          <FormActions
            saving={saving}
            submitLabel="ثبت در دفتر سود"
            onCancel={() => {
              setEntryOpen(false);
              setError(null);
            }}
          />
        </form>
      </Modal>
    </main>
  );
}
