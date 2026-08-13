import {
  BarChart3,
  Boxes,
  Calculator,
  Check,
  ChevronLeft,
  Download,
  Eye,
  FileText,
  Gem,
  HeartHandshake,
  IdCard,
  ImagePlus,
  KeyRound,
  Mail,
  MapPin,
  Monitor,
  PackagePlus,
  Pencil,
  Phone,
  Printer,
  Search,
  Settings,
  ShieldCheck,
  ShoppingBag,
  Trash2,
  Truck,
  UserRound,
  Users,
} from "lucide-react";
import {
  type FormEvent,
  type ReactNode,
  useEffect,
  useMemo,
  useState,
} from "react";
import { useAuthentication } from "../auth/AuthContext";
import { buildInvoiceDocumentHtml, invoiceFileName } from "../invoices/invoiceDocument";
import { DesktopBridgeError, isDesktopHost, sendDesktopCommand } from "../../platform/desktopBridge";
import { createIdempotencyKey, OperationalApiError } from "./operationsApi";
import { useOperations } from "./OperationsContext";
import {
  EmptyState,
  FormActions,
  FormField,
  formatDate,
  formatRials,
  InlineError,
  LoadingState,
  MetricTile,
  Modal,
  PageHeader,
  RefreshButton,
  StatusBadge,
  TableCard,
  translateStatus,
} from "./PagePrimitives";
import type {
  CustomerAddress,
  CustomerInteraction,
  Invoice,
  InvoicePrintJob,
  Order,
  Payment,
  Person,
  Product,
  ProductVariant,
  StoreProfile,
  Supplier,
  SupplierPurchase,
  Warehouse,
} from "./operations.types";

interface RouteProps {
  path: string;
  onNavigate: (path: string) => void;
  onNotice: (message: string) => void;
}

interface FormState {
  saving: boolean;
  error: string | null;
}

const initialFormState: FormState = { saving: false, error: null };

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : "عملیات کامل نشد.";
}

function numberValue(form: FormData, name: string): number {
  return Number(form.get(name) || 0);
}

function textValue(form: FormData, name: string): string {
  return String(form.get(name) || "").trim();
}

function optionalText(form: FormData, name: string): string | null {
  return textValue(form, name) || null;
}

function invoiceActualProfit(invoice: Invoice): number {
  const knownItems = invoice.items.filter((item) => item.grossProfitRials != null);
  if (!knownItems.length) return 0;
  const knownRevenue = knownItems.reduce((sum, item) => sum + item.lineTotalRials, 0);
  const allocatedDiscount = invoice.subtotalRials > 0
    ? invoice.discountRials * (knownRevenue / invoice.subtotalRials)
    : 0;
  return knownItems.reduce((sum, item) => sum + (item.grossProfitRials ?? 0), 0) - allocatedDiscount;
}

function queryFlag(path: string, name: string): boolean {
  const query = path.split("?")[1] || "";
  return new URLSearchParams(query).get(name) === "1";
}

function queryValue(path: string, name: string): string {
  const query = path.split("?")[1] || "";
  return new URLSearchParams(query).get(name) || "";
}

function ModuleBody({ children }: { children: ReactNode }) {
  const { loading } = useOperations();
  if (loading) return <main className="module-main" dir="rtl"><LoadingState /></main>;
  return <main className="module-main" dir="rtl">{children}</main>;
}

function DataNotice() {
  const { error } = useOperations();
  return error ? (
    <div className="data-warning" role="status">
      بخشی از اطلاعات دریافت نشد: {error}
    </div>
  ) : null;
}

interface InvoiceDesktopResult {
  opened: boolean;
  saved: boolean;
  fileName?: string | null;
  printed?: boolean;
  printerName?: string | null;
  failureCode?: string | null;
}

function InvoicesPage({ path, onNavigate, onNotice }: RouteProps) {
  const { data, request, refresh, refreshing } = useOperations();
  const { user } = useAuthentication();
  const [selected, setSelected] = useState<Invoice | null>(null);
  const [editing, setEditing] = useState<Invoice | null>(null);
  const [printTarget, setPrintTarget] = useState<Invoice | null>(null);
  const [busyAction, setBusyAction] = useState<string | null>(null);
  const [formState, setFormState] = useState<FormState>(initialFormState);
  const total = data.invoices.reduce((sum, invoice) => sum + invoice.grandTotalRials, 0);
  const active = data.invoices.filter((invoice) => invoice.status !== "Voided");
  const canCorrect = user?.permissions.includes("Orders.Manage") === true;
  const canPrint = user?.permissions.includes("Invoices.Print") === true;

  useEffect(() => {
    const requestedInvoiceId = queryValue(path, "open");
    if (!requestedInvoiceId) return;
    const invoice = data.invoices.find((item) => item.id === requestedInvoiceId);
    if (invoice) setSelected(invoice);
  }, [data.invoices, path]);

  const closePreview = () => {
    setSelected(null);
    if (queryValue(path, "open")) onNavigate("/invoices");
  };

  const openDesktopDocument = async (
    invoice: Invoice,
    action: "preview" | "save" | "print",
    copies = 1,
  ) => {
    if (!isDesktopHost()) {
      if (action === "print") {
        const preview = window.open("", "_blank", "noopener,noreferrer");
        if (!preview) throw new Error("مرورگر اجازه بازکردن پیش‌نمایش چاپ را نداد.");
        preview.document.open();
        preview.document.write(buildInvoiceDocumentHtml(invoice));
        preview.document.close();
        preview.focus();
        preview.print();
        return { opened: true, saved: false } satisfies InvoiceDesktopResult;
      }
      throw new Error("دانلود PDF از داخل نسخه دسکتاپ وندوم انجام می‌شود.");
    }

    return sendDesktopCommand<InvoiceDesktopResult>("invoice.document", {
      action,
      html: buildInvoiceDocumentHtml(invoice),
      suggestedFileName: invoiceFileName(invoice),
      copies,
    }, 120_000);
  };

  const savePdf = async (invoice: Invoice) => {
    setBusyAction(`save:${invoice.id}`);
    try {
      const result = await openDesktopDocument(invoice, "save");
      if (result.saved) onNotice(`فایل ${result.fileName || invoiceFileName(invoice)} ذخیره شد.`);
    } catch (error) {
      onNotice(messageOf(error));
    } finally {
      setBusyAction(null);
    }
  };

  const submitCorrection = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!editing?.address) return;
    const form = new FormData(event.currentTarget);
    setFormState({ saving: true, error: null });
    try {
      const corrected = await request<Invoice>(`/api/v1/invoices/${editing.id}/document`, {
        method: "PUT",
        body: JSON.stringify({
          customerName: textValue(form, "customerName"),
          customerNationalId: optionalText(form, "customerNationalId"),
          recipientName: textValue(form, "recipientName"),
          phoneNumber: textValue(form, "phoneNumber"),
          province: textValue(form, "province"),
          city: textValue(form, "city"),
          postalCode: textValue(form, "postalCode"),
          addressLine: textValue(form, "addressLine"),
          reason: textValue(form, "reason"),
          rowVersion: editing.rowVersion,
        }),
      });
      setEditing(null);
      setSelected((current) => current?.id === corrected.id ? corrected : current);
      setFormState(initialFormState);
      await refresh();
      onNotice("اصلاح اطلاعات چاپی فاکتور با سابقه حسابرسی ثبت شد.");
    } catch (error) {
      setFormState({ saving: false, error: messageOf(error) });
    }
  };

  const submitPrint = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!printTarget) return;
    if (!isDesktopHost()) {
      setFormState({ saving: false, error: "چاپ مستقیم فقط داخل برنامه دسکتاپ وندوم در دسترس است." });
      return;
    }
    const form = new FormData(event.currentTarget);
    setFormState({ saving: true, error: null });
    let printJob: InvoicePrintJob | null = null;
    try {
      const copies = numberValue(form, "copies");
      printJob = await request<InvoicePrintJob>(`/api/v1/invoices/${printTarget.id}/print-jobs`, {
        method: "POST",
        body: JSON.stringify({
          copies,
          reprintReason: textValue(form, "reason"),
        }),
      });
      const result = await openDesktopDocument(printTarget, "print", copies);
      await request<InvoicePrintJob>(`/api/v1/invoices/${printTarget.id}/print-jobs/${printJob.id}/complete`, {
        method: "POST",
        body: JSON.stringify({
          succeeded: result.printed === true,
          printerName: result.printerName || null,
          failureCode: result.printed ? null : result.failureCode || "PRINT_FAILED",
          rowVersion: printJob.rowVersion,
        }),
      });
      if (!result.printed) {
        setFormState({
          saving: false,
          error: result.failureCode === "PRINTER_UNAVAILABLE"
            ? "چاپگر پیش‌فرض ویندوز در دسترس نیست؛ چاپگر را در تنظیمات Windows فعال یا پیش‌فرض کن."
            : "چاپ فاکتور کامل نشد.",
        });
        return;
      }
      setPrintTarget(null);
      setFormState(initialFormState);
      onNotice(printJob.isReprint ? "چاپ مجدد فاکتور با موفقیت ثبت شد." : "فاکتور با موفقیت چاپ شد.");
    } catch (error) {
      if (printJob) {
        try {
          await request<InvoicePrintJob>(`/api/v1/invoices/${printTarget.id}/print-jobs/${printJob.id}/complete`, {
            method: "POST",
            body: JSON.stringify({
              succeeded: false,
              printerName: null,
              failureCode: error instanceof DesktopBridgeError
                ? error.code.slice(0, 100).toUpperCase()
                : "DESKTOP_PRINT_FAILED",
              rowVersion: printJob.rowVersion,
            }),
          });
        } catch {
          // The original print error remains the actionable message.
        }
      }
      setFormState({ saving: false, error: messageOf(error) });
    }
  };

  return (
    <ModuleBody>
      <PageHeader
        icon={FileText}
        title="فاکتورها"
        description="فاکتورهای صادرشده مستقیماً از سرویس صدور فاکتور خوانده می‌شوند."
        actionLabel="فاکتور جدید"
        onAction={() => onNavigate("/orders/new")}
        secondary={<RefreshButton refreshing={refreshing} onClick={() => void refresh()} />}
      />
      <DataNotice />
      <div className="module-metrics-grid">
        <MetricTile label="کل فاکتورها" value={String(data.invoices.length)} hint="رکورد دیتابیس" />
        <MetricTile label="فاکتور معتبر" value={String(active.length)} hint="غیرباطل" />
        <MetricTile label="مبلغ کل" value={formatRials(total)} hint="همه دوره‌ها" />
      </div>
      {data.invoices.length ? (
        <TableCard>
          <div className="table-scroll">
            <table className="data-table">
              <thead><tr><th>شماره</th><th>مشتری</th><th>تاریخ صدور</th><th>مبلغ</th><th>وضعیت</th><th /></tr></thead>
              <tbody>{data.invoices.map((invoice) => (
                <tr key={invoice.id}>
                  <td className="numeric-cell">{invoice.invoiceNumber}</td>
                  <td>{invoice.customerNameSnapshot || "مشتری ثبت‌شده"}</td>
                  <td>{formatDate(invoice.issuedAt)}</td>
                  <td>{formatRials(invoice.grandTotalRials)}</td>
                  <td><StatusBadge status={invoice.status} /></td>
                  <td><div className="icon-action-group">
                    <button className="icon-action icon-action--gold" type="button" title="مشاهده فاکتور" aria-label={`مشاهده فاکتور ${invoice.invoiceNumber}`} onClick={() => setSelected(invoice)}><Eye size={16} /></button>
                    <button className="icon-action" type="button" title="ویرایش اطلاعات چاپی" aria-label={`ویرایش فاکتور ${invoice.invoiceNumber}`} disabled={invoice.status !== "Issued" || !canCorrect} onClick={() => { setFormState(initialFormState); setEditing(invoice); }}><Pencil size={15} /></button>
                    <button className="icon-action" type="button" title="دانلود PDF" aria-label={`دانلود فاکتور ${invoice.invoiceNumber}`} disabled={busyAction === `save:${invoice.id}`} onClick={() => void savePdf(invoice)}><Download size={15} /></button>
                    <button className="icon-action" type="button" title="چاپ فاکتور" aria-label={`چاپ فاکتور ${invoice.invoiceNumber}`} disabled={invoice.status !== "Issued" || !canPrint} onClick={() => { setFormState(initialFormState); setPrintTarget(invoice); }}><Printer size={15} /></button>
                  </div></td>
                </tr>
              ))}</tbody>
            </table>
          </div>
        </TableCard>
      ) : <EmptyState title="هنوز فاکتوری صادر نشده" description="از «فاکتور جدید» سفارش را بساز و پس از ثبت پرداخت، فاکتور خودکار صادر می‌شود." />}
      <Modal open={Boolean(selected)} title={`فاکتور ${selected?.invoiceNumber || ""}`} description="پیش‌نمایش نسخه آمادهٔ PDF و چاپ" onClose={closePreview}>
        {selected && <InvoiceDetails invoice={selected} onEdit={() => { setFormState(initialFormState); setEditing(selected); setSelected(null); }} onDownload={() => void savePdf(selected)} onPrint={() => { setFormState(initialFormState); setPrintTarget(selected); setSelected(null); }} busy={busyAction === `save:${selected.id}`} canCorrect={canCorrect} canPrint={canPrint} />}
      </Modal>
      <Modal open={Boolean(editing)} title={`اصلاح فاکتور ${editing?.invoiceNumber || ""}`} description="مبالغ و اقلام قفل هستند؛ فقط اطلاعات چاپی با ثبت دلیل اصلاح می‌شوند." onClose={() => { setEditing(null); setFormState(initialFormState); }}>
        {editing?.address && <form className="entity-form" onSubmit={submitCorrection}>
          <FormField label="نام مشتری"><input name="customerName" defaultValue={editing.customerNameSnapshot || ""} required maxLength={200} /></FormField>
          <FormField label="شناسه ملی"><input name="customerNationalId" dir="ltr" defaultValue={editing.customerNationalIdSnapshot || ""} maxLength={32} /></FormField>
          <FormField label="نام تحویل‌گیرنده"><input name="recipientName" defaultValue={editing.address.recipientName} required maxLength={200} /></FormField>
          <FormField label="شماره تلفن"><input name="phoneNumber" type="tel" dir="ltr" defaultValue={editing.address.phoneNumber} required maxLength={32} /></FormField>
          <FormField label="استان"><input name="province" defaultValue={editing.address.province} required maxLength={100} /></FormField>
          <FormField label="شهر"><input name="city" defaultValue={editing.address.city} required maxLength={100} /></FormField>
          <FormField label="کد پستی"><input name="postalCode" dir="ltr" defaultValue={editing.address.postalCode} required maxLength={20} /></FormField>
          <FormField label="نشانی کامل" wide><textarea name="addressLine" defaultValue={editing.address.addressLine} required maxLength={1000} /></FormField>
          <FormField label="دلیل اصلاح" hint="این دلیل در سابقه حسابرسی باقی می‌ماند" wide><textarea name="reason" required minLength={3} maxLength={1000} /></FormField>
          <InlineError message={formState.error} />
          <FormActions saving={formState.saving} submitLabel="ثبت اصلاح فاکتور" onCancel={() => { setEditing(null); setFormState(initialFormState); }} />
        </form>}
      </Modal>
      <Modal open={Boolean(printTarget)} title={`چاپ ${printTarget?.invoiceNumber || "فاکتور"}`} description="فاکتور مستقیم به چاپگر پیش‌فرض ویندوز فرستاده می‌شود و نتیجه در سابقه چاپ ثبت خواهد شد." onClose={() => { setPrintTarget(null); setFormState(initialFormState); }}>
        <form className="entity-form" onSubmit={submitPrint}>
          <FormField label="تعداد نسخه"><input name="copies" type="number" min="1" max="20" defaultValue="1" required /></FormField>
          <FormField label="دلیل چاپ / چاپ مجدد" wide><textarea name="reason" defaultValue="تحویل فاکتور به مشتری" required minLength={3} maxLength={1000} /></FormField>
          <InlineError message={formState.error} />
          <FormActions saving={formState.saving} submitLabel="چاپ مستقیم فاکتور" onCancel={() => { setPrintTarget(null); setFormState(initialFormState); }} />
        </form>
      </Modal>
    </ModuleBody>
  );
}

