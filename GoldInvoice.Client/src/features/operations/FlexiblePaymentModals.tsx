import { CalendarDays, Landmark, WalletCards } from "lucide-react";
import { type FormEvent, useEffect, useMemo, useState } from "react";
import { rialsToTomans, tomansToRials } from "../../lib/money";
import type { Order } from "./operations.types";
import { FormActions, FormField, formatMoney, InlineError, Modal } from "./PagePrimitives";
import { useOperations } from "./OperationsContext";

export interface InstallmentLine {
  id: string;
  sequence: number;
  dueOn: string;
  amountRials: number;
  paidAt?: string | null;
  reference?: string | null;
}

export interface InstallmentPlan {
  id: string;
  orderId: string;
  customerId: string;
  orderNumber: string;
  customerName: string;
  totalAmountRials: number;
  createdAt: string;
  installments: InstallmentLine[];
  paymentId?: string | null;
  invoiceId?: string | null;
}

export interface TrustFundBalance {
  customerId: string;
  balanceRials: number;
}

export interface TrustFundAllocationResult {
  entryId: string;
  customerId: string;
  orderId: string;
  allocatedAmountRials: number;
  remainingBalanceRials: number;
  paymentId: string;
  invoiceId?: string | null;
}

interface DraftLine {
  dueOn: string;
  amountTomans: number;
}

function localDateValue(date: Date): string {
  const copy = new Date(date.getTime() - date.getTimezoneOffset() * 60_000);
  return copy.toISOString().slice(0, 10);
}

function defaultLines(totalRials: number, count: number): DraftLine[] {
  const totalTomans = rialsToTomans(totalRials);
  const base = Math.floor(totalTomans / count);
  let allocated = 0;

  return Array.from({ length: count }, (_, index) => {
    const amount = index === count - 1 ? totalTomans - allocated : base;
    allocated += amount;
    const due = new Date();
    due.setMonth(due.getMonth() + index);
    return {
      dueOn: localDateValue(due),
      amountTomans: amount,
    };
  });
}

export function InstallmentSetupModal({
  order,
  onClose,
  onCreated,
}: {
  order: Order | null;
  onClose: () => void;
  onCreated: (plan: InstallmentPlan) => void | Promise<void>;
}) {
  const { request } = useOperations();
  const [count, setCount] = useState(3);
  const [lines, setLines] = useState<DraftLine[]>([]);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!order) return;
    setCount(3);
    setLines(defaultLines(order.grandTotalRials, 3));
    setSaving(false);
    setError(null);
  }, [order]);

  const expectedTomans = order ? rialsToTomans(order.grandTotalRials) : 0;
  const enteredTomans = lines.reduce((sum, line) => sum + Number(line.amountTomans || 0), 0);

  const changeCount = (value: number) => {
    if (!order) return;
    const bounded = Math.max(1, Math.min(24, Math.floor(value || 1)));
    setCount(bounded);
    setLines(defaultLines(order.grandTotalRials, bounded));
    setError(null);
  };

  const updateLine = (index: number, patch: Partial<DraftLine>) => {
    setLines((current) => current.map((line, lineIndex) =>
      lineIndex === index ? { ...line, ...patch } : line,
    ));
    setError(null);
  };

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!order) return;

    if (lines.length !== count || lines.some((line) => !line.dueOn || line.amountTomans <= 0)) {
      setError("برای همه اقساط تاریخ و مبلغ معتبر وارد کن.");
      return;
    }

    if (enteredTomans !== expectedTomans) {
      setError(`جمع اقساط باید دقیقاً ${expectedTomans.toLocaleString("fa-IR")} تومان باشد.`);
      return;
    }

    setSaving(true);
    setError(null);
    try {
      const plan = await request<InstallmentPlan>("/api/v1/finance/installments", {
        method: "POST",
        body: JSON.stringify({
          orderId: order.id,
          installments: lines.map((line) => ({
            dueOn: line.dueOn,
            amountRials: tomansToRials(line.amountTomans),
          })),
        }),
      });
      await onCreated(plan);
    } catch (caught) {
      setSaving(false);
      setError(caught instanceof Error ? caught.message : "ثبت برنامه اقساط کامل نشد.");
    }
  };

  return (
    <Modal
      open={Boolean(order)}
      title="تنظیم جدول اقساط"
      description={order ? `سفارش ${order.orderNumber} · ${formatMoney(order.grandTotalRials)}` : undefined}
      onClose={onClose}
    >
      {order && (
        <form className="installment-setup" onSubmit={(event) => void submit(event)}>
          <div className="installment-summary">
            <WalletCards size={19} />
            <div>
              <span>مبلغ کل سفارش</span>
              <strong>{formatMoney(order.grandTotalRials)}</strong>
            </div>
            <label>
              <span>تعداد اقساط</span>
              <input
                type="number"
                min="1"
                max="24"
                value={count}
                onChange={(event) => changeCount(Number(event.target.value))}
              />
            </label>
          </div>

          <div className="installment-editor-table">
            <div className="installment-editor-head">
              <span>قسط</span><span>سررسید</span><span>مبلغ (تومان)</span>
            </div>
            {lines.map((line, index) => (
              <div className="installment-editor-row" key={index}>
                <strong>{index + 1}</strong>
                <label>
                  <CalendarDays size={14} />
                  <input
                    type="date"
                    value={line.dueOn}
                    onChange={(event) => updateLine(index, { dueOn: event.target.value })}
                    required
                  />
                </label>
                <input
                  type="number"
                  min="1"
                  step="1"
                  value={line.amountTomans || ""}
                  onChange={(event) => updateLine(index, { amountTomans: Number(event.target.value) })}
                  required
                />
              </div>
            ))}
          </div>

          <div className={`installment-total-check ${enteredTomans === expectedTomans ? "is-valid" : ""}`}>
            <span>جمع جدول</span>
            <strong>{enteredTomans.toLocaleString("fa-IR")} تومان</strong>
          </div>

          <InlineError message={error} />
          <FormActions saving={saving} submitLabel="ثبت جدول اقساط" onCancel={onClose} />
        </form>
      )}
    </Modal>
  );
}

