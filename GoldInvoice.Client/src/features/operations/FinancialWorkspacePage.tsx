import { Coins, Landmark, RefreshCw, UserRound, Warehouse } from "lucide-react";
import { type FormEvent, useCallback, useEffect, useMemo, useState } from "react";
import { formatTomansFromRials, tomansToRials } from "../../lib/money";
import { useOperations } from "./OperationsContext";
import { EmptyState, FormField, PageHeader, TableCard } from "./PagePrimitives";
import "./FinancialWorkspacePage.css";

interface FinancialWorkspacePageProps {
  onNotice: (message: string) => void;
}

interface FinancialWorkspaceEntry {
  id: string;
  scope: "Warehouse" | "Houman" | "Ali";
  entryType: "Expense" | "Asset";
  occurredOn: string;
  amountRials: number;
  reason?: string | null;
}

interface FinancialWorkspaceResponse {
  entries: FinancialWorkspaceEntry[];
}

type PersonScope = "Houman" | "Ali";

function todayValue(): string {
  const now = new Date();
  const local = new Date(now.getTime() - now.getTimezoneOffset() * 60_000);
  return local.toISOString().slice(0, 10);
}

function displayDate(value: string): string {
  const parsed = new Date(`${value}T00:00:00`);
  return new Intl.DateTimeFormat("fa-IR-u-ca-persian", {
    year: "numeric",
    month: "short",
    day: "numeric",
  }).format(parsed);
}

function money(value: number): string {
  return formatTomansFromRials(value);
}