function InvoiceDetails({
  invoice,
  onEdit,
  onDownload,
  onPrint,
  busy,
  canCorrect,
  canPrint,
}: {
  invoice: Invoice;
  onEdit: () => void;
  onDownload: () => void;
  onPrint: () => void;
  busy: boolean;
  canCorrect: boolean;
  canPrint: boolean;
}) {
  return (
    <div className="invoice-preview-panel">
      <div className="invoice-preview-actions">
        <button className="secondary-button" type="button" onClick={onEdit} disabled={invoice.status !== "Issued" || !canCorrect}><Pencil size={15} /> ویرایش اطلاعات</button>
        <button className="secondary-button" type="button" onClick={onDownload} disabled={busy}><Download size={15} /> {busy ? "در حال ساخت…" : "دانلود PDF"}</button>
        <button className="primary-button" type="button" onClick={onPrint} disabled={invoice.status !== "Issued" || !canPrint}><Printer size={15} /> چاپ فاکتور</button>
      </div>
      <iframe className="invoice-preview-frame" title={`پیش‌نمایش فاکتور ${invoice.invoiceNumber}`} srcDoc={buildInvoiceDocumentHtml(invoice)} sandbox="" />
    </div>
  );
}

type AddressMode = "create" | "view" | "edit" | "delete" | null;

