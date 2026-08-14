import { CircleDollarSign, Landmark, RefreshCw, TableProperties, WalletCards } from "lucide-react";
import { type FormEvent, useEffect, useMemo, useState } from "react";
import { tomansToRials } from "../../lib/money";
import type { Order } from "./operations.types";
import { EmptyState, FormActions, FormField, formatDate, formatMoney, InlineError, Modal, PageHeader, StatusBadge, TableCard } from "./PagePrimitives";
import { useOperations } from "./OperationsContext";
import type { InstallmentLine, InstallmentPlan } from "./FlexiblePaymentModals";
import "./flexible-payments.css";

interface RouteProps {
  path: string;
  onNavigate: (path: string) => void;
  onNotice: (message: string) => void;
}

interface TrustFundEntry {
  id: string;
  customerId: string;
  orderId?: string | null;
  entryType: "Deposit" | "Release" | "Allocation";
  amountRials: number;
  occurredAt: string;
  reference?: string | null;
}

interface TrustFundBalance {
  customerId: string;
  balanceRials: number;
}

interface TrustFundSnapshot {
  entries: TrustFundEntry[];
  balances: TrustFundBalance[];
}

function queryValue(path: string, name: string): string {
  const query = path.split("?")[1] || "";
  return new URLSearchParams(query).get(name) || "";
}

function dateOnly(value: string): string {
  return new Intl.DateTimeFormat("fa-IR-u-ca-persian", {
    year: "numeric",
    month: "short",
    day: "numeric",
  }).format(new Date(`${value}T00:00:00`));
}

function installmentStatus(line: InstallmentLine): string {
  if (line.paidAt) return "Paid";
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const due = new Date(`${line.dueOn}T00:00:00`);
  return due < today ? "Overdue" : "Pending";
}