export function FinancialWorkspacePage({ onNotice }: FinancialWorkspacePageProps) {
  const { data, request } = useOperations();
  const [entries, setEntries] = useState<FinancialWorkspaceEntry[]>([]);
  const [loading, setLoading] = useState(true);
  const [savingScope, setSavingScope] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await request<FinancialWorkspaceResponse>(
        "/api/v1/settings/financial-workspace",
      );
      setEntries(response.entries);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "دریافت دفتر مالی کامل نشد.");
    } finally {
      setLoading(false);
    }
  }, [request]);

  useEffect(() => {
    void load();
  }, [load]);

  const warehouseEntries = useMemo(
    () => entries.filter((entry) => entry.scope === "Warehouse"),
    [entries],
  );
  const houmanEntries = useMemo(
    () => entries.filter((entry) => entry.scope === "Houman"),
    [entries],
  );
  const aliEntries = useMemo(
    () => entries.filter((entry) => entry.scope === "Ali"),
    [entries],
  );

  const inventoryQuantity = data.inventoryItems.reduce(
    (sum, item) => sum + item.quantityAvailable,
    0,
  );
  const inventoryCostValue = data.inventoryItems.reduce(
    (sum, item) => sum + (item.averageUnitCostRials * item.quantityAvailable),
    0,
  );
  const warehouseExpenseTotal = warehouseEntries.reduce(
    (sum, entry) => sum + entry.amountRials,
    0,
  );

  const personTotals = (scopeEntries: FinancialWorkspaceEntry[]) => {
    const assets = scopeEntries
      .filter((entry) => entry.entryType === "Asset")
      .reduce((sum, entry) => sum + entry.amountRials, 0);
    const expenses = scopeEntries
      .filter((entry) => entry.entryType === "Expense")
      .reduce((sum, entry) => sum + entry.amountRials, 0);
    return { assets, expenses, balance: assets - expenses };
  };

  const houmanTotals = personTotals(houmanEntries);
  const aliTotals = personTotals(aliEntries);

  const createWarehouseExpense = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const formElement = event.currentTarget;
    const form = new FormData(formElement);
    const amountTomans = Number(form.get("amountTomans") || 0);
    const reason = String(form.get("reason") || "").trim();
    const occurredOn = String(form.get("occurredOn") || "");

    if (!Number.isSafeInteger(amountTomans) || amountTomans <= 0 || !reason || !occurredOn) {
      setError("تاریخ، دلیل و مبلغ معتبر برای هزینه انبار وارد کن.");
      return;
    }

    setSavingScope("Warehouse");
    setError(null);
    try {
      const created = await request<FinancialWorkspaceEntry>(
        "/api/v1/settings/financial-workspace/entries",
        {
          method: "POST",
          body: JSON.stringify({
            scope: "Warehouse",
            entryType: "Expense",
            occurredOn,
            amountRials: tomansToRials(amountTomans),
            reason,
          }),
        },
      );
      setEntries((current) => [created, ...current.filter((item) => item.id !== created.id)]);
      formElement.reset();
      onNotice("هزینه انبار ثبت شد.");
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "ثبت هزینه انبار کامل نشد.");
    } finally {
      setSavingScope(null);
    }
  };

  const createPersonEntry = async (
    event: FormEvent<HTMLFormElement>,
    scope: PersonScope,
  ) => {
    event.preventDefault();
    const formElement = event.currentTarget;
    const form = new FormData(formElement);
    const amountTomans = Number(form.get("amountTomans") || 0);
    const entryType = String(form.get("entryType") || "");
    const occurredOn = String(form.get("occurredOn") || "");

    if (
      !Number.isSafeInteger(amountTomans) ||
      amountTomans <= 0 ||
      !["Asset", "Expense"].includes(entryType) ||
      !occurredOn
    ) {
      setError("تاریخ، نوع و مبلغ معتبر وارد کن.");
      return;
    }

    setSavingScope(scope);
    setError(null);
    try {
      const created = await request<FinancialWorkspaceEntry>(
        "/api/v1/settings/financial-workspace/entries",
        {
          method: "POST",
          body: JSON.stringify({
            scope,
            entryType,
            occurredOn,
            amountRials: tomansToRials(amountTomans),
            reason: null,
          }),
        },
      );
      setEntries((current) => [created, ...current.filter((item) => item.id !== created.id)]);
      formElement.reset();
      onNotice(scope === "Houman" ? "رکورد هومن ثبت شد." : "رکورد علی ثبت شد.");
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "ثبت رکورد مالی کامل نشد.");
    } finally {
      setSavingScope(null);
    }
  };

  const renderPersonSection = (
    scope: PersonScope,
    title: string,
    scopeEntries: FinancialWorkspaceEntry[],
    totals: ReturnType<typeof personTotals>,
  ) => (
    <section className="financial-person-section lux-card">
      <header className="financial-section-heading">
        <div className="financial-section-icon"><UserRound size={20} /></div>
        <div>
          <h2>{title}</h2>
          <p>دارایی‌ها و هزینه‌های ثبت‌شده بدون فیلد دلیل.</p>
        </div>
      </header>

      <div className="financial-mini-metrics">
        <div><span>دارایی ثبت‌شده</span><strong>{money(totals.assets)}</strong></div>
        <div><span>هزینه ثبت‌شده</span><strong className="financial-person-expense">{money(totals.expenses)}</strong></div>
        <div><span>مانده</span><strong>{money(totals.balance)}</strong></div>
      </div>

      <form className="entity-form financial-entry-form" onSubmit={(event) => void createPersonEntry(event, scope)}>
        <FormField label="تاریخ">
          <input name="occurredOn" type="date" defaultValue={todayValue()} required />
        </FormField>
        <FormField label="نوع">
          <select name="entryType" defaultValue="Asset" required>
            <option value="Asset">دارایی</option>
            <option value="Expense">هزینه</option>
          </select>
        </FormField>
        <FormField label="مبلغ (تومان)">
          <input name="amountTomans" type="number" min="1" step="1" required />
        </FormField>
        <div className="financial-form-submit">
          <button className="primary-button" type="submit" disabled={savingScope === scope}>
            {savingScope === scope ? <><RefreshCw className="spin" size={15} /> در حال ثبت…</> : "ثبت رکورد"}
          </button>
        </div>
      </form>

      {scopeEntries.length ? (
        <TableCard>
          <div className="table-scroll">
            <table className="data-table financial-person-table">
              <thead><tr><th>تاریخ</th><th>نوع</th><th>مبلغ</th></tr></thead>
              <tbody>
                {scopeEntries.map((entry) => (
                  <tr key={entry.id}>
                    <td>{displayDate(entry.occurredOn)}</td>
                    <td><span className="financial-type-badge">{entry.entryType === "Asset" ? "دارایی" : "هزینه"}</span></td>
                    <td><strong className={entry.entryType === "Expense" ? "financial-person-expense" : "financial-neutral-amount"}>{money(entry.amountRials)}</strong></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </TableCard>
      ) : (
        <EmptyState title={`رکوردی برای ${title} ثبت نشده`} description="اولین دارایی یا هزینه را از فرم بالا ثبت کن." />
      )}
    </section>
  );

  return (
    <main className="module-main financial-workspace-page" dir="rtl">
      <PageHeader
        icon={Landmark}
        title="دارایی‌ها و هزینه‌ها"
        description="نمای یکپارچه موجودی انبار و دفتر مالی هومن و علی."
        secondary={
          <button className="secondary-button" type="button" onClick={() => void load()} disabled={loading}>
            <RefreshCw className={loading ? "spin" : ""} size={16} />
            به‌روزرسانی
          </button>
        }
      />

      {error && <div className="inline-error" role="alert">{error}</div>}

      <section className="financial-warehouse-section lux-card">
        <header className="financial-section-heading">
          <div className="financial-section-icon"><Warehouse size={20} /></div>
          <div>
            <h2>انبار</h2>
            <p>موجودی واقعی انبار از داده‌های Inventory و هزینه‌های ثبت‌شده برای انبار.</p>
          </div>
        </header>

        <div className="financial-overview-grid">
          <article>
            <span>موجودی قابل فروش</span>
            <strong>{new Intl.NumberFormat("fa-IR").format(inventoryQuantity)} قطعه</strong>
          </article>
          <article>
            <span>ارزش بهای موجودی</span>
            <strong>{money(inventoryCostValue)}</strong>
          </article>
          <article>
            <span>جمع هزینه‌های انبار</span>
            <strong className="financial-warehouse-expense">{money(warehouseExpenseTotal)}</strong>
          </article>
        </div>

        <form className="entity-form financial-entry-form" onSubmit={(event) => void createWarehouseExpense(event)}>
          <FormField label="تاریخ">
            <input name="occurredOn" type="date" defaultValue={todayValue()} required />
          </FormField>
          <FormField label="مبلغ (تومان)">
            <input name="amountTomans" type="number" min="1" step="1" required />
          </FormField>
          <FormField label="دلیل هزینه" wide>
            <input name="reason" maxLength={500} required />
          </FormField>
          <div className="financial-form-submit">
            <button className="primary-button" type="submit" disabled={savingScope === "Warehouse"}>
              {savingScope === "Warehouse" ? <><RefreshCw className="spin" size={15} /> در حال ثبت…</> : "ثبت هزینه انبار"}
            </button>
          </div>
        </form>

        {warehouseEntries.length ? (
          <TableCard>
            <div className="table-scroll">
              <table className="data-table">
                <thead><tr><th>تاریخ</th><th>دلیل</th><th>مبلغ</th></tr></thead>
                <tbody>
                  {warehouseEntries.map((entry) => (
                    <tr key={entry.id}>
                      <td>{displayDate(entry.occurredOn)}</td>
                      <td>{entry.reason || "—"}</td>
                      <td><strong className="financial-warehouse-expense">{money(entry.amountRials)}</strong></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </TableCard>
        ) : (
          <EmptyState title="هزینه‌ای برای انبار ثبت نشده" description="هزینه‌های انبار با تاریخ، دلیل و مبلغ اینجا نمایش داده می‌شوند." />
        )}
      </section>

      <div className="financial-people-grid">
        {renderPersonSection("Houman", "هومن", houmanEntries, houmanTotals)}
        {renderPersonSection("Ali", "علی", aliEntries, aliTotals)}
      </div>

      <footer className="financial-workspace-note">
        <Coins size={16} />
        <span>مبالغ هومن و علی عمداً با رنگ خنثی رابط وندوم نمایش داده می‌شوند؛ فقط هزینه‌های انبار قرمز هستند.</span>
      </footer>
    </main>
  );
}