function CustomersPage({ path, onNavigate, onNotice }: RouteProps) {
  const { data, request, refresh, refreshing } = useOperations();
  const [createOpen, setCreateOpen] = useState(queryFlag(path, "new"));
  const [addressCustomer, setAddressCustomer] = useState<Person | null>(null);
  const [addressMode, setAddressMode] = useState<AddressMode>(null);
  const [addresses, setAddresses] = useState<CustomerAddress[]>([]);
  const [selectedAddress, setSelectedAddress] = useState<CustomerAddress | null>(null);
  const [addressLoading, setAddressLoading] = useState(false);
  const [addressCounts, setAddressCounts] = useState<Record<string, number>>({});
  const [formState, setFormState] = useState<FormState>(initialFormState);

  useEffect(() => {
    setAddressCounts(Object.fromEntries(
      data.customers.map((customer) => [customer.id, customer.addressCount]),
    ));
  }, [data.customers]);

  const closeCreate = () => {
    setFormState(initialFormState);
    setCreateOpen(false);
    if (queryFlag(path, "new")) onNavigate("/customers");
  };
  const closeAddress = () => {
    setAddressCustomer(null);
    setAddressMode(null);
    setAddresses([]);
    setSelectedAddress(null);
    setAddressLoading(false);
    setFormState(initialFormState);
  };
  const openNewAddress = (customer: Person) => {
    setAddressCustomer(customer);
    setAddressMode("create");
    setAddresses([]);
    setSelectedAddress(null);
    setFormState(initialFormState);
  };
  const loadAddresses = async (customer: Person, mode: Exclude<AddressMode, "create" | null>) => {
    setAddressCustomer(customer);
    setAddressMode(mode);
    setAddressLoading(true);
    setAddresses([]);
    setSelectedAddress(null);
    setFormState(initialFormState);
    try {
      const loaded = await request<CustomerAddress[]>(`/api/v1/customers/${customer.id}/addresses`);
      const primary = loaded.find((item) => item.isDefault) || loaded[0] || null;
      setAddresses(loaded);
      setSelectedAddress(primary);
      setAddressCounts((current) => ({ ...current, [customer.id]: loaded.length }));
      if (!primary) {
        setAddressMode("create");
        onNotice("نشانی فعالی برای این مشتری پیدا نشد؛ یک نشانی جدید ثبت کن.");
      }
    } catch (error) {
      setFormState({ saving: false, error: messageOf(error) });
    } finally {
      setAddressLoading(false);
    }
  };
  const submitCustomer = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    setFormState({ saving: true, error: null });
    try {
      await request<Person>("/api/v1/people/customers", {
        method: "POST",
        body: JSON.stringify({
          displayName: textValue(form, "displayName"),
          phoneNumber: textValue(form, "phoneNumber"),
          temporaryPassword: textValue(form, "temporaryPassword"),
        }),
      });
      await refresh();
      onNotice("مشتری جدید با شماره تلفن ثبت شد.");
      closeCreate();
    } catch (error) {
      setFormState({ saving: false, error: messageOf(error) });
    }
  };
  const submitAddress = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!addressCustomer) return;
    const form = new FormData(event.currentTarget);
    const editing = addressMode === "edit" && selectedAddress;
    setFormState({ saving: true, error: null });
    try {
      const payload = {
        title: textValue(form, "title"),
        recipientName: textValue(form, "recipientName"),
        phoneNumber: textValue(form, "phoneNumber"),
        province: textValue(form, "province"),
        city: textValue(form, "city"),
        postalCode: textValue(form, "postalCode"),
        addressLine: textValue(form, "addressLine"),
        isDefault: form.get("isDefault") === "on",
        ...(editing ? { rowVersion: editing.rowVersion } : {}),
      };
      await request<CustomerAddress>(
        editing
          ? `/api/v1/customers/${addressCustomer.id}/addresses/${editing.id}`
          : `/api/v1/customers/${addressCustomer.id}/addresses`,
        { method: editing ? "PUT" : "POST", body: JSON.stringify(payload) },
      );
      if (!editing) {
        setAddressCounts((current) => ({
          ...current,
          [addressCustomer.id]: (current[addressCustomer.id] ?? addressCustomer.addressCount) + 1,
        }));
      }
      await refresh();
      onNotice(editing ? "نشانی مشتری ویرایش شد." : "نشانی مشتری ثبت شد.");
      closeAddress();
    } catch (error) {
      setFormState({ saving: false, error: messageOf(error) });
    }
  };
  const deleteAddress = async () => {
    if (!addressCustomer || !selectedAddress) return;
    setFormState({ saving: true, error: null });
    try {
      await request<void>(
        `/api/v1/customers/${addressCustomer.id}/addresses/${selectedAddress.id}?rowVersion=${encodeURIComponent(selectedAddress.rowVersion)}`,
        { method: "DELETE" },
      );
      setAddressCounts((current) => ({
        ...current,
        [addressCustomer.id]: Math.max(
          0,
          (current[addressCustomer.id] ?? addressCustomer.addressCount) - 1,
        ),
      }));
      await refresh();
      onNotice("نشانی مشتری حذف شد.");
      closeAddress();
    } catch (error) {
      setFormState({ saving: false, error: messageOf(error) });
    }
  };
  const retryAddressLoad = () => {
    if (addressCustomer && addressMode && addressMode !== "create") {
      void loadAddresses(addressCustomer, addressMode);
    }
  };
  const addressModalOpen = Boolean(addressCustomer && addressMode);
  const addressModalTitle = addressMode === "view"
    ? `مشاهده نشانی‌های ${addressCustomer?.displayName || "مشتری"}`
    : addressMode === "edit"
      ? `ویرایش نشانی ${addressCustomer?.displayName || "مشتری"}`
      : addressMode === "delete"
        ? "حذف نشانی"
        : `ثبت نشانی ${addressCustomer?.displayName || "مشتری"}`;

  return (
    <ModuleBody>
      <PageHeader icon={Users} title="مشتریان" description="اطلاعات تماس، نشانی و سابقه سفارش مشتریان در یک نمای واحد."
        actionLabel="مشتری جدید" onAction={() => { setFormState(initialFormState); setCreateOpen(true); }} secondary={<RefreshButton refreshing={refreshing} onClick={() => void refresh()} />} />
      <DataNotice />
      <div className="module-metrics-grid">
        <MetricTile label="مشتریان فعال" value={String(data.customers.filter((item) => item.isActive).length)} hint="حساب قابل استفاده" />
        <MetricTile label="دارای سفارش" value={String(data.customers.filter((item) => item.orderCount > 0).length)} hint="سابقه خرید" />
        <MetricTile label="کل فاکتورها" value={String(data.customers.reduce((sum, item) => sum + item.invoiceCount, 0))} hint="متصل به مشتری" />
      </div>
      {data.customers.length ? (
        <TableCard><div className="table-scroll"><table className="data-table"><thead><tr><th>نام</th><th>شماره تلفن</th><th>سفارش</th><th>فاکتور</th><th>آخرین فعالیت</th><th>نشانی</th><th>سفارش جدید</th></tr></thead><tbody>
          {data.customers.map((customer) => {
            const addressCount = addressCounts[customer.id] ?? customer.addressCount;
            return <tr key={customer.id}>
              <td><strong>{customer.displayName}</strong></td>
              <td className="numeric-cell">{customer.phoneNumber || "—"}</td>
              <td>{customer.orderCount}</td>
              <td>{customer.invoiceCount}</td>
              <td>{formatDate(customer.lastActivityAt)}</td>
              <td>{addressCount > 0 ? (
                <div className="icon-action-group">
                  <button className="icon-action" type="button" title="مشاهده نشانی" aria-label={`مشاهده نشانی ${customer.displayName}`} onClick={() => void loadAddresses(customer, "view")}><Eye size={16} /></button>
                  <button className="icon-action" type="button" title="ویرایش نشانی" aria-label={`ویرایش نشانی ${customer.displayName}`} onClick={() => void loadAddresses(customer, "edit")}><Pencil size={15} /></button>
                  <button className="icon-action icon-action--danger" type="button" title="حذف نشانی" aria-label={`حذف نشانی ${customer.displayName}`} onClick={() => void loadAddresses(customer, "delete")}><Trash2 size={15} /></button>
                </div>
              ) : <button className="row-action" type="button" onClick={() => openNewAddress(customer)}><MapPin size={15} /> ثبت نشانی</button>}</td>
              <td><button className="icon-action icon-action--gold" type="button" title="ثبت سفارش" aria-label={`ثبت سفارش برای ${customer.displayName}`} onClick={() => onNavigate(`/orders/new?customerId=${customer.id}`)}><ShoppingBag size={16} /></button></td>
            </tr>;
          })}
        </tbody></table></div></TableCard>
      ) : <EmptyState title="مشتری ثبت نشده" description="اولین مشتری را با دکمه بالا ثبت کن؛ دادهٔ نمونه نمایش داده نمی‌شود." />}
      <Modal open={createOpen} title="ثبت مشتری جدید" description="مشتری با شماره تلفن یکتا ثبت می‌شود و ایمیل لازم نیست." onClose={closeCreate}>
        <form className="entity-form" onSubmit={submitCustomer}>
          <FormField label="نام و نام خانوادگی"><input name="displayName" required maxLength={200} /></FormField>
          <FormField label="شماره تلفن"><input name="phoneNumber" type="tel" dir="ltr" required minLength={7} maxLength={32} autoComplete="tel" /></FormField>
          <FormField label="رمز موقت" hint="حداقل ۱۲ کاراکتر، بزرگ/کوچک، عدد و علامت"><input name="temporaryPassword" type="password" minLength={12} required autoComplete="new-password" /></FormField>
          <InlineError message={formState.error} /><FormActions saving={formState.saving} submitLabel="ثبت مشتری" onCancel={closeCreate} />
        </form>
      </Modal>
      <Modal open={addressModalOpen} title={addressModalTitle} onClose={closeAddress}>
        {addressLoading ? <LoadingState /> : formState.error && !selectedAddress && addressMode !== "create" ? (
          <div className="address-load-error">
            <InlineError message={formState.error} />
            <button className="secondary-button" type="button" onClick={retryAddressLoad}>تلاش دوباره</button>
          </div>
        ) : addressMode === "view" ? (
          <div className="address-card-list">
            {addresses.map((address) => <article className="address-card" key={address.id}>
              <header><div><h3>{address.title}</h3><span>{address.isDefault ? "نشانی پیش‌فرض" : "نشانی دیگر"}</span></div><div className="icon-action-group"><button className="icon-action" type="button" title="ویرایش" aria-label={`ویرایش ${address.title}`} onClick={() => { setSelectedAddress(address); setAddressMode("edit"); setFormState(initialFormState); }}><Pencil size={15} /></button><button className="icon-action icon-action--danger" type="button" title="حذف" aria-label={`حذف ${address.title}`} onClick={() => { setSelectedAddress(address); setAddressMode("delete"); setFormState(initialFormState); }}><Trash2 size={15} /></button></div></header>
              <p><MapPin size={15} /> {address.province}، {address.city}، {address.addressLine}</p>
              <small><Phone size={14} /> {address.phoneNumber} · کد پستی {address.postalCode}</small>
            </article>)}
            <button className="secondary-button" type="button" onClick={() => { setSelectedAddress(null); setAddressMode("create"); setFormState(initialFormState); }}><MapPin size={15} /> افزودن نشانی دیگر</button>
            <InlineError message={formState.error} />
          </div>
        ) : addressMode === "delete" && selectedAddress ? (
          <div className="confirmation-panel">
            <Trash2 size={28} />
            <strong>نشانی «{selectedAddress.title}» حذف شود؟</strong>
            <p>{selectedAddress.province}، {selectedAddress.city}، {selectedAddress.addressLine}</p>
            <InlineError message={formState.error} />
            <div className="form-actions"><button className="secondary-button" type="button" onClick={closeAddress}>انصراف</button><button className="danger-button" type="button" disabled={formState.saving} onClick={() => void deleteAddress()}>{formState.saving ? "در حال حذف…" : "حذف نشانی"}</button></div>
          </div>
        ) : (
          <form className="entity-form" key={selectedAddress?.id || "new-address"} onSubmit={submitAddress}>
            <FormField label="عنوان"><input name="title" defaultValue={selectedAddress?.title || ""} placeholder="خانه / محل کار" required /></FormField>
            <FormField label="نام تحویل‌گیرنده"><input name="recipientName" defaultValue={selectedAddress?.recipientName || addressCustomer?.displayName || ""} required /></FormField>
            <FormField label="تلفن"><input name="phoneNumber" type="tel" dir="ltr" defaultValue={selectedAddress?.phoneNumber || addressCustomer?.phoneNumber || ""} required /></FormField>
            <FormField label="استان"><input name="province" defaultValue={selectedAddress?.province || ""} required /></FormField>
            <FormField label="شهر"><input name="city" defaultValue={selectedAddress?.city || ""} required /></FormField>
            <FormField label="کد پستی"><input name="postalCode" dir="ltr" defaultValue={selectedAddress?.postalCode || ""} required /></FormField>
            <FormField label="نشانی کامل" wide><textarea name="addressLine" defaultValue={selectedAddress?.addressLine || ""} required /></FormField>
            <label className="check-field"><input name="isDefault" type="checkbox" defaultChecked={selectedAddress?.isDefault ?? true} /> نشانی پیش‌فرض</label>
            <InlineError message={formState.error} /><FormActions saving={formState.saving} submitLabel={addressMode === "edit" ? "ذخیره ویرایش" : "ثبت نشانی"} onCancel={closeAddress} />
          </form>
        )}
      </Modal>
    </ModuleBody>
  );
}

