import { Check, PackagePlus, RefreshCw, Truck, Warehouse as WarehouseIcon } from "lucide-react";
import { type ChangeEvent, type FormEvent, useEffect, useMemo, useRef, useState } from "react";
import { tomansToRials } from "../../lib/money";
import { useOperations } from "./OperationsContext";
import {
  EmptyState,
  FormActions,
  FormField,
  formatDate,
  formatMoney,
  InlineError,
  MetricTile,
  Modal,
  PageHeader,
  RefreshButton,
  StatusBadge,
  TableCard,
} from "./PagePrimitives";
import type {
  Product,
  ProductVariant,
  Supplier,
  SupplierPurchase,
  Warehouse,
} from "./operations.types";

interface SupplierPurchasesPageProps {
  path: string;
  onNavigate: (path: string) => void;
  onNotice: (message: string) => void;
}

type ResourceMode = "existing" | "new";

interface PurchaseDraft {
  supplierId: string;
  warehouseMode: ResourceMode;
  warehouseId: string;
  warehouseCode: string;
  warehouseName: string;
  productMode: ResourceMode;
  variantId: string;
  categoryId: string;
  productName: string;
  productDescription: string;
  variantName: string;
  sku: string;
  quantity: number;
  unitCostTomans: number;
  sellingPriceTomans: number;
  purchasedAt: string;
  supplierReference: string;
  notes: string;
  karat: number;
  grossWeight: number;
  netGoldWeight: number;
  stoneWeight: number;
  otherMaterialWeight: number;
  hasStone: boolean;
  isWeightVariable: boolean;
}

const steps = [
  "تأمین‌کننده",
  "انبار",
  "محصول",
  "قیمت و تعداد",
  "تصویر و مشخصات",
  "تأیید نهایی",
] as const;