export function TrustFundPaymentModal({
  order,
  onClose,
  onCompleted,
}: {
  order: Order | null;
  onClose: () => void;
  onCompleted: (result: TrustFundAllocationResult) => void | Promise<void>;
}) {
  const { request } = useOperations();
  const [balance, setBalance] = useState<TrustFundBalance | null>(null);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [reference, setReference] = useState("");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!order) {
      setBalance(null);
      return;
    }

    let cancelled = false;
    setLoading(true);
    setSaving(false);
    setReference("");
    setError(null);

    void request<TrustFundBalance>(`/api/v1/finance/trust-funds/customers/${order.customerId}`)
      .then((result) => {
        if (!cancelled) setBalance(result);
      })
      .catch((caught) => {
        if (!cancelled) setError(caught instanceof Error ? caught.message : "مانده وجه امانی دریافت نشد.");
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [order, request]);

  const enough = Boolean(order && balance && balance.balanceRials >= order.grandTotalRials);
  const after = order && balance
    ? balance.balanceRials - order.grandTotalRials
    : 0;

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!order || !enough) return;

    setSaving(true);
    setError(null);
    try {
      const result = await request<TrustFundAllocationResult>("/api/v1/finance/trust-funds/allocate", {
        method: "POST",
        body: JSON.stringify({
          orderId: order.id,
          reference: reference.trim() || null,
        }),
      });
      await onCompleted(result);
    } catch (caught) {
      setSaving(false);
      setError(caught instanceof Error ? caught.message : "تخصیص وجه امانی کامل نشد.");
    }
  };

  return (
    <Modal
      open={Boolean(order)}
      title="پرداخت از وجه امانی"
      description="مبلغ سفارش از مانده امانی مشتری تخصیص داده می‌شود."
      onClose={onClose}
    >
      {order && (
        <form className="trust-allocation-form" onSubmit={(event) => void submit(event)}>
          <div className="trust-allocation-summary">
            <Landmark size={20} />
            <div><span>مشتری</span><strong>{order.customerNameSnapshot || "مشتری"}</strong></div>
            <div><span>مانده امانی</span><strong>{loading ? "در حال دریافت…" : formatMoney(balance?.balanceRials ?? 0)}</strong></div>
            <div><span>مبلغ سفارش</span><strong>{formatMoney(order.grandTotalRials)}</strong></div>
            <div><span>مانده بعد از پرداخت</span><strong>{enough ? formatMoney(after) : "موجودی کافی نیست"}</strong></div>
          </div>

          <FormField label="شماره پیگیری / توضیح کوتاه" wide>
            <input
              value={reference}
              onChange={(event) => setReference(event.target.value)}
              maxLength={200}
              placeholder="اختیاری"
            />
          </FormField>

          {!loading && !enough && (
            <div className="inline-error">مانده وجه امانی مشتری برای این سفارش کافی نیست.</div>
          )}
          <InlineError message={error} />
          <FormActions saving={saving} submitLabel="تخصیص وجه امانی و صدور فاکتور" onCancel={onClose} />
        </form>
      )}
    </Modal>
  );
}