function ProductPhoto({ product }: { product: Product }) {
  const { authorizedFetch } = useAuthentication();
  const [source, setSource] = useState<string | null>(null);
  const primary = product.images.find((image) => image.isPrimary) || product.images[0];
  useEffect(() => {
    let active = true;
    let objectUrl: string | null = null;
    if (!primary) {
      setSource(null);
      return () => { active = false; };
    }

    void authorizedFetch(`/api/v1/catalog/products/${product.id}/images/${primary.id}`)
      .then((response) => {
        if (!response.ok) throw new Error("تصویر محصول دریافت نشد.");
        return response.blob();
      })
      .then((blob) => {
        objectUrl = URL.createObjectURL(blob);
        if (active) setSource(objectUrl);
      })
      .catch(() => { if (active) setSource(null); });
    return () => {
      active = false;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [authorizedFetch, primary?.id, product.id]);

  return source
    ? <img className="product-photo" src={source} alt={primary?.altText || product.name} />
    : <div className="product-photo product-photo--empty"><Gem size={28} /><span>بدون تصویر</span></div>;
}

function ProductsPage({ path, onNavigate, onNotice }: RouteProps) {
  const { data, request, refresh, refreshing } = useOperations();
  const [createOpen, setCreateOpen] = useState(queryFlag(path, "new"));
  const [variantProduct, setVariantProduct] = useState<Product | null>(null);
  const [imageProduct, setImageProduct] = useState<Product | null>(null);
  const [formState, setFormState] = useState<FormState>(initialFormState);
  const categoryFilter = queryValue(path, "categoryId");
  const filteredProducts = categoryFilter
    ? data.products.filter((product) => product.productCategoryId === categoryFilter)
    : data.products;
  const closeCreate = () => { setFormState(initialFormState); setCreateOpen(false); if (queryFlag(path, "new")) onNavigate("/products"); };
  const submitProduct = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); const form = new FormData(event.currentTarget); setFormState({ saving: true, error: null });
    const file = form.get("image");
    if (!(file instanceof File) || file.size === 0) { setFormState({ saving: false, error: "برای محصول یک تصویر انتخاب کن." }); return; }
    try {
      const product = await request<Product>("/api/v1/catalog/products", { method: "POST", body: JSON.stringify({ productCategoryId: optionalText(form, "categoryId"), name: textValue(form, "name"), slug: textValue(form, "slug"), description: optionalText(form, "description") }) });
      const upload = new FormData(); upload.append("file", file); upload.append("altText", product.name);
      try {
        await request(`/api/v1/catalog/products/${product.id}/image`, { method: "PUT", body: upload });
      } catch (error) {
        setCreateOpen(false); setImageProduct(product); setFormState({ saving: false, error: `محصول ثبت شد اما تصویر ذخیره نشد: ${messageOf(error)}` }); await refresh(); return;
      }
      onNotice("محصول و تصویر آن ثبت شد؛ حالا مدل کالا را اضافه کن."); closeCreate(); await refresh();
    } catch (error) { setFormState({ saving: false, error: messageOf(error) }); }
  };
  const submitImage = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); if (!imageProduct) return; const form = new FormData(event.currentTarget); const file = form.get("image");
    if (!(file instanceof File) || file.size === 0) { setFormState({ saving: false, error: "یک تصویر انتخاب کن." }); return; }
    setFormState({ saving: true, error: null });
    try { const upload = new FormData(); upload.append("file", file); upload.append("altText", textValue(form, "altText") || imageProduct.name); await request(`/api/v1/catalog/products/${imageProduct.id}/image`, { method: "PUT", body: upload }); onNotice("تصویر محصول ذخیره شد."); setImageProduct(null); setFormState(initialFormState); await refresh(); } catch (error) { setFormState({ saving: false, error: messageOf(error) }); }
  };
  const submitVariant = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); if (!variantProduct) return; const form = new FormData(event.currentTarget); setFormState({ saving: true, error: null });
    try { const variant = await request<ProductVariant>(`/api/v1/catalog/products/${variantProduct.id}/variants`, { method: "POST", body: JSON.stringify({ sku: textValue(form, "sku"), name: textValue(form, "name"), goldDetail: { karat: numberValue(form, "karat"), grossWeight: numberValue(form, "grossWeight"), netGoldWeight: numberValue(form, "netGoldWeight"), stoneWeight: numberValue(form, "stoneWeight"), otherMaterialWeight: numberValue(form, "otherMaterialWeight"), manufacturingWageType: "FixedRials", manufacturingWageValue: 0, profitPercentage: 0, taxPercentage: 0, hasStone: form.get("hasStone") === "on", isWeightVariable: form.get("isWeightVariable") === "on" } }) }); await request("/api/v1/pricing/rules", { method: "POST", body: JSON.stringify({ productVariantId: variant.id, pricingMethod: "FixedPrice", goldMarketPriceType: null, fixedPriceRials: numberValue(form, "fixedPriceRials"), fixedGoldPricePerGramRials: null, wageType: "FixedRials", wageValue: 0, profitPercentage: 0, taxPercentage: 0, effectiveFrom: new Date().toISOString(), effectiveTo: null }) }); onNotice("مدل محصول و قیمت فروش آن ثبت شد."); setFormState(initialFormState); setVariantProduct(null); await refresh(); } catch (error) { setFormState({ saving: false, error: messageOf(error) }); }
  };
  return <ModuleBody><PageHeader icon={Gem} title="محصولات" description="کاتالوگ، مدل‌ها، کد کالا و مشخصات تخصصی طلا." actionLabel="محصول جدید" onAction={() => { setFormState(initialFormState); setCreateOpen(true); }} secondary={<RefreshButton refreshing={refreshing} onClick={() => void refresh()} />} /><DataNotice />
    <div className="module-metrics-grid"><MetricTile label="محصول" value={String(data.products.length)} hint="کالای اصلی" /><MetricTile label="مدل کالا" value={String(data.products.reduce((sum, item) => sum + item.variants.length, 0))} hint="SKU ثبت‌شده" /><MetricTile label="دسته‌بندی" value={String(data.categories.length)} hint="ساختار کاتالوگ" /></div>
    {categoryFilter && <div className="inline-help">فیلتر دسته‌بندی فعال است. <button className="text-button" type="button" onClick={() => onNavigate("/products")}>نمایش همه محصولات</button></div>}
    {filteredProducts.length ? <div className="card-grid">{filteredProducts.map((product) => <article className="lux-card entity-card product-card" key={product.id}><ProductPhoto product={product} /><header><div><small>{data.categories.find((item) => item.id === product.productCategoryId)?.name || "بدون دسته"}</small><h3>{product.name}</h3></div><StatusBadge status={product.isActive ? "Active" : "Inactive"} /></header><p>{product.description || "توضیحی ثبت نشده است."}</p><div className="chip-list">{product.variants.map((variant) => <span key={variant.id}>{variant.sku} · {variant.name}</span>)}{!product.variants.length && <span>هنوز مدلی ندارد</span>}</div><div className="entity-card-actions"><button className="secondary-button" type="button" onClick={() => { setFormState(initialFormState); setVariantProduct(product); }}><PackagePlus size={15} /> افزودن مدل</button><button className="secondary-button" type="button" onClick={() => { setFormState(initialFormState); setImageProduct(product); }}><ImagePlus size={15} /> {product.images.length ? "تغییر تصویر" : "افزودن تصویر"}</button></div></article>)}</div> : <EmptyState title="محصولی یافت نشد" description="محصول و تصویر آن را ثبت کن تا در انبار و سفارش قابل استفاده باشد." />}
    <Modal open={createOpen} title="ثبت محصول" description="تصویر JPG، PNG یا WebP تا ۵ مگابایت الزامی است." onClose={closeCreate}><form className="entity-form" onSubmit={submitProduct}><FormField label="نام محصول"><input name="name" required /></FormField><FormField label="شناسه لاتین" hint="مثلاً gold-ring-001"><input name="slug" dir="ltr" required /></FormField><FormField label="دسته‌بندی"><select name="categoryId"><option value="">بدون دسته</option>{data.categories.map((item) => <option value={item.id} key={item.id}>{item.name}</option>)}</select></FormField><FormField label="تصویر محصول" wide><input name="image" type="file" accept="image/jpeg,image/png,image/webp" required /></FormField><FormField label="توضیحات" wide><textarea name="description" /></FormField><InlineError message={formState.error} /><FormActions saving={formState.saving} submitLabel="ثبت محصول و تصویر" onCancel={closeCreate} /></form></Modal>
    <Modal open={Boolean(imageProduct)} title={`تصویر ${imageProduct?.name || "محصول"}`} onClose={() => { setImageProduct(null); setFormState(initialFormState); }}><form className="entity-form" onSubmit={submitImage}><FormField label="فایل تصویر" wide><input name="image" type="file" accept="image/jpeg,image/png,image/webp" required /></FormField><FormField label="متن جایگزین" wide><input name="altText" defaultValue={imageProduct?.name || ""} /></FormField><InlineError message={formState.error} /><FormActions saving={formState.saving} submitLabel="ذخیره تصویر" onCancel={() => { setImageProduct(null); setFormState(initialFormState); }} /></form></Modal>
    <Modal open={Boolean(variantProduct)} title={`مدل جدید برای ${variantProduct?.name || "محصول"}`} onClose={() => { setFormState(initialFormState); setVariantProduct(null); }}><form className="entity-form" onSubmit={submitVariant}><FormField label="کد کالا (SKU)"><input name="sku" dir="ltr" required /></FormField><FormField label="نام مدل"><input name="name" required /></FormField><FormField label="عیار"><select name="karat" defaultValue="18">{[9,10,14,18,21,22,24].map((value) => <option key={value}>{value}</option>)}</select></FormField><FormField label="قیمت ثابت فروش (ریال)"><input name="fixedPriceRials" type="number" min="1" step="1" required /></FormField><FormField label="وزن کل (گرم)"><input name="grossWeight" type="number" min="0.001" step="0.001" required /></FormField><FormField label="وزن خالص طلا"><input name="netGoldWeight" type="number" min="0.001" step="0.001" required /></FormField><FormField label="وزن نگین"><input name="stoneWeight" type="number" min="0" step="0.001" defaultValue="0" /></FormField><FormField label="وزن سایر مواد"><input name="otherMaterialWeight" type="number" min="0" step="0.001" defaultValue="0" /></FormField><label className="check-field"><input name="hasStone" type="checkbox" /> دارای نگین</label><label className="check-field"><input name="isWeightVariable" type="checkbox" /> وزن متغیر</label><InlineError message={formState.error} /><FormActions saving={formState.saving} submitLabel="ثبت مدل و قیمت" onCancel={() => { setFormState(initialFormState); setVariantProduct(null); }} /></form></Modal>
  </ModuleBody>;
}