function localDateTimeValue(): string {
  const now = new Date();
  return new Date(now.getTime() - now.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
}

function createDraft(supplierId = ""): PurchaseDraft {
  return {
    supplierId,
    warehouseMode: "existing",
    warehouseId: "",
    warehouseCode: "",
    warehouseName: "",
    productMode: "new",
    variantId: "",
    categoryId: "",
    productName: "",
    productDescription: "",
    variantName: "مدل اصلی",
    sku: "",
    quantity: 1,
    unitCostTomans: 0,
    sellingPriceTomans: 0,
    purchasedAt: localDateTimeValue(),
    supplierReference: "",
    notes: "",
    karat: 18,
    grossWeight: 1,
    netGoldWeight: 1,
    stoneWeight: 0,
    otherMaterialWeight: 0,
    hasStone: false,
    isWeightVariable: false,
  };
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : "عملیات کامل نشد.";
}

function makeSlug(sku: string): string {
  const normalized = sku.toLocaleLowerCase("en-US").replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
  return `purchase-${normalized || "item"}-${Date.now().toString(36)}`;
}

export function SupplierPurchasesPage({ path, onNotice }: SupplierPurchasesPageProps) {
  const { data, request, refresh, refreshing } = useOperations();
  const [supplierModalOpen, setSupplierModalOpen] = useState(false);
  const [supplierSaving, setSupplierSaving] = useState(false);
  const [supplierError, setSupplierError] = useState<string | null>(null);
  const [wizardOpen, setWizardOpen] = useState(false);
  const [step, setStep] = useState(0);
  const [draft, setDraft] = useState<PurchaseDraft>(() => createDraft());
  const [imageFile, setImageFile] = useState<File | null>(null);
  const [wizardError, setWizardError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const queryOpened = useRef(false);
  const createdWarehouseId = useRef<string | null>(null);
  const createdProductId = useRef<string | null>(null);
  const createdVariantId = useRef<string | null>(null);
  const imageUploaded = useRef(false);

  const variants = useMemo(
    () => data.products.flatMap((product) =>
      product.variants.map((variant) => ({ ...variant, productName: product.name }))),
    [data.products],
  );

  const activeSuppliers = data.suppliers.filter((item) => item.isActive);
  const activeWarehouses = data.warehouses.filter((item) => item.isActive);
  const activeVariants = variants.filter((item) => item.isActive);
  const selectedSupplier = data.suppliers.find((item) => item.id === draft.supplierId);
  const selectedWarehouse = data.warehouses.find((item) => item.id === draft.warehouseId);
  const selectedVariant = variants.find((item) => item.id === draft.variantId);
  const expectedProfitRials = tomansToRials(
    Math.round((draft.sellingPriceTomans - draft.unitCostTomans) * draft.quantity),
  );

  const update = <K extends keyof PurchaseDraft>(key: K, value: PurchaseDraft[K]) => {
    setDraft((current) => ({ ...current, [key]: value }));
    setWizardError(null);
  };

  const resetCreatedResources = () => {
    createdWarehouseId.current = null;
    createdProductId.current = null;
    createdVariantId.current = null;
    imageUploaded.current = false;
  };

  const openWizard = (supplierId = "") => {
    const fallbackSupplier = activeSuppliers[0]?.id ?? "";
    setDraft(createDraft(supplierId || fallbackSupplier));
    setImageFile(null);
    setWizardError(null);
    setSaving(false);
    setStep(0);
    resetCreatedResources();
    setWizardOpen(true);
  };

  const closeWizard = () => {
    if (saving) return;
    setWizardOpen(false);
    setWizardError(null);
  };

  useEffect(() => {
    if (queryOpened.current || !new URLSearchParams(path.split("?")[1] || "").has("purchase")) return;
    if (!activeSuppliers.length) return;
    queryOpened.current = true;
    openWizard(activeSuppliers[0].id);
  }, [activeSuppliers, path]);

  const validateStep = (index: number): string | null => {
    if (index === 0 && !draft.supplierId) return "یک تأمین‌کننده فعال انتخاب کن.";
    if (index === 1) {
      if (draft.warehouseMode === "existing" && !draft.warehouseId) return "انبار مقصد را انتخاب کن.";
      if (draft.warehouseMode === "new" && (!draft.warehouseCode.trim() || !draft.warehouseName.trim())) {
        return "کد و نام انبار جدید را وارد کن.";
      }
    }
    if (index === 2) {
      if (draft.productMode === "existing" && !draft.variantId) return "محصول موجود را انتخاب کن.";
      if (draft.productMode === "new" && (!draft.productName.trim() || !draft.variantName.trim() || !draft.sku.trim())) {
        return "نام محصول، نام مدل و کد SKU را کامل کن.";
      }
    }
    if (index === 3) {
      if (!Number.isInteger(draft.quantity) || draft.quantity < 1) return "تعداد خرید باید یک عدد صحیح مثبت باشد.";
      if (!Number.isSafeInteger(draft.unitCostTomans) || draft.unitCostTomans < 0) return "قیمت خرید را به تومان و به‌صورت عدد صحیح وارد کن.";
      if (!Number.isSafeInteger(draft.sellingPriceTomans) || draft.sellingPriceTomans < 1) return "قیمت فروش را به تومان و به‌صورت عدد صحیح وارد کن.";
    }
    if (index === 4 && draft.productMode === "new") {
      if (!imageFile) return "برای محصول جدید یک تصویر انتخاب کن.";
      if (imageFile.size > 5 * 1024 * 1024) return "حجم تصویر نباید بیشتر از ۵ مگابایت باشد.";
      if (draft.karat < 1 || draft.karat > 24) return "عیار باید بین ۱ تا ۲۴ باشد.";
      if (draft.grossWeight <= 0 || draft.netGoldWeight <= 0) return "وزن ناخالص و وزن خالص باید بیشتر از صفر باشند.";
      if (draft.netGoldWeight > draft.grossWeight) return "وزن خالص نمی‌تواند بیشتر از وزن ناخالص باشد.";
    }
    return null;
  };

  const nextStep = () => {
    const error = validateStep(step);
    if (error) {
      setWizardError(error);
      return;
    }
    setWizardError(null);
    setStep((current) => Math.min(current + 1, steps.length - 1));
  };

  const submitSupplier = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    setSupplierSaving(true);
    setSupplierError(null);
    try {
      const supplier = await request<Supplier>("/api/v1/suppliers", {
        method: "POST",
        body: JSON.stringify({
          code: String(form.get("code") || "").trim(),
          name: String(form.get("name") || "").trim(),
          contactName: String(form.get("contactName") || "").trim() || null,
          phoneNumber: String(form.get("phoneNumber") || "").trim() || null,
          email: String(form.get("email") || "").trim() || null,
          nationalId: String(form.get("nationalId") || "").trim() || null,
          addressLine: String(form.get("addressLine") || "").trim() || null,
          notes: String(form.get("notes") || "").trim() || null,
        }),
      });
      await refresh();
      setSupplierModalOpen(false);
      setSupplierSaving(false);
      onNotice("تأمین‌کننده ثبت شد؛ حالا خرید را تکمیل کن.");
      openWizard(supplier.id);
    } catch (error) {
      setSupplierSaving(false);
      setSupplierError(messageOf(error));
    }
  };

  const submitPurchase = async () => {
    const finalError = [0, 1, 2, 3, 4].map(validateStep).find(Boolean);
    if (finalError) {
      setWizardError(finalError);
      return;
    }

    setSaving(true);
    setWizardError(null);
    try {
      let warehouseId = draft.warehouseId;
      if (draft.warehouseMode === "new") {
        if (!createdWarehouseId.current) {
          const warehouse = await request<Warehouse>("/api/v1/inventory/warehouses", {
            method: "POST",
            body: JSON.stringify({ code: draft.warehouseCode.trim(), name: draft.warehouseName.trim() }),
          });
          createdWarehouseId.current = warehouse.id;
        }
        warehouseId = createdWarehouseId.current ?? "";
      }

      let variantId = draft.variantId;
      if (draft.productMode === "new") {
        if (!createdProductId.current) {
          const product = await request<Product>("/api/v1/catalog/products", {
            method: "POST",
            body: JSON.stringify({
              productCategoryId: draft.categoryId || null,
              name: draft.productName.trim(),
              slug: makeSlug(draft.sku),
              description: draft.productDescription.trim() || null,
            }),
          });
          createdProductId.current = product.id;
        }
        if (!createdVariantId.current) {
          const variant = await request<ProductVariant>(
            `/api/v1/catalog/products/${createdProductId.current}/variants`,
            {
              method: "POST",
              body: JSON.stringify({
                sku: draft.sku.trim(),
                name: draft.variantName.trim(),
                goldDetail: {
                  karat: draft.karat,
                  grossWeight: draft.grossWeight,
                  netGoldWeight: draft.netGoldWeight,
                  stoneWeight: draft.stoneWeight,
                  otherMaterialWeight: draft.otherMaterialWeight,
                  manufacturingWageType: "FixedRials",
                  manufacturingWageValue: 0,
                  profitPercentage: 0,
                  taxPercentage: 0,
                  hasStone: draft.hasStone,
                  isWeightVariable: draft.isWeightVariable,
                },
              }),
            },
          );
          createdVariantId.current = variant.id;
        }
        variantId = createdVariantId.current ?? "";

        if (!imageUploaded.current && imageFile) {
          const imageBody = new FormData();
          imageBody.append("file", imageFile);
          imageBody.append("altText", draft.productName.trim());
          await request(`/api/v1/catalog/products/${createdProductId.current}/image`, {
            method: "PUT",
            body: imageBody,
          });
          imageUploaded.current = true;
        }
      }

      await request<SupplierPurchase>("/api/v1/inventory/supplier-purchases", {
        method: "POST",
        body: JSON.stringify({
          supplierId: draft.supplierId,
          warehouseId,
          productVariantId: variantId,
          quantity: draft.quantity,
          unitCostRials: tomansToRials(draft.unitCostTomans),
          sellingUnitPriceRials: tomansToRials(draft.sellingPriceTomans),
          purchasedAt: draft.purchasedAt ? new Date(draft.purchasedAt).toISOString() : null,
          supplierReference: draft.supplierReference.trim() || null,
          notes: draft.notes.trim() || null,
        }),
      });

      setWizardOpen(false);
      setSaving(false);
      onNotice("خرید تأیید شد؛ انبار، محصول، قیمت فروش و موجودی به‌روز شدند.");
      await refresh();
    } catch (error) {
      setSaving(false);
      setWizardError(messageOf(error));
    }
  };

  const handleImage = (event: ChangeEvent<HTMLInputElement>) => {
    setImageFile(event.target.files?.[0] ?? null);
    setWizardError(null);
  };

  const totalPurchaseCost = data.supplierPurchases.reduce((sum, item) => sum + item.totalCostRials, 0);
  const totalExpectedProfit = data.supplierPurchases.reduce((sum, item) => sum + item.expectedTotalProfitRials, 0);

  return (
    <div className="module-page">
      <PageHeader
        icon={Truck}
        title="تأمین‌کنندگان و خرید"
        description="خرید را از تأمین‌کننده شروع کن؛ انبار، محصول، قیمت و تصویر در یک مسیر کوتاه ثبت می‌شوند."
        actionLabel="خرید جدید"
        onAction={() => openWizard()}
        secondary={
          <>
            <button className="secondary-button" type="button" onClick={() => setSupplierModalOpen(true)}>
              <PackagePlus size={16} /> تأمین‌کننده جدید
            </button>
            <RefreshButton refreshing={refreshing} onClick={() => void refresh()} />
          </>
        }
      />

      <div className="module-metrics-grid">
        <MetricTile label="خریدهای ثبت‌شده" value={String(data.supplierPurchases.length)} hint="سند خرید" />
        <MetricTile label="جمع بهای خرید" value={formatMoney(totalPurchaseCost)} hint="بهای تمام‌شده" />
        <MetricTile label="سود مورد انتظار" value={formatMoney(totalExpectedProfit)} hint="پیش از فروش و تخفیف" />
      </div>

      {data.suppliers.length ? (
        <TableCard>
          <div className="table-scroll">
            <table className="data-table">
              <thead><tr><th>کد</th><th>نام</th><th>مسئول تماس</th><th>تلفن</th><th>وضعیت</th><th /></tr></thead>
              <tbody>
                {data.suppliers.map((supplier) => (
                  <tr key={supplier.id}>
                    <td>{supplier.code}</td><td><strong>{supplier.name}</strong></td>
                    <td>{supplier.contactName || "—"}</td><td>{supplier.phoneNumber || "—"}</td>
                    <td><StatusBadge status={supplier.isActive ? "Active" : "Inactive"} /></td>
                    <td>{supplier.isActive && <button className="row-action" type="button" onClick={() => openWizard(supplier.id)}>شروع خرید</button>}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </TableCard>
      ) : (
        <EmptyState title="تأمین‌کننده‌ای ثبت نشده" description="ابتدا تأمین‌کننده را ثبت کن؛ سپس ویزارد خرید خودکار باز می‌شود." />
      )}

      {data.supplierPurchases.length > 0 && (
        <section className="module-subsection">
          <h2 className="section-title">آخرین خریدها</h2>
          <TableCard><div className="table-scroll"><table className="data-table">
            <thead><tr><th>سند</th><th>تأمین‌کننده</th><th>محصول</th><th>تعداد</th><th>خرید واحد</th><th>فروش واحد</th><th>سود مورد انتظار</th><th>تاریخ</th></tr></thead>
            <tbody>{data.supplierPurchases.map((purchase) => <tr key={purchase.id}>
              <td>{purchase.purchaseNumber}</td><td>{purchase.supplierName}</td>
              <td><strong>{purchase.productName} · {purchase.variantName}</strong><small>{purchase.sku}</small></td>
              <td>{purchase.quantity}</td><td>{formatMoney(purchase.unitCostRials)}</td>
              <td>{formatMoney(purchase.sellingUnitPriceRials)}</td>
              <td><strong className={purchase.expectedTotalProfitRials < 0 ? "negative-text" : "positive-text"}>{formatMoney(purchase.expectedTotalProfitRials)}</strong></td>
              <td>{formatDate(purchase.purchasedAt)}</td>
            </tr>)}</tbody>
          </table></div></TableCard>
        </section>
      )}

      <Modal open={supplierModalOpen} title="ثبت تأمین‌کننده" onClose={() => !supplierSaving && setSupplierModalOpen(false)}>
        <form className="entity-form" onSubmit={submitSupplier}>
          <FormField label="کد"><input name="code" dir="ltr" maxLength={50} required /></FormField>
          <FormField label="نام مجموعه"><input name="name" maxLength={200} required /></FormField>
          <FormField label="مسئول تماس"><input name="contactName" /></FormField>
          <FormField label="تلفن"><input name="phoneNumber" /></FormField>
          <FormField label="ایمیل"><input name="email" type="email" dir="ltr" /></FormField>
          <FormField label="شناسه ملی"><input name="nationalId" /></FormField>
          <FormField label="نشانی" wide><textarea name="addressLine" /></FormField>
          <FormField label="یادداشت" wide><textarea name="notes" /></FormField>
          <InlineError message={supplierError} />
          <FormActions saving={supplierSaving} submitLabel="ثبت و شروع خرید" onCancel={() => setSupplierModalOpen(false)} />
        </form>
      </Modal>

      <Modal
        open={wizardOpen}
        title="ثبت خرید تأمین‌کننده"
        description="همه مراحل در همین پنجره انجام می‌شود؛ ثبت نهایی در مرحله آخر است."
        onClose={closeWizard}
      >
        <div className="purchase-wizard">
          <ol className="wizard-steps" aria-label="مراحل ثبت خرید">
            {steps.map((label, index) => (
              <li className={index === step ? "is-current" : index < step ? "is-complete" : ""} key={label}>
                <button type="button" onClick={() => index < step && setStep(index)} disabled={index > step || saving}>
                  <span>{index < step ? <Check size={15} /> : index + 1}</span><small>{label}</small>
                </button>
              </li>
            ))}
          </ol>

          <div className="wizard-panel">
            {step === 0 && <section>
              <h3>۱. تأمین‌کننده را انتخاب کن</h3>
              <p>شروع عملیات از تأمین‌کننده است؛ فقط تأمین‌کننده‌های فعال نمایش داده می‌شوند.</p>
              <FormField label="تأمین‌کننده" wide><select value={draft.supplierId} onChange={(event) => update("supplierId", event.target.value)} required>
                <option value="">انتخاب کن</option>{activeSuppliers.map((item) => <option value={item.id} key={item.id}>{item.name} ({item.code})</option>)}
              </select></FormField>
              {!activeSuppliers.length && <EmptyState title="تأمین‌کننده فعال وجود ندارد" description="این پنجره را ببند و یک تأمین‌کننده جدید ثبت کن." />}
            </section>}

            {step === 1 && <section>
              <h3>۲. انبار مقصد</h3><p>یک انبار موجود را انتخاب کن یا همین‌جا انبار جدید بساز.</p>
              <div className="wizard-mode-switch">
                <button type="button" className={draft.warehouseMode === "existing" ? "is-active" : ""} onClick={() => update("warehouseMode", "existing")}><WarehouseIcon size={17} /> انبار موجود</button>
                <button type="button" className={draft.warehouseMode === "new" ? "is-active" : ""} onClick={() => update("warehouseMode", "new")}><PackagePlus size={17} /> انبار جدید</button>
              </div>
              {draft.warehouseMode === "existing" ? <div className="entity-form">
                <FormField label="انبار" wide><select value={draft.warehouseId} onChange={(event) => update("warehouseId", event.target.value)}><option value="">انتخاب کن</option>{activeWarehouses.map((item) => <option value={item.id} key={item.id}>{item.name} ({item.code})</option>)}</select></FormField>
              </div> : <div className="entity-form">
                <FormField label="کد انبار"><input value={draft.warehouseCode} maxLength={50} dir="ltr" onChange={(event) => update("warehouseCode", event.target.value)} /></FormField>
                <FormField label="نام انبار"><input value={draft.warehouseName} maxLength={200} onChange={(event) => update("warehouseName", event.target.value)} /></FormField>
              </div>}
            </section>}

            {step === 2 && <section>
              <h3>۳. محصول و مدل</h3><p>می‌توانی محصول موجود را انتخاب کنی یا محصول جدید را در همین خرید بسازی.</p>
              <div className="wizard-mode-switch">
                <button type="button" className={draft.productMode === "new" ? "is-active" : ""} onClick={() => update("productMode", "new")}><PackagePlus size={17} /> محصول جدید</button>
                <button type="button" className={draft.productMode === "existing" ? "is-active" : ""} onClick={() => update("productMode", "existing")}><Check size={17} /> محصول موجود</button>
              </div>
              {draft.productMode === "existing" ? <div className="entity-form">
                <FormField label="محصول و مدل" wide><select value={draft.variantId} onChange={(event) => update("variantId", event.target.value)}><option value="">انتخاب کن</option>{activeVariants.map((item) => <option value={item.id} key={item.id}>{item.productName} · {item.name} ({item.sku})</option>)}</select></FormField>
              </div> : <div className="entity-form">
                <FormField label="نام محصول"><input value={draft.productName} maxLength={200} onChange={(event) => update("productName", event.target.value)} /></FormField>
                <FormField label="دسته‌بندی"><select value={draft.categoryId} onChange={(event) => update("categoryId", event.target.value)}><option value="">بدون دسته‌بندی</option>{data.categories.filter((item) => item.isActive).map((item) => <option value={item.id} key={item.id}>{item.name}</option>)}</select></FormField>
                <FormField label="نام مدل"><input value={draft.variantName} maxLength={200} onChange={(event) => update("variantName", event.target.value)} /></FormField>
                <FormField label="کد SKU"><input value={draft.sku} maxLength={64} dir="ltr" onChange={(event) => update("sku", event.target.value)} /></FormField>
                <FormField label="توضیحات محصول" wide><textarea value={draft.productDescription} maxLength={4000} onChange={(event) => update("productDescription", event.target.value)} /></FormField>
              </div>}
            </section>}

            {step === 3 && <section>
              <h3>۴. تعداد و قیمت‌ها</h3><p>همه مبالغ این فرم و بخش‌های قابل مشاهده سامانه به تومان هستند.</p>
              <div className="entity-form">
                <FormField label="تعداد خرید"><input type="number" min="1" step="1" value={draft.quantity} onChange={(event) => update("quantity", Number(event.target.value))} /></FormField>
                <FormField label="قیمت خرید هر واحد (تومان)"><input type="number" min="0" step="1" value={draft.unitCostTomans} onChange={(event) => update("unitCostTomans", Number(event.target.value))} /></FormField>
                <FormField label="قیمت فروش هر واحد (تومان)"><input type="number" min="1" step="1" value={draft.sellingPriceTomans} onChange={(event) => update("sellingPriceTomans", Number(event.target.value))} /></FormField>
                <FormField label="زمان خرید"><input type="datetime-local" value={draft.purchasedAt} onChange={(event) => update("purchasedAt", event.target.value)} /></FormField>
                <FormField label="شماره فاکتور تأمین‌کننده"><input maxLength={100} value={draft.supplierReference} onChange={(event) => update("supplierReference", event.target.value)} /></FormField>
                <FormField label="یادداشت" wide><textarea maxLength={1000} value={draft.notes} onChange={(event) => update("notes", event.target.value)} /></FormField>
              </div>
              <div className={`purchase-profit-preview ${expectedProfitRials < 0 ? "is-negative" : ""}`}><span>سود مورد انتظار این خرید</span><strong>{formatMoney(expectedProfitRials)}</strong><small>اختلاف قیمت فروش و خرید × تعداد</small></div>
            </section>}

            {step === 4 && <section>
              <h3>۵. تصویر و مشخصات تکمیلی</h3>
              {draft.productMode === "existing" ? <div className="wizard-existing-summary"><Check size={24} /><div><strong>مشخصات محصول موجود حفظ می‌شود</strong><p>تصویر و مشخصات قبلی تغییر نمی‌کنند؛ فقط خرید، موجودی و قیمت فروش ثبت می‌شود.</p></div></div> : <>
                <p>تصویر اصلی و مشخصات طلا را پیش از تأیید نهایی اضافه کن.</p>
                <div className="entity-form">
                  <FormField label="تصویر محصول" hint="JPG، PNG یا WebP؛ حداکثر ۵ مگابایت" wide><input type="file" accept="image/jpeg,image/png,image/webp" onChange={handleImage} /></FormField>
                  <FormField label="عیار"><input type="number" min="1" max="24" step="1" value={draft.karat} onChange={(event) => update("karat", Number(event.target.value))} /></FormField>
                  <FormField label="وزن ناخالص (گرم)"><input type="number" min="0.001" step="0.001" value={draft.grossWeight} onChange={(event) => update("grossWeight", Number(event.target.value))} /></FormField>
                  <FormField label="وزن خالص طلا (گرم)"><input type="number" min="0.001" step="0.001" value={draft.netGoldWeight} onChange={(event) => update("netGoldWeight", Number(event.target.value))} /></FormField>
                  <FormField label="وزن سنگ (گرم)"><input type="number" min="0" step="0.001" value={draft.stoneWeight} onChange={(event) => update("stoneWeight", Number(event.target.value))} /></FormField>
                  <FormField label="وزن سایر مواد (گرم)"><input type="number" min="0" step="0.001" value={draft.otherMaterialWeight} onChange={(event) => update("otherMaterialWeight", Number(event.target.value))} /></FormField>
                  <label className="checkbox-field"><input type="checkbox" checked={draft.hasStone} onChange={(event) => update("hasStone", event.target.checked)} /><span>دارای سنگ</span></label>
                  <label className="checkbox-field"><input type="checkbox" checked={draft.isWeightVariable} onChange={(event) => update("isWeightVariable", event.target.checked)} /><span>وزن متغیر</span></label>
                </div>
              </>}
            </section>}

            {step === 5 && <section>
              <h3>۶. بررسی و تأیید</h3><p>بعد از تأیید، خرید و همه موارد جدید به‌ترتیب ثبت می‌شوند.</p>
              <dl className="wizard-review">
                <div><dt>تأمین‌کننده</dt><dd>{selectedSupplier?.name || "—"}</dd></div>
                <div><dt>انبار</dt><dd>{draft.warehouseMode === "new" ? `${draft.warehouseName} (جدید)` : selectedWarehouse?.name || "—"}</dd></div>
                <div><dt>محصول</dt><dd>{draft.productMode === "new" ? `${draft.productName} · ${draft.variantName}` : selectedVariant ? `${selectedVariant.productName} · ${selectedVariant.name}` : "—"}</dd></div>
                <div><dt>تعداد</dt><dd>{draft.quantity}</dd></div>
                <div><dt>قیمت خرید واحد</dt><dd>{formatMoney(tomansToRials(draft.unitCostTomans))}</dd></div>
                <div><dt>قیمت فروش واحد</dt><dd>{formatMoney(tomansToRials(draft.sellingPriceTomans))}</dd></div>
                <div><dt>بهای کل خرید</dt><dd>{formatMoney(tomansToRials(draft.unitCostTomans * draft.quantity))}</dd></div>
                <div><dt>سود مورد انتظار</dt><dd className={expectedProfitRials < 0 ? "negative-text" : "positive-text"}>{formatMoney(expectedProfitRials)}</dd></div>
              </dl>
            </section>}
          </div>

          <InlineError message={wizardError} />
          <div className="wizard-actions">
            <button className="secondary-button" type="button" onClick={step === 0 ? closeWizard : () => setStep((current) => current - 1)} disabled={saving}>{step === 0 ? "انصراف" : "مرحله قبل"}</button>
            {step < steps.length - 1 ? <button className="primary-button" type="button" onClick={nextStep} disabled={saving}>مرحله بعد</button> : <button className="primary-button" type="button" onClick={() => void submitPurchase()} disabled={saving}>{saving ? <><RefreshCw className="spin" size={16} /> در حال ثبت…</> : <><Check size={17} /> تأیید و ثبت خرید</>}</button>}
          </div>
        </div>
      </Modal>
    </div>
  );
}