export function InstallmentsPage({ path, onNavigate, onNotice }: RouteProps) {
  const { request, refresh } = useOperations();
  const [plans, setPlans] = useState<InstallmentPlan[]>([]);
  const [selected, setSelected] = useState<InstallmentPlan | null>(null);
  const [payLine, setPayLine] = useState<InstallmentLine | null>(null);
  const [reference, setReference] = useState("");
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = async () => {
    setLoading(true);
    setError(null);
    try {
      const result = await request<InstallmentPlan[]>("/api/v1/finance/installments");
      setPlans(result);
      const requested = queryValue(path, "open");
      if (requested) {
        setSelected(result.find((plan) => plan.id === requested) || null);
      } else if (selected) {
        setSelected(result.find((plan) => plan.id === selected.id) || null);
      }
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "دریافت اقساط کامل نشد.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void load();
    // Path determines an optional opened plan; manual reloads use load().
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [path, request]);

  const stats = useMemo(() => {
    const all = plans.flatMap((plan) => plan.installments);
    const paid = all.filter((line) => line.paidAt).length;
    return {
      plans: plans.length,
      unpaid: all.length - paid,
      balance: plans.reduce((sum, plan) =>
        sum + plan.installments
          .filter((line) => !line.paidAt)
          .reduce((lineSum, line) => lineSum + line.amountRials, 0),
      0),
    };
  }, [plans]);

  const submitPayment = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!selected || !payLine) return;

    setSaving(true);
    setError(null);
    try {
      const updated = await request<InstallmentPlan>(
        `/api/v1/finance/installments/${selected.id}/items/${payLine.id}/pay`,
        {
          method: "POST",
          body: JSON.stringify({ reference: reference.trim() || null }),
        },
      );

      setPlans((current) => current.map((plan) => plan.id === updated.id ? updated : plan));
      setSelected(updated);
      setPayLine(null);
      setReference("");
      setSaving(false);
      await refresh();

      if (updated.invoiceId) {
        onNotice("آخرین قسط پرداخت شد؛ سفارش تسویه و فاکتور صادر شد.");
      } else {
        onNotice("قسط با موفقیت پرداخت شد.");
      }
    } catch (caught) {
      setSaving(false);
      setError(caught instanceof Error ? caught.message : "ثبت پرداخت قسط کامل نشد.");
    }
  };

  return (
    <main className="module-main finance-ledger-page" dir="rtl">
      <PageHeader
        icon={WalletCards}
        title="اقساط"
        description="برنامه‌های اقساطی، سررسیدها، مبالغ پرداخت‌شده و اقساط باقی‌مانده."
        secondary={
          <button className="secondary-button" type="button" disabled={loading} onClick={() => void load()}>
            <RefreshCw className={loading ? "spin" : ""} size={15} />
            به‌روزرسانی
          </button>
        }
      />

      <InlineError message={error} />

      <div className="module-metrics-grid">
        <article className="lux-card module-metric"><span>پرونده اقساط</span><strong>{stats.plans}</strong><small>سفارش اقساطی</small></article>
        <article className="lux-card module-metric"><span>قسط باقی‌مانده</span><strong>{stats.unpaid}</strong><small>نیازمند وصول</small></article>
        <article className="lux-card module-metric"><span>مانده اقساط</span><strong>{formatMoney(stats.balance)}</strong><small>جمع مبالغ پرداخت‌نشده</small></article>
      </div>

      {plans.length ? (
        <TableCard>
          <div className="table-scroll">
            <table className="data-table">
              <thead>
                <tr><th>سفارش</th><th>مشتری</th><th>مبلغ کل</th><th>پرداخت‌شده</th><th>باقی‌مانده</th><th>مانده</th><th>جدول</th></tr>
              </thead>
              <tbody>
                {plans.map((plan) => {
                  const paid = plan.installments.filter((line) => line.paidAt).length;
                  const remaining = plan.installments.length - paid;
                  const remainingAmount = plan.installments
                    .filter((line) => !line.paidAt)
                    .reduce((sum, line) => sum + line.amountRials, 0);

                  return (
                    <tr key={plan.id}>
                      <td><strong>{plan.orderNumber}</strong></td>
                      <td>{plan.customerName}</td>
                      <td>{formatMoney(plan.totalAmountRials)}</td>
                      <td>{paid}</td>
                      <td>{remaining}</td>
                      <td>{formatMoney(remainingAmount)}</td>
                      <td>
                        <button
                          className="icon-action icon-action--gold"
                          type="button"
                          title="نمایش جدول اقساط"
                          aria-label={`جدول اقساط سفارش ${plan.orderNumber}`}
                          onClick={() => setSelected(plan)}
                        >
                          <TableProperties size={16} />
                        </button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </TableCard>
      ) : (
        <EmptyState title="فروش اقساطی ثبت نشده" description="هنگام تسویه سفارش، روش «اقساطی» را انتخاب کن." />
      )}

      <Modal
        open={Boolean(selected)}
        title={selected ? `جدول اقساط ${selected.orderNumber}` : "جدول اقساط"}
        description={selected ? `${selected.customerName} · ${selected.installments.filter((line) => !line.paidAt).length} قسط باقی‌مانده` : undefined}
        onClose={() => {
          setSelected(null);
          setPayLine(null);
          setError(null);
          if (queryValue(path, "open")) onNavigate("/accounting/installments");
        }}
      >
        {selected && (
          <div className="installment-schedule">
            <div className="table-scroll">
              <table className="data-table">
                <thead><tr><th>قسط</th><th>سررسید</th><th>مبلغ</th><th>وضعیت</th><th>پرداخت</th></tr></thead>
                <tbody>
                  {selected.installments.map((line, index) => {
                    const status = installmentStatus(line);
                    const firstUnpaidIndex = selected.installments.findIndex((item) => !item.paidAt);
                    const canPay = !line.paidAt && firstUnpaidIndex === index;
                    return (
                      <tr key={line.id}>
                        <td>{line.sequence}</td>
                        <td>{dateOnly(line.dueOn)}</td>
                        <td><strong>{formatMoney(line.amountRials)}</strong></td>
                        <td><StatusBadge status={status} /></td>
                        <td>
                          {line.paidAt ? (
                            <span className="finance-paid-date">{formatDate(line.paidAt)}</span>
                          ) : (
                            <button
                              className="icon-action icon-action--gold"
                              type="button"
                              title={canPay ? "ثبت پرداخت این قسط" : "ابتدا قسط قبلی را پرداخت کن"}
                              aria-label={`پرداخت قسط ${line.sequence}`}
                              disabled={!canPay}
                              onClick={() => {
                                setPayLine(line);
                                setReference("");
                                setError(null);
                              }}
                            >
                              <CircleDollarSign size={16} />
                            </button>
                          )}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>

            {selected.invoiceId && (
              <button className="primary-button finance-invoice-link" type="button" onClick={() => onNavigate(`/invoices?open=${selected.invoiceId}`)}>
                مشاهده فاکتور نهایی
              </button>
            )}
          </div>
        )}
      </Modal>

      <Modal
        open={Boolean(payLine)}
        title={payLine ? `پرداخت قسط ${payLine.sequence}` : "پرداخت قسط"}
        description={payLine ? `${dateOnly(payLine.dueOn)} · ${formatMoney(payLine.amountRials)}` : undefined}
        onClose={() => {
          setPayLine(null);
          setReference("");
          setError(null);
        }}
      >
        {payLine && (
          <form className="entity-form" onSubmit={(event) => void submitPayment(event)}>
            <FormField label="شماره پیگیری / توضیح" wide>
              <input value={reference} onChange={(event) => setReference(event.target.value)} maxLength={200} />
            </FormField>
            <InlineError message={error} />
            <FormActions
              saving={saving}
              submitLabel="ثبت پرداخت قسط"
              onCancel={() => {
                setPayLine(null);
                setReference("");
                setError(null);
              }}
            />
          </form>
        )}
      </Modal>
    </main>
  );
}

export function TrustFundsPage({ onNotice }: RouteProps) {
  const { data, request } = useOperations();
  const [snapshot, setSnapshot] = useState<TrustFundSnapshot>({ entries: [], balances: [] });
  const [open, setOpen] = useState(false);
  const [saving, setSaving] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = async () => {
    setLoading(true);
    setError(null);
    try {
      setSnapshot(await request<TrustFundSnapshot>("/api/v1/finance/trust-funds"));
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "دریافت وجوه امانی کامل نشد.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void load();
  }, [request]);

  const totalBalance = snapshot.balances.reduce((sum, item) => sum + item.balanceRials, 0);
  const balanceFor = (customerId: string) =>
    snapshot.balances.find((item) => item.customerId === customerId)?.balanceRials ?? 0;
  const customerName = (customerId: string) =>
    data.customers.find((customer) => customer.id === customerId)?.displayName || "مشتری";

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const customerId = String(form.get("customerId") || "");
    const entryType = String(form.get("entryType") || "");
    const amountTomans = Number(form.get("amountTomans") || 0);
    const reference = String(form.get("reference") || "").trim();

    if (!customerId || !["Deposit", "Release"].includes(entryType) || !Number.isSafeInteger(amountTomans) || amountTomans <= 0) {
      setError("مشتری، نوع عملیات و مبلغ معتبر را وارد کن.");
      return;
    }

    setSaving(true);
    setError(null);
    try {
      await request("/api/v1/finance/trust-funds/entries", {
        method: "POST",
        body: JSON.stringify({
          customerId,
          entryType,
          amountRials: tomansToRials(amountTomans),
          occurredAt: new Date().toISOString(),
          reference: reference || null,
        }),
      });
      setOpen(false);
      setSaving(false);
      onNotice(entryType === "Deposit" ? "وجه امانی مشتری ثبت شد." : "آزادسازی وجه امانی ثبت شد.");
      await load();
    } catch (caught) {
      setSaving(false);
      setError(caught instanceof Error ? caught.message : "ثبت عملیات وجه امانی کامل نشد.");
    }
  };

  return (
    <main className="module-main finance-ledger-page" dir="rtl">
      <PageHeader
        icon={Landmark}
        title="وجوه امانی"
        description="سپرده‌های مشتریان، تخصیص برای سفارش‌ها و آزادسازی مانده."
        actionLabel="ثبت وجه امانی"
        onAction={() => {
          setError(null);
          setOpen(true);
        }}
        secondary={
          <button className="secondary-button" type="button" disabled={loading} onClick={() => void load()}>
            <RefreshCw className={loading ? "spin" : ""} size={15} />
            به‌روزرسانی
          </button>
        }
      />

      <InlineError message={error} />

      <div className="module-metrics-grid">
        <article className="lux-card module-metric"><span>کل مانده امانی</span><strong>{formatMoney(totalBalance)}</strong><small>قابل تخصیص به سفارش‌ها</small></article>
        <article className="lux-card module-metric"><span>مشتری دارای مانده</span><strong>{snapshot.balances.filter((item) => item.balanceRials > 0).length}</strong><small>سپرده فعال</small></article>
        <article className="lux-card module-metric"><span>تراکنش دفتر</span><strong>{snapshot.entries.length}</strong><small>سپرده، تخصیص و آزادسازی</small></article>
      </div>

      {snapshot.balances.length ? (
        <div className="trust-balance-grid">
          {snapshot.balances
            .filter((item) => item.balanceRials !== 0)
            .map((item) => (
              <article className="lux-card trust-balance-card" key={item.customerId}>
                <span>{customerName(item.customerId)}</span>
                <strong>{formatMoney(item.balanceRials)}</strong>
                <small>مانده وجه امانی</small>
              </article>
            ))}
        </div>
      ) : null}

      {snapshot.entries.length ? (
        <TableCard>
          <div className="table-scroll">
            <table className="data-table">
              <thead><tr><th>تاریخ</th><th>مشتری</th><th>نوع</th><th>مبلغ</th><th>مانده فعلی</th><th>سفارش</th><th>پیگیری</th></tr></thead>
              <tbody>
                {snapshot.entries.map((entry) => (
                  <tr key={entry.id}>
                    <td>{formatDate(entry.occurredAt)}</td>
                    <td>{customerName(entry.customerId)}</td>
                    <td><span className="finance-neutral-badge">{entry.entryType === "Deposit" ? "سپرده" : entry.entryType === "Allocation" ? "تخصیص سفارش" : "آزادسازی"}</span></td>
                    <td><strong className="finance-neutral-amount">{formatMoney(entry.amountRials)}</strong></td>
                    <td>{formatMoney(balanceFor(entry.customerId))}</td>
                    <td>{entry.orderId ? (data.orders.find((order: Order) => order.id === entry.orderId)?.orderNumber || entry.orderId.slice(0, 8)) : "—"}</td>
                    <td>{entry.reference || "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </TableCard>
      ) : (
        <EmptyState title="وجه امانی ثبت نشده" description="سپرده اولیه مشتری را با دکمه بالا ثبت کن." />
      )}

      <Modal
        open={open}
        title="ثبت عملیات وجه امانی"
        description="سپرده مانده را افزایش می‌دهد؛ آزادسازی از مانده مشتری کم می‌کند."
        onClose={() => {
          setOpen(false);
          setError(null);
        }}
      >
        <form className="entity-form" onSubmit={(event) => void submit(event)}>
          <FormField label="مشتری">
            <select name="customerId" required>
              <option value="">انتخاب کن</option>
              {data.customers.filter((item) => item.isActive).map((customer) => (
                <option value={customer.id} key={customer.id}>{customer.displayName}</option>
              ))}
            </select>
          </FormField>
          <FormField label="نوع عملیات">
            <select name="entryType" defaultValue="Deposit" required>
              <option value="Deposit">سپرده جدید</option>
              <option value="Release">آزادسازی / بازپرداخت</option>
            </select>
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
            submitLabel="ثبت در دفتر امانی"
            onCancel={() => {
              setOpen(false);
              setError(null);
            }}
          />
        </form>
      </Modal>
    </main>
  );
}