function InventoryPage({ path, onNavigate, onNotice }: RouteProps) {
  const { data, request, refresh, refreshing } = useOperations();
  const [warehouseOpen, setWarehouseOpen] = useState(false);
  const [receiptOpen, setReceiptOpen] = useState(queryFlag(path, "receipt"));
  const [formState, setFormState] = useState<FormState>(initialFormState);
  const variants = useMemo(() => data.products.flatMap((product) => product.variants.map((variant) => ({ ...variant, productName: product.name }))), [data.products]);
  const closeReceipt = () => { setFormState(initialFormState); setReceiptOpen(false); if (queryFlag(path, "receipt")) onNavigate("/inventory"); };
  const submitWarehouse = async (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); const form = new FormData(event.currentTarget); setFormState({ saving: true, error: null }); try { await request<Warehouse>("/api/v1/inventory/warehouses", { method: "POST", body: JSON.stringify({ code: textValue(form, "code"), name: textValue(form, "name") }) }); onNotice("انبار جدید ثبت شد."); setFormState(initialFormState); setWarehouseOpen(false); await refresh(); } catch (error) { setFormState({ saving: false, error: messageOf(error) }); } };
  const submitReceipt = async (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); const form = new FormData(event.currentTarget); setFormState({ saving: true, error: null }); try { await request("/api/v1/inventory/receipts", { method: "POST", body: JSON.stringify({ warehouseId: textValue(form, "warehouseId"), productVariantId: textValue(form, "variantId"), quantity: numberValue(form, "quantity"), referenceType: "DesktopReceipt", reason: optionalText(form, "reason") }) }); onNotice("رسید انبار ثبت و موجودی به‌روزرسانی شد."); closeReceipt(); await refresh(); } catch (error) { setFormState({ saving: false, error: messageOf(error) }); } };
  return <ModuleBody><PageHeader icon={Boxes} title="انبار" description="موجودی واقعی هر انبار، رزرو، قیمت خرید میانگین و مقدار قابل فروش." actionLabel="موجودی اولیه / اصلاح" onAction={() => { setFormState(initialFormState); setReceiptOpen(true); }} secondary={<><button className="secondary-button" type="button" onClick={() => onNavigate("/suppliers?purchase=1")}>خرید از تأمین‌کننده</button><button className="secondary-button" type="button" onClick={() => { setFormState(initialFormState); setWarehouseOpen(true); }}>انبار جدید</button><RefreshButton refreshing={refreshing} onClick={() => void refresh()} /></>} /><DataNotice />
    <div className="module-metrics-grid"><MetricTile label="انبار فعال" value={String(data.warehouses.filter((item) => item.isActive).length)} hint="محل نگهداری" /><MetricTile label="موجودی کل" value={String(data.inventoryItems.reduce((sum, item) => sum + item.quantityOnHand, 0))} hint="قطعه" /><MetricTile label="قابل فروش" value={String(data.inventoryItems.reduce((sum, item) => sum + item.quantityAvailable, 0))} hint="پس از رزرو" /></div>
    {data.inventoryItems.length ? <TableCard><div className="table-scroll"><table className="data-table"><thead><tr><th>انبار</th><th>محصول / مدل</th><th>موجودی</th><th>رزرو</th><th>قابل فروش</th><th>میانگین قیمت خرید</th></tr></thead><tbody>{data.inventoryItems.map((item) => { const variant = variants.find((value) => value.id === item.productVariantId); return <tr key={item.id}><td>{data.warehouses.find((value) => value.id === item.warehouseId)?.name || "—"}</td><td>{variant ? `${variant.productName} · ${variant.name}` : item.productVariantId}<small>{variant?.sku}</small></td><td>{item.quantityOnHand}</td><td>{item.quantityReserved}</td><td><strong>{item.quantityAvailable}</strong></td><td>{item.hasAcquisitionCost ? formatRials(item.averageUnitCostRials) : <span className="muted-text">ثبت نشده</span>}</td></tr>; })}</tbody></table></div></TableCard> : <EmptyState title="موجودی ثبت نشده" description="ابتدا انبار و مدل محصول را بساز، سپس خرید را از صفحه تأمین‌کنندگان ثبت کن." />}
    <Modal open={warehouseOpen} title="ساخت انبار" onClose={() => { setFormState(initialFormState); setWarehouseOpen(false); }}><form className="entity-form" onSubmit={submitWarehouse}><FormField label="کد انبار"><input name="code" dir="ltr" required /></FormField><FormField label="نام انبار"><input name="name" required /></FormField><InlineError message={formState.error} /><FormActions saving={formState.saving} submitLabel="ثبت انبار" onCancel={() => { setFormState(initialFormState); setWarehouseOpen(false); }} /></form></Modal>
    <Modal open={receiptOpen} title="موجودی اولیه / اصلاح" description="این فرم قیمت خرید ندارد و فقط برای ورود اولیه یا اصلاح فنی است. خرید واقعی را از صفحه تأمین‌کنندگان ثبت کن." onClose={closeReceipt}><form className="entity-form" onSubmit={submitReceipt}><FormField label="انبار"><select name="warehouseId" required><option value="">انتخاب کن</option>{data.warehouses.filter((item) => item.isActive).map((item) => <option value={item.id} key={item.id}>{item.name}</option>)}</select></FormField><FormField label="مدل کالا"><select name="variantId" required><option value="">انتخاب کن</option>{variants.filter((item) => item.isActive).map((item) => <option value={item.id} key={item.id}>{item.productName} · {item.name} ({item.sku})</option>)}</select></FormField><FormField label="تعداد"><input name="quantity" type="number" min="1" defaultValue="1" required /></FormField><FormField label="علت اصلاح"><input name="reason" placeholder="موجودی اولیه / اصلاح شمارش" required /></FormField><InlineError message={formState.error} /><FormActions saving={formState.saving} submitLabel="ثبت موجودی" onCancel={closeReceipt} /></form></Modal>
  </ModuleBody>;
}

function OrdersPage(props: RouteProps) {
  const { data, request, refresh, refreshing } = useOperations();
  const [settleOrder, setSettleOrder] = useState<Order | null>(queryFlag(props.path, "settle") ? data.orders.find((item) => !["Paid", "Completed", "Cancelled"].includes(item.status)) || null : null);
  const [formState, setFormState] = useState<FormState>(initialFormState);
  useEffect(() => {
    if (!queryFlag(props.path, "settle") || settleOrder || !data.orders.length) return;
    setSettleOrder(data.orders.find((item) => !["Paid", "Completed", "Cancelled", "Refunded"].includes(item.status)) || null);
  }, [data.orders, props.path, settleOrder]);
  const closeSettlement = () => { setFormState(initialFormState); setSettleOrder(null); if (queryFlag(props.path, "settle")) props.onNavigate("/orders"); };
  const submitPayment = async (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); if (!settleOrder) return; const form = new FormData(event.currentTarget); setFormState({ saving: true, error: null }); try { const payment = await request<Payment>("/api/v1/payments/manual", { method: "POST", headers: { "Idempotency-Key": createIdempotencyKey("manual-payment") }, body: JSON.stringify({ orderId: settleOrder.id, method: textValue(form, "method"), reference: optionalText(form, "reference") }) }); setSettleOrder(null); setFormState(initialFormState); await refresh(); if (payment.invoiceId) { props.onNotice("پرداخت تأیید و فاکتور صادر شد؛ پیش‌نمایش فاکتور باز شد."); props.onNavigate(`/invoices?open=${payment.invoiceId}`); } else { props.onNotice("پرداخت ثبت شد."); props.onNavigate("/orders"); } } catch (error) { setFormState({ saving: false, error: messageOf(error) }); } };
  return <ModuleBody><PageHeader icon={ShoppingBag} title="سفارش‌ها" description="سفارش، رزرو موجودی، پرداخت و صدور فاکتور در یک گردش‌کار." actionLabel="سفارش جدید" onAction={() => props.onNavigate("/orders/new")} secondary={<RefreshButton refreshing={refreshing} onClick={() => void refresh()} />} /><DataNotice />
    <div className="module-metrics-grid"><MetricTile label="کل سفارش‌ها" value={String(data.orders.length)} hint="همه وضعیت‌ها" /><MetricTile label="در انتظار پرداخت" value={String(data.orders.filter((item) => ["Pending","AwaitingPayment","PaymentReview"].includes(item.status)).length)} hint="نیازمند اقدام" /><MetricTile label="فروش سفارش‌ها" value={formatRials(data.orders.filter((item) => item.status !== "Cancelled").reduce((sum, item) => sum + item.grandTotalRials, 0))} hint="غیرلغوشده" /></div>
    {data.orders.length ? <TableCard><div className="table-scroll"><table className="data-table"><thead><tr><th>شماره</th><th>مشتری</th><th>اقلام</th><th>مبلغ</th><th>وضعیت</th><th /></tr></thead><tbody>{data.orders.map((order) => <tr key={order.id}><td>{order.orderNumber}</td><td>{order.customerNameSnapshot || "مشتری"}</td><td>{order.items.length}</td><td>{formatRials(order.grandTotalRials)}</td><td><StatusBadge status={order.status} /></td><td>{!["Paid","Completed","Cancelled","Refunded"].includes(order.status) && <button className="row-action" type="button" onClick={() => { setFormState(initialFormState); setSettleOrder(order); }}>ثبت پرداخت</button>}</td></tr>)}</tbody></table></div></TableCard> : <EmptyState title="سفارشی ثبت نشده" description="سفارش جدید، موجودی را رزرو می‌کند و بعد از تسویه فاکتور صادر می‌شود." />}
    <Modal open={Boolean(settleOrder)} title={`تسویه ${settleOrder?.orderNumber || "سفارش"}`} description={settleOrder ? `مبلغ قابل پرداخت: ${formatRials(settleOrder.grandTotalRials)}` : undefined} onClose={closeSettlement}><form className="entity-form" onSubmit={submitPayment}><FormField label="روش پرداخت"><select name="method"><option value="Cash">نقدی</option><option value="PointOfSale">کارت‌خوان</option><option value="BankTransfer">حواله بانکی</option><option value="CardToCard">کارت‌به‌کارت</option></select></FormField><FormField label="شماره پیگیری"><input name="reference" /></FormField><InlineError message={formState.error} /><FormActions saving={formState.saving} submitLabel="ثبت پرداخت و صدور فاکتور" onCancel={closeSettlement} /></form></Modal>
  </ModuleBody>;
}

function NewOrderPage({ path, onNavigate, onNotice }: RouteProps) {
  const { data, request, refresh } = useOperations();
  const requestedCustomerId = queryValue(path, "customerId");
  const [customerId, setCustomerId] = useState(() =>
    data.customers.some((item) => item.id === requestedCustomerId && item.isActive)
      ? requestedCustomerId
      : "",
  );
  const [addresses, setAddresses] = useState<CustomerAddress[]>([]);
  const [loadingAddresses, setLoadingAddresses] = useState(false);
  const [addressError, setAddressError] = useState<string | null>(null);
  const [formState, setFormState] = useState<FormState>(initialFormState);
  const variants = useMemo(() => data.products.flatMap((product) => product.variants.map((variant) => ({ ...variant, productName: product.name }))), [data.products]);
  useEffect(() => {
    if (requestedCustomerId && data.customers.some((item) => item.id === requestedCustomerId && item.isActive)) {
      setCustomerId(requestedCustomerId);
    }
  }, [data.customers, requestedCustomerId]);
  useEffect(() => {
    let active = true;
    if (!customerId) {
      setAddresses([]);
      setAddressError(null);
      return () => { active = false; };
    }
    setLoadingAddresses(true);
    setAddressError(null);
    void request<CustomerAddress[]>(`/api/v1/customers/${customerId}/addresses`)
      .then((loaded) => { if (active) setAddresses(loaded); })
      .catch((error) => {
        if (!active) return;
        setAddresses([]);
        setAddressError(messageOf(error));
      })
      .finally(() => { if (active) setLoadingAddresses(false); });
    return () => { active = false; };
  }, [customerId, request]);
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const addressId = textValue(form, "addressId");
    const inventoryItem = data.inventoryItems.find((item) => item.id === textValue(form, "inventoryItemId"));
    if (!data.storeProfile) {
      setFormState({ saving: false, error: "قبل از اولین سفارش، مشخصات فروشگاه را در تنظیمات کامل کن." });
      return;
    }
    if (!customerId || !addresses.some((address) => address.id === addressId)) {
      setFormState({ saving: false, error: "مشتری و یکی از نشانی‌های ثبت‌شده او را انتخاب کن." });
      return;
    }
    if (!inventoryItem || inventoryItem.quantityAvailable < numberValue(form, "quantity")) {
      setFormState({ saving: false, error: "موجودی انتخاب‌شده معتبر یا کافی نیست؛ صفحه را به‌روزرسانی کن." });
      return;
    }
    setFormState({ saving: true, error: null });
    try {
      await request<Order>("/api/v1/orders", {
        method: "POST",
        headers: { "Idempotency-Key": createIdempotencyKey("desktop-order") },
        body: JSON.stringify({
          customerId,
          customerAddressId: addressId,
          customerNationalId: optionalText(form, "nationalId"),
          lines: [{ inventoryItemId: inventoryItem.id, inventoryUnitId: null, quantity: numberValue(form, "quantity"), inventoryRowVersion: inventoryItem.rowVersion }],
          reservationLifetimeMinutes: 15,
          discountRials: numberValue(form, "discount"),
          shippingRials: numberValue(form, "shipping"),
        }),
      });
      await refresh();
      onNotice("سفارش ثبت و موجودی آن رزرو شد؛ حالا پرداخت را ثبت کن.");
      onNavigate("/orders");
    } catch (error) {
      const friendlyMessage = error instanceof OperationalApiError && error.status === 404
        ? "مشتری، نشانی یا موجودی انتخاب‌شده دیگر در دسترس نیست؛ اطلاعات صفحه را به‌روزرسانی کن."
        : error instanceof OperationalApiError && error.status === 422
          ? "قبل از ثبت سفارش، مشخصات فروشگاه را در تنظیمات کامل کن."
          : messageOf(error);
      setFormState({ saving: false, error: friendlyMessage });
    }
  };
  return <ModuleBody><PageHeader icon={ShoppingBag} title="سفارش و فاکتور جدید" description="ابتدا سفارش ساخته می‌شود؛ پس از ثبت پرداخت، فاکتور رسمی خودکار صادر خواهد شد." secondary={<button className="secondary-button" type="button" onClick={() => onNavigate("/orders")}>بازگشت</button>} />
    {!data.storeProfile && <div className="setup-warning"><Settings size={20} /><div><strong>مشخصات فروشگاه هنوز کامل نشده است</strong><span>برای ثبت سفارش و Snapshot صحیح فاکتور، ابتدا مشخصات فروشگاه را ذخیره کن.</span></div><button className="secondary-button" type="button" onClick={() => onNavigate("/settings")}>رفتن به تنظیمات</button></div>}
    <section className="lux-card workflow-card"><div className="workflow-steps"><span className="is-active">۱ مشتری</span><span>۲ کالا و موجودی</span><span>۳ پرداخت</span><span>۴ فاکتور</span></div><form className="entity-form entity-form--page" onSubmit={submit}><FormField label="مشتری"><select value={customerId} onChange={(event) => setCustomerId(event.target.value)} required><option value="">انتخاب مشتری</option>{data.customers.filter((item) => item.isActive).map((item) => <option value={item.id} key={item.id}>{item.displayName} · {item.phoneNumber || "بدون تلفن"}</option>)}</select></FormField><FormField label="نشانی"><select name="addressId" disabled={!customerId || loadingAddresses || Boolean(addressError)} required><option value="">{loadingAddresses ? "در حال دریافت…" : "انتخاب نشانی"}</option>{addresses.map((item) => <option value={item.id} key={item.id}>{item.title} · {item.city}</option>)}</select></FormField><FormField label="کد/شناسه ملی مشتری"><input name="nationalId" /></FormField><FormField label="موجودی کالا"><select name="inventoryItemId" required><option value="">انتخاب کالا</option>{data.inventoryItems.filter((item) => item.quantityAvailable > 0).map((item) => { const variant = variants.find((value) => value.id === item.productVariantId); return <option value={item.id} key={item.id}>{variant ? `${variant.productName} · ${variant.name}` : item.id} — موجودی {item.quantityAvailable}</option>; })}</select></FormField><FormField label="تعداد"><input name="quantity" type="number" min="1" defaultValue="1" required /></FormField><FormField label="تخفیف (ریال)"><input name="discount" type="number" min="0" defaultValue="0" /></FormField><FormField label="هزینه ارسال (ریال)"><input name="shipping" type="number" min="0" defaultValue="0" /></FormField>{addressError && <div className="inline-error form-field--wide">دریافت نشانی‌ها کامل نشد: {addressError}</div>}{customerId && !loadingAddresses && !addressError && !addresses.length && <div className="inline-help form-field--wide">این مشتری نشانی ندارد. از صفحه مشتریان برای او نشانی ثبت کن.</div>}<InlineError message={formState.error} /><FormActions saving={formState.saving} submitLabel="ثبت سفارش" onCancel={() => onNavigate("/orders")} /></form></section>
  </ModuleBody>;
}

