import { Clock3 } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { activeNumberLocale } from "../../lib/money";
import { useOperations } from "./OperationsContext";
import { formatMoney, TableCard } from "./PagePrimitives";
import type { InstallmentPlan } from "./FlexiblePaymentModals";

export function PendingInstallmentInvoices() {
  const { data, request } = useOperations();
  const [plans, setPlans] = useState<InstallmentPlan[]>([]);
  const [failed, setFailed] = useState(false);
  const english = activeNumberLocale() === "en-US";

  useEffect(() => {
    let cancelled = false;
    setFailed(false);

    void request<InstallmentPlan[]>("/api/v1/finance/installments")
      .then((result) => {
        if (!cancelled) setPlans(result);
      })
      .catch(() => {
        if (!cancelled) setFailed(true);
      });

    return () => {
      cancelled = true;
    };
  }, [data.invoices.length, request]);

  const pending = useMemo(
    () => plans.filter((plan) => !plan.invoiceId),
    [plans],
  );

  if (!pending.length && !failed) return null;

  return (
    <section className="pending-installment-invoices">
      <header className="pending-invoice-heading">
        <div>
          <h2>{english ? "Pending installment invoices" : "فاکتورهای اقساطی در انتظار صدور"}</h2>
          <p>
            {english
              ? "No printable invoice is created until the last installment is paid."
              : "تا پرداخت آخرین قسط، فاکتور قابل چاپ صادر نمی‌شود."}
          </p>
        </div>
      </header>

      {failed ? (
        <div className="inline-error">
          {english
            ? "Installment invoice status could not be loaded."
            : "وضعیت فاکتورهای اقساطی دریافت نشد."}
        </div>
      ) : (
        <TableCard>
          <div className="table-scroll">
            <table className="data-table">
              <thead>
                <tr>
                  <th>{english ? "Order" : "سفارش"}</th>
                  <th>{english ? "Customer" : "مشتری"}</th>
                  <th>{english ? "Total" : "مبلغ کل"}</th>
                  <th>{english ? "Paid installments" : "اقساط پرداخت‌شده"}</th>
                  <th>{english ? "Remaining" : "باقی‌مانده"}</th>
                  <th>{english ? "Invoice status" : "وضعیت فاکتور"}</th>
                </tr>
              </thead>
              <tbody>
                {pending.map((plan) => {
                  const paid = plan.installments.filter((line) => Boolean(line.paidAt)).length;
                  const remaining = plan.installments.length - paid;
                  return (
                    <tr key={plan.id}>
                      <td><strong>{plan.orderNumber}</strong></td>
                      <td>{plan.customerName}</td>
                      <td>{formatMoney(plan.totalAmountRials)}</td>
                      <td>{paid} / {plan.installments.length}</td>
                      <td>{remaining}</td>
                      <td>
                        <span className="pending-invoice-badge">
                          <Clock3 size={14} />
                          {english ? "Pending issuance" : "در انتظار صدور فاکتور"}
                        </span>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </TableCard>
      )}
    </section>
  );
}