function AccountingPage() {
  const { data, refresh, refreshing } = useOperations();
  const verified = data.payments.filter((item) => item.status === "Verified");
  const total = verified.reduce((sum, item) => sum + item.amountRials, 0);
  const outstanding = data.orders.filter((item) => !["Paid","Completed","Cancelled","Refunded"].includes(item.status)).reduce((sum, item) => sum + item.grandTotalRials, 0);
  return <ModuleBody><PageHeader icon={Calculator} title="حسابداری" description="پرداخت‌های واقعی، وجوه وصول‌شده و مانده سفارش‌ها." secondary={<RefreshButton refreshing={refreshing} onClick={() => void refresh()} />} /><DataNotice /><div className="module-metrics-grid"><MetricTile label="دریافت تأییدشده" value={formatRials(total)} hint={`${verified.length} پرداخت`} /><MetricTile label="مطالبات سفارش‌ها" value={formatRials(outstanding)} hint="تسویه‌نشده" /><MetricTile label="پرداخت نیازمند بررسی" value={String(data.payments.filter((item) => item.status === "RequiresReview").length)} hint="پیگیری مالی" /></div>
    {data.payments.length ? <TableCard><div className="table-scroll"><table className="data-table"><thead><tr><th>شناسه</th><th>سفارش</th><th>روش</th><th>مبلغ</th><th>زمان تأیید</th><th>وضعیت</th></tr></thead><tbody>{data.payments.map((payment) => <tr key={payment.id}><td className="numeric-cell">{payment.id.slice(0,8)}</td><td>{data.orders.find((item) => item.id === payment.orderId)?.orderNumber || payment.orderId.slice(0,8)}</td><td>{translateStatus(payment.method)}</td><td>{formatRials(payment.amountRials)}</td><td>{formatDate(payment.verifiedAt)}</td><td><StatusBadge status={payment.status} /></td></tr>)}</tbody></table></div></TableCard> : <EmptyState title="تراکنشی ثبت نشده" description="پرداخت‌های نقدی، کارت‌خوان و درگاه پس از ثبت اینجا دیده می‌شوند." />}
  </ModuleBody>;
}

function ReportsPage() {
  const { data, refresh, refreshing } = useOperations();
  const today = new Date().toDateString();
  const todayInvoices = data.invoices.filter((item) => item.status !== "Voided" && new Date(item.issuedAt).toDateString() === today);
  const sales = todayInvoices.reduce((sum, item) => sum + item.grandTotalRials, 0);
  const profit = todayInvoices.reduce((sum, invoice) => sum + invoiceActualProfit(invoice), 0);
  return <ModuleBody><PageHeader icon={BarChart3} title="گزارش‌ها" description="گزارش عملیاتی بر پایه داده‌های فاکتور، سفارش، کالا و انبار." secondary={<RefreshButton refreshing={refreshing} onClick={() => void refresh()} />} /><DataNotice /><div className="module-metrics-grid"><MetricTile label="فروش امروز" value={formatRials(sales)} hint={`${todayInvoices.length} فاکتور`} /><MetricTile label="سود واقعی امروز" value={formatRials(profit)} hint="فروش منهای خرید و تخفیف" /><MetricTile label="موجودی قابل فروش" value={String(data.inventoryItems.reduce((sum, item) => sum + item.quantityAvailable, 0))} hint="قطعه" /><MetricTile label="مشتری فعال" value={String(data.customers.filter((item) => item.isActive).length)} hint="حساب" /></div>
    <div className="reports-grid"><section className="lux-card report-card"><h2>وضعیت سفارش‌ها</h2>{["Pending","AwaitingPayment","Paid","Processing","Completed","Cancelled"].map((status) => <div className="report-row" key={status}><span>{translateStatus(status)}</span><strong>{data.orders.filter((item) => item.status === status).length}</strong></div>)}</section><section className="lux-card report-card"><h2>محصولات پرفروش</h2>{data.products.map((product) => ({ product, count: data.invoices.filter((invoice) => invoice.status !== "Voided").flatMap((item) => item.items).filter((line) => line.productName === product.name).reduce((sum, line) => sum + line.quantity, 0) })).sort((a,b) => b.count-a.count).slice(0,6).map((item) => <div className="report-row" key={item.product.id}><span>{item.product.name}</span><strong>{item.count}</strong></div>)}{!data.products.length && <p>داده‌ای ثبت نشده است.</p>}</section></div>
  </ModuleBody>;
}

function EmployeesPage({ onNotice }: RouteProps) {
  const { data, request, refresh, refreshing } = useOperations(); const [open, setOpen] = useState(false); const [formState, setFormState] = useState<FormState>(initialFormState);
  const close = () => { setFormState(initialFormState); setOpen(false); };
  const submit = async (event: FormEvent<HTMLFormElement>) => { event.preventDefault(); const form = new FormData(event.currentTarget); setFormState({ saving: true, error: null }); try { await request<Person>("/api/v1/people/employees", { method: "POST", body: JSON.stringify({ displayName: textValue(form,"displayName"), email: textValue(form,"email"), phoneNumber: optionalText(form,"phoneNumber"), temporaryPassword: textValue(form,"temporaryPassword") }) }); onNotice("کارمند مدیر ثبت شد؛ در اولین ورود باید MFA را فعال کند."); close(); await refresh(); } catch (error) { setFormState({ saving:false,error:messageOf(error) }); } };
  return <ModuleBody><PageHeader icon={IdCard} title="کارکنان" description="مالک و مدیران سیستم از جدول امنیت و نقش‌ها خوانده می‌شوند." actionLabel="مدیر جدید" onAction={() => { setFormState(initialFormState); setOpen(true); }} secondary={<RefreshButton refreshing={refreshing} onClick={() => void refresh()} />} /><DataNotice />{data.employees.length ? <div className="card-grid">{data.employees.map((employee) => <article className="lux-card person-card" key={employee.id}><span className="large-avatar">{employee.displayName.slice(0,1)}</span><div><h3>{employee.displayName}</h3><p>{employee.email}</p><div className="chip-list">{employee.roles.map((role) => <span key={role}>{translateStatus(role)}</span>)}<span>{employee.mfaEnabled ? "MFA فعال" : "MFA در انتظار"}</span></div></div><StatusBadge status={employee.isActive ? "Active" : "Inactive"} /></article>)}</div> : <EmptyState title="کارمندی یافت نشد" description="حساب مالک فعلی باید دست‌کم در این لیست نمایش داده شود." />}<Modal open={open} title="ثبت مدیر جدید" description="حساب مدیر ملزم به فعال‌سازی MFA است." onClose={close}><form className="entity-form" onSubmit={submit}><FormField label="نام کامل"><input name="displayName" required /></FormField><FormField label="ایمیل"><input name="email" type="email" required /></FormField><FormField label="تلفن"><input name="phoneNumber" /></FormField><FormField label="رمز موقت"><input name="temporaryPassword" type="password" minLength={12} required /></FormField><InlineError message={formState.error} /><FormActions saving={formState.saving} submitLabel="ثبت مدیر" onCancel={close} /></form></Modal></ModuleBody>;
}

function SuppliersPage({ path, onNavigate, onNotice }: RouteProps) {
  const { data, request, refresh, refreshing } = useOperations();
  const [open, setOpen] = useState(false);
  const [purchaseSupplier, setPurchaseSupplier] = useState<Supplier | null>(null);
  const [purchaseDraft, setPurchaseDraft] = useState({ quantity: 1, unitCost: 0, sellingPrice: 0 });
  const [formState, setFormState] = useState<FormState>(initialFormState);
  const variants = useMemo(() => data.products.flatMap((product) =>
    product.variants.map((variant) => ({ ...variant, productName: product.name }))), [data.products]);
  useEffect(() => {
    if (!queryFlag(path, "purchase") || purchaseSupplier) return;
    const supplier = data.suppliers.find((item) => item.isActive);
    if (supplier) setPurchaseSupplier(supplier);
  }, [data.suppliers, path, purchaseSupplier]);
  const close = () => { setFormState(initialFormState); setOpen(false); };
  const closePurchase = () => {
    setFormState(initialFormState); setPurchaseSupplier(null);
    setPurchaseDraft({ quantity: 1, unitCost: 0, sellingPrice: 0 });
    if (queryFlag(path, "purchase")) onNavigate("/suppliers");
  };
  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); const form = new FormData(event.currentTarget); setFormState({ saving: true, error: null });
    try { await request<Supplier>("/api/v1/suppliers", { method: "POST", body: JSON.stringify({ code: textValue(form, "code"), name: textValue(form, "name"), contactName: optionalText(form, "contactName"), phoneNumber: optionalText(form, "phoneNumber"), email: optionalText(form, "email"), nationalId: optionalText(form, "nationalId"), addressLine: optionalText(form, "addressLine"), notes: optionalText(form, "notes") }) }); onNotice("تأمین‌کننده در دیتابیس ثبت شد."); close(); await refresh(); } catch (error) { setFormState({ saving: false, error: messageOf(error) }); }
  };
  const submitPurchase = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault(); if (!purchaseSupplier) return; const form = new FormData(event.currentTarget); setFormState({ saving: true, error: null });
    const purchasedAt = textValue(form, "purchasedAt");
    try {
      await request<SupplierPurchase>("/api/v1/inventory/supplier-purchases", { method: "POST", body: JSON.stringify({ supplierId: purchaseSupplier.id, warehouseId: textValue(form, "warehouseId"), productVariantId: textValue(form, "variantId"), quantity: numberValue(form, "quantity"), unitCostRials: numberValue(form, "unitCostRials"), sellingUnitPriceRials: numberValue(form, "sellingUnitPriceRials"), purchasedAt: purchasedAt ? new Date(purchasedAt).toISOString() : null, supplierReference: optionalText(form, "supplierReference"), notes: optionalText(form, "notes") }) });
      onNotice("خرید ثبت شد؛ موجودی، میانگین قیمت خرید و قیمت فروش به‌روز شدند."); closePurchase(); await refresh();
    } catch (error) { setFormState({ saving: false, error: messageOf(error) }); }
  };
  const totalPurchaseCost = data.supplierPurchases.reduce((sum, item) => sum + item.totalCostRials, 0);
  const totalExpectedProfit = data.supplierPurchases.reduce((sum, item) => sum + item.expectedTotalProfitRials, 0);
  const expectedProfit = (purchaseDraft.sellingPrice - purchaseDraft.unitCost) * purchaseDraft.quantity;
  return <ModuleBody>
    <PageHeader icon={Truck} title="تأمین‌کنندگان و خرید" description="ثبت تعداد خرید، قیمت خرید و قیمت فروش دستی؛ اختلاف قیمت به‌عنوان سود واقعی فاکتور محاسبه می‌شود." actionLabel="تأمین‌کننده جدید" onAction={() => { setFormState(initialFormState); setOpen(true); }} secondary={<RefreshButton refreshing={refreshing} onClick={() => void refresh()} />} />
    <DataNotice />
    <div className="module-metrics-grid"><MetricTile label="خریدهای ثبت‌شده" value={String(data.supplierPurchases.length)} hint="سند خرید" /><MetricTile label="جمع بهای خرید" value={formatRials(totalPurchaseCost)} hint="بهای تمام‌شده" /><MetricTile label="سود مورد انتظار" value={formatRials(totalExpectedProfit)} hint="پیش از فروش و تخفیف" /></div>
    {data.suppliers.length ? <TableCard><div className="table-scroll"><table className="data-table"><thead><tr><th>کد</th><th>نام</th><th>مسئول تماس</th><th>تلفن</th><th>وضعیت</th><th /></tr></thead><tbody>{data.suppliers.map((supplier) => <tr key={supplier.id}><td>{supplier.code}</td><td><strong>{supplier.name}</strong></td><td>{supplier.contactName || "—"}</td><td>{supplier.phoneNumber || "—"}</td><td><StatusBadge status={supplier.isActive ? "Active" : "Inactive"} /></td><td>{supplier.isActive && <button className="row-action" type="button" onClick={() => { setFormState(initialFormState); setPurchaseDraft({ quantity: 1, unitCost: 0, sellingPrice: 0 }); setPurchaseSupplier(supplier); }}>ثبت خرید</button>}</td></tr>)}</tbody></table></div></TableCard> : <EmptyState title="تأمین‌کننده‌ای ثبت نشده" description="ابتدا تأمین‌کننده را ثبت کن و سپس خرید کالا را از همان ردیف انجام بده." />}
    {data.supplierPurchases.length > 0 && <section className="module-subsection"><h2 className="section-title">آخرین خریدها</h2><TableCard><div className="table-scroll"><table className="data-table"><thead><tr><th>سند</th><th>تأمین‌کننده</th><th>محصول</th><th>تعداد</th><th>خرید واحد</th><th>فروش واحد</th><th>سود مورد انتظار</th><th>تاریخ</th></tr></thead><tbody>{data.supplierPurchases.map((purchase) => <tr key={purchase.id}><td>{purchase.purchaseNumber}</td><td>{purchase.supplierName}</td><td><strong>{purchase.productName} · {purchase.variantName}</strong><small>{purchase.sku}</small></td><td>{purchase.quantity}</td><td>{formatRials(purchase.unitCostRials)}</td><td>{formatRials(purchase.sellingUnitPriceRials)}</td><td><strong className={purchase.expectedTotalProfitRials < 0 ? "negative-text" : "positive-text"}>{formatRials(purchase.expectedTotalProfitRials)}</strong></td><td>{formatDate(purchase.purchasedAt)}</td></tr>)}</tbody></table></div></TableCard></section>}
    <Modal open={open} title="ثبت تأمین‌کننده" onClose={close}><form className="entity-form" onSubmit={submit}><FormField label="کد"><input name="code" dir="ltr" required /></FormField><FormField label="نام مجموعه"><input name="name" required /></FormField><FormField label="مسئول تماس"><input name="contactName" /></FormField><FormField label="تلفن"><input name="phoneNumber" /></FormField><FormField label="ایمیل"><input name="email" type="email" /></FormField><FormField label="شناسه ملی"><input name="nationalId" /></FormField><FormField label="نشانی" wide><textarea name="addressLine" /></FormField><FormField label="یادداشت" wide><textarea name="notes" /></FormField><InlineError message={formState.error} /><FormActions saving={formState.saving} submitLabel="ثبت تأمین‌کننده" onCancel={close} /></form></Modal>
    <Modal open={Boolean(purchaseSupplier)} title={`ثبت خرید از ${purchaseSupplier?.name || "تأمین‌کننده"}`} description="قیمت فروش این فرم، قیمت فعال فروشگاه برای مدل انتخاب‌شده می‌شود." onClose={closePurchase}><form className="entity-form" onSubmit={submitPurchase}><FormField label="انبار"><select name="warehouseId" required><option value="">انتخاب کن</option>{data.warehouses.filter((item) => item.isActive).map((item) => <option value={item.id} key={item.id}>{item.name}</option>)}</select></FormField><FormField label="مدل کالا"><select name="variantId" required><option value="">انتخاب کن</option>{variants.filter((item) => item.isActive).map((item) => <option value={item.id} key={item.id}>{item.productName} · {item.name} ({item.sku})</option>)}</select></FormField><FormField label="تعداد خرید"><input name="quantity" type="number" min="1" step="1" value={purchaseDraft.quantity} onChange={(event) => setPurchaseDraft((current) => ({ ...current, quantity: Number(event.target.value) }))} required /></FormField><FormField label="قیمت خرید هر واحد (ریال)"><input name="unitCostRials" type="number" min="0" step="1" value={purchaseDraft.unitCost} onChange={(event) => setPurchaseDraft((current) => ({ ...current, unitCost: Number(event.target.value) }))} required /></FormField><FormField label="قیمت فروش هر واحد (ریال)"><input name="sellingUnitPriceRials" type="number" min="1" step="1" value={purchaseDraft.sellingPrice} onChange={(event) => setPurchaseDraft((current) => ({ ...current, sellingPrice: Number(event.target.value) }))} required /></FormField><FormField label="زمان خرید"><input name="purchasedAt" type="datetime-local" /></FormField><div className={`purchase-profit-preview ${expectedProfit < 0 ? "is-negative" : ""}`}><span>سود مورد انتظار این خرید</span><strong>{formatRials(expectedProfit)}</strong><small>اختلاف قیمت فروش و خرید × تعداد</small></div><FormField label="شماره فاکتور تأمین‌کننده"><input name="supplierReference" maxLength={100} /></FormField><FormField label="یادداشت" wide><textarea name="notes" maxLength={1000} /></FormField><InlineError message={formState.error} /><FormActions saving={formState.saving} submitLabel="ثبت خرید و قیمت فروش" onCancel={closePurchase} /></form></Modal>
  </ModuleBody>;
}

function CrmPage({ onNotice }: RouteProps) {
  const {data,request,refresh,refreshing}=useOperations();const[open,setOpen]=useState(false);const[formState,setFormState]=useState<FormState>(initialFormState);
  const close=()=>{setFormState(initialFormState);setOpen(false);};
  const submit=async(event:FormEvent<HTMLFormElement>)=>{event.preventDefault();const form=new FormData(event.currentTarget);setFormState({saving:true,error:null});try{const followUp=textValue(form,"nextFollowUpAt");await request<CustomerInteraction>("/api/v1/crm/interactions",{method:"POST",body:JSON.stringify({customerId:textValue(form,"customerId"),interactionType:textValue(form,"interactionType"),subject:textValue(form,"subject"),notes:optionalText(form,"notes"),occurredAt:new Date().toISOString(),nextFollowUpAt:followUp?new Date(followUp).toISOString():null})});onNotice("تعامل مشتری و پیگیری آن ثبت شد.");close();await refresh();}catch(error){setFormState({saving:false,error:messageOf(error)});}};
  const complete=async(interaction:CustomerInteraction)=>{try{await request(`/api/v1/crm/interactions/${interaction.id}/status`,{method:"POST",body:JSON.stringify({status:"Completed",rowVersion:interaction.rowVersion})});onNotice("پیگیری تکمیل شد.");await refresh();}catch(error){onNotice(messageOf(error));}};
  return <ModuleBody><PageHeader icon={HeartHandshake} title="ارتباط با مشتری" description="تماس‌ها، جلسات، یادداشت‌ها و پیگیری‌های آینده." actionLabel="تعامل جدید" onAction={()=>{setFormState(initialFormState);setOpen(true);}} secondary={<RefreshButton refreshing={refreshing} onClick={()=>void refresh()}/>} /><DataNotice /><div className="module-metrics-grid"><MetricTile label="تعامل‌ها" value={String(data.interactions.length)} hint="همه رکوردها"/><MetricTile label="پیگیری باز" value={String(data.interactions.filter(item=>item.status==="Open").length)} hint="نیازمند اقدام"/><MetricTile label="تکمیل‌شده" value={String(data.interactions.filter(item=>item.status==="Completed").length)} hint="بسته‌شده"/></div>{data.interactions.length?<div className="timeline-list">{data.interactions.map(item=><article className="lux-card timeline-item" key={item.id}><span className="timeline-dot"/><div><header><div><small>{translateStatus(item.interactionType)} · {formatDate(item.occurredAt)}</small><h3>{item.subject}</h3></div><StatusBadge status={item.status}/></header><p>{item.customerName}</p>{item.notes&&<blockquote>{item.notes}</blockquote>}{item.nextFollowUpAt&&<small>پیگیری: {formatDate(item.nextFollowUpAt)}</small>}{item.status==="Open"&&<button className="secondary-button" type="button" onClick={()=>void complete(item)}><Check size={15}/> تکمیل پیگیری</button>}</div></article>)}</div>:<EmptyState title="تعامل مشتری ثبت نشده" description="تماس یا پیگیری جدید را ثبت کن؛ اطلاعات ساختگی نمایش داده نمی‌شود."/>}<Modal open={open} title="ثبت تعامل مشتری" onClose={close}><form className="entity-form" onSubmit={submit}><FormField label="مشتری"><select name="customerId" required><option value="">انتخاب کن</option>{data.customers.map(item=><option value={item.id} key={item.id}>{item.displayName}</option>)}</select></FormField><FormField label="نوع"><select name="interactionType"><option value="Call">تماس</option><option value="Message">پیام</option><option value="Meeting">جلسه</option><option value="FollowUp">پیگیری</option><option value="Note">یادداشت</option></select></FormField><FormField label="موضوع" wide><input name="subject" required/></FormField><FormField label="شرح" wide><textarea name="notes"/></FormField><FormField label="زمان پیگیری بعدی"><input name="nextFollowUpAt" type="datetime-local"/></FormField><InlineError message={formState.error}/><FormActions saving={formState.saving} submitLabel="ثبت تعامل" onCancel={close}/></form></Modal></ModuleBody>;
}

function SettingsPage({ onNotice }: RouteProps) {
  const auth=useAuthentication();const{data,request,refresh,refreshing}=useOperations();const[formState,setFormState]=useState<FormState>(initialFormState);
  const submit=async(event:FormEvent<HTMLFormElement>)=>{event.preventDefault();const form=new FormData(event.currentTarget);setFormState({saving:true,error:null});try{await request<StoreProfile>("/api/v1/settings/store-profile",{method:"PUT",body:JSON.stringify({tradeName:textValue(form,"tradeName"),legalName:textValue(form,"legalName"),nationalId:optionalText(form,"nationalId"),economicCode:optionalText(form,"economicCode"),registrationNumber:optionalText(form,"registrationNumber"),phoneNumber:textValue(form,"phoneNumber"),postalCode:textValue(form,"postalCode"),addressLine:textValue(form,"addressLine"),rowVersion:data.storeProfile?.rowVersion||null})});onNotice("مشخصات فروشگاه ذخیره شد.");await refresh();setFormState(initialFormState);}catch(error){setFormState({saving:false,error:messageOf(error)});}};
  return <ModuleBody><PageHeader icon={Settings} title="تنظیمات" description="هویت فروشگاه، اتصال سرور و نشست‌های فعال." secondary={<RefreshButton refreshing={refreshing} onClick={()=>void refresh()}/>} /><DataNotice /><div className="settings-grid"><section className="lux-card settings-card"><h2>مشخصات فروشگاه</h2><form className="entity-form" onSubmit={submit}><FormField label="نام تجاری"><input name="tradeName" defaultValue={data.storeProfile?.tradeName||"مِزون وندوم"} required/></FormField><FormField label="نام حقوقی"><input name="legalName" defaultValue={data.storeProfile?.legalName||"وندوم"} required/></FormField><FormField label="شناسه ملی"><input name="nationalId" defaultValue={data.storeProfile?.nationalId||""}/></FormField><FormField label="کد اقتصادی"><input name="economicCode" defaultValue={data.storeProfile?.economicCode||""}/></FormField><FormField label="شماره ثبت"><input name="registrationNumber" defaultValue={data.storeProfile?.registrationNumber||""}/></FormField><FormField label="تلفن"><input name="phoneNumber" defaultValue={data.storeProfile?.phoneNumber||""} required/></FormField><FormField label="کد پستی"><input name="postalCode" defaultValue={data.storeProfile?.postalCode||""} required/></FormField><FormField label="نشانی" wide><textarea name="addressLine" defaultValue={data.storeProfile?.addressLine||""} required/></FormField><InlineError message={formState.error}/><div className="form-actions"><button className="primary-button" type="submit" disabled={formState.saving}>{formState.saving?"در حال ذخیره…":"ذخیره تنظیمات"}</button></div></form></section><aside className="settings-stack"><section className="lux-card settings-card"><h2>اتصال برنامه</h2><dl><div><dt>آدرس API</dt><dd dir="ltr">{auth.runtime?.apiBaseUrl}</dd></div><div><dt>میزبان</dt><dd>{auth.runtime?.isDesktop?"Windows Desktop":"مرورگر"}</dd></div><div><dt>ارتباط</dt><dd>{auth.runtime?.isInsecureTransport?"ناامن (HTTP)":"امن (HTTPS)"}</dd></div></dl></section><section className="lux-card settings-card"><h2>نشست‌های ورود</h2>{data.sessions.map(session=><div className="session-row" key={session.id}><span>{session.isCurrent?"این دستگاه":"نشست دیگر"}<small>{formatDate(session.lastSeenAt)}</small></span><StatusBadge status={session.revokedAt?"Inactive":"Active"}/></div>)}</section></aside></div></ModuleBody>;
}

function ManagerProfilePage({ onNavigate }: RouteProps) {
  const auth = useAuthentication();
  const { data } = useOperations();
  const user = auth.user;
  const currentSession = data.sessions.find((session) => session.isCurrent);
  const role = user?.roles.includes("Owner")
    ? "مالک مجموعه"
    : user?.roles.includes("Admin")
      ? "مدیر مجموعه"
      : "کاربر";
  return <ModuleBody>
    <PageHeader icon={UserRound} title="پروفایل مدیر" description="اطلاعات حساب فعال، سطح دسترسی و وضعیت امنیتی این نشست." secondary={<button className="secondary-button" type="button" onClick={() => onNavigate("/settings")}>تنظیمات حساب</button>} />
    <div className="profile-detail-grid">
      <section className="lux-card manager-profile-card">
        <span className="manager-profile-avatar">{user?.displayName?.slice(0, 1) || "و"}</span>
        <div><p className="eyebrow gold-text">{role}</p><h2>{user?.displayName || "کاربر وندوم"}</h2><span className="status-badge is-positive">حساب فعال</span></div>
      </section>
      <section className="lux-card profile-facts-card">
        <div><Mail size={18} /><span><small>ایمیل مدیریتی</small><strong dir="ltr">{user?.email || "—"}</strong></span></div>
        <div><ShieldCheck size={18} /><span><small>نقش‌ها</small><strong>{user?.roles.map(translateStatus).join("، ") || "—"}</strong></span></div>
        <div><KeyRound size={18} /><span><small>ورود دومرحله‌ای</small><strong>{user?.mfaEnabled ? "فعال" : "در انتظار فعال‌سازی"}</strong></span></div>
        <div><Monitor size={18} /><span><small>نشست فعلی</small><strong>{currentSession ? `فعال از ${formatDate(currentSession.createdAt)}` : "نشست امن فعال"}</strong></span></div>
      </section>
    </div>
    <section className="lux-card profile-permissions-card"><header><h2>دسترسی‌های حساب</h2><span>{user?.permissions.length || 0} مجوز فعال</span></header><div className="chip-list">{user?.permissions.map((permission) => <span key={permission}>{permission}</span>)}</div></section>
  </ModuleBody>;
}

function SearchPage({ path, onNavigate }: RouteProps) {
  const {data}=useOperations();const query=decodeURIComponent(new URLSearchParams(path.split("?")[1]||"").get("q")||"").trim().toLocaleLowerCase("fa");
  const matches=(value:string|undefined|null)=>Boolean(query&&value?.toLocaleLowerCase("fa").includes(query));
  const results=[...data.invoices.filter(item=>matches(item.invoiceNumber)||matches(item.customerNameSnapshot)).map(item=>({id:item.id,type:"فاکتور",title:item.invoiceNumber,detail:item.customerNameSnapshot||"",path:"/invoices"})),...data.orders.filter(item=>matches(item.orderNumber)||matches(item.customerNameSnapshot)).map(item=>({id:item.id,type:"سفارش",title:item.orderNumber,detail:item.customerNameSnapshot||"",path:"/orders"})),...data.products.filter(item=>matches(item.name)||matches(item.slug)||item.variants.some(variant=>matches(variant.sku))).map(item=>({id:item.id,type:"محصول",title:item.name,detail:item.variants.map(variant=>variant.sku).join("، "),path:"/products"})),...data.customers.filter(item=>matches(item.displayName)||matches(item.phoneNumber)).map(item=>({id:item.id,type:"مشتری",title:item.displayName,detail:item.phoneNumber||"",path:"/customers"})),...data.suppliers.filter(item=>matches(item.name)||matches(item.code)||matches(item.phoneNumber)).map(item=>({id:item.id,type:"تأمین‌کننده",title:item.name,detail:item.code,path:"/suppliers"}))];
  return <ModuleBody><PageHeader icon={Search} title="نتایج جست‌وجو" description={query?`نتایج واقعی برای «${query}»`:"عبارت موردنظر را در نوار بالای برنامه وارد کن."}/>{results.length?<div className="search-results">{results.map(result=><button className="lux-card search-result" type="button" key={`${result.type}-${result.id}`} onClick={()=>onNavigate(result.path)}><span>{result.type}</span><div><strong>{result.title}</strong><small>{result.detail}</small></div><ChevronLeft size={17}/></button>)}</div>:<EmptyState title={query?"نتیجه‌ای پیدا نشد":"عبارتی وارد نشده"} description="جست‌وجو در فاکتور، سفارش، محصول، مشتری و تأمین‌کننده انجام می‌شود."/>}</ModuleBody>;
}

function NotFoundPage({ onNavigate }: RouteProps) { return <ModuleBody><EmptyState title="این صفحه پیدا نشد" description="از منوی کناری یکی از بخش‌های مدیریت را انتخاب کن."/><button className="primary-button centered-action" type="button" onClick={()=>onNavigate("/")}>بازگشت به داشبورد</button></ModuleBody>; }

export function OperationsRouter(props: RouteProps) {
  const base=props.path.split("?")[0];
  if(base.startsWith("/invoices"))return <InvoicesPage {...props}/>;
  if(base.startsWith("/customers"))return <CustomersPage {...props}/>;
  if(base.startsWith("/products"))return <ProductsPage {...props}/>;
  if(base.startsWith("/inventory"))return <InventoryPage {...props}/>;
  if(base.startsWith("/orders/new"))return <NewOrderPage {...props}/>;
  if(base.startsWith("/orders"))return <OrdersPage {...props}/>;
  if(base.startsWith("/accounting"))return <AccountingPage/>;
  if(base.startsWith("/reports"))return <ReportsPage/>;
  if(base.startsWith("/employees"))return <EmployeesPage {...props}/>;
  if(base.startsWith("/suppliers"))return <SuppliersPage {...props}/>;
  if(base.startsWith("/crm"))return <CrmPage {...props}/>;
  if(base.startsWith("/settings"))return <SettingsPage {...props}/>;
  if(base.startsWith("/profile"))return <ManagerProfilePage {...props}/>;
  if(base.startsWith("/search"))return <SearchPage {...props}/>;
  return <NotFoundPage {...props}/>;
}
