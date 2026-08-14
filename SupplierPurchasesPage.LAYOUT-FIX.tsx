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
  "ØªØ£Ù…ÛŒÙ†â€ŒÚ©Ù†Ù†Ø¯Ù‡",
  "Ø§Ù†Ø¨Ø§Ø±",
  "Ù…Ø­ØµÙˆÙ„",
  "Ù‚ÛŒÙ…Øª Ùˆ ØªØ¹Ø¯Ø§Ø¯",
  "ØªØµÙˆÛŒØ± Ùˆ Ù…Ø´Ø®ØµØ§Øª",
  "ØªØ£ÛŒÛŒØ¯ Ù†Ù‡Ø§ÛŒÛŒ",
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
    variantName: "Ù…Ø¯Ù„ Ø§ØµÙ„ÛŒ",
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
  return error instanceof Error ? error.message : "Ø¹Ù…Ù„ÛŒØ§Øª Ú©Ø§Ù…Ù„ Ù†Ø´Ø¯.";
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
    if (index === 0 && !draft.supplierId) return "ÛŒÚ© ØªØ£Ù…ÛŒÙ†â€ŒÚ©Ù†Ù†Ø¯Ù‡ ÙØ¹Ø§Ù„ Ø§Ù†ØªØ®Ø§Ø¨ Ú©Ù†.";
    if (index === 1) {
      if (draft.warehouseMode === "existing" && !draft.warehouseId) return "Ø§Ù†Ø¨Ø§Ø± Ù…Ù‚ØµØ¯ Ø±Ø§ Ø§Ù†ØªØ®Ø§Ø¨ Ú©Ù†.";
      if (draft.warehouseMode === "new" && (!draft.warehouseCode.trim() || !draft.warehouseName.trim())) {
        return "Ú©Ø¯ Ùˆ Ù†Ø§Ù… Ø§Ù†Ø¨Ø§Ø± Ø¬Ø¯ÛŒØ¯ Ø±Ø§ ÙˆØ§Ø±Ø¯ Ú©Ù†.";
      }
    }
    if (index === 2) {
      if (draft.productMode === "existing" && !draft.variantId) return "Ù…Ø­ØµÙˆÙ„ Ù…ÙˆØ¬ÙˆØ¯ Ø±Ø§ Ø§Ù†ØªØ®Ø§Ø¨ Ú©Ù†.";
      if (draft.productMode === "new" && (!draft.productName.trim() || !draft.variantName.trim() || !draft.sku.trim())) {
        return "Ù†Ø§Ù… Ù…Ø­ØµÙˆÙ„ØŒ Ù†Ø§Ù… Ù…Ø¯Ù„ Ùˆ Ú©Ø¯ SKU Ø±Ø§ Ú©Ø§Ù…Ù„ Ú©Ù†.";
      }
    }
    if (index === 3) {
      if (!Number.isInteger(draft.quantity) || draft.quantity < 1) return "ØªØ¹Ø¯Ø§Ø¯ Ø®Ø±ÛŒØ¯ Ø¨Ø§ÛŒØ¯ ÛŒÚ© Ø¹Ø¯Ø¯ ØµØ­ÛŒØ­ Ù…Ø«Ø¨Øª Ø¨Ø§Ø´Ø¯.";
      if (!Number.isSafeInteger(draft.unitCostTomans) || draft.unitCostTomans < 0) return "Ù‚ÛŒÙ…Øª Ø®Ø±ÛŒØ¯ Ø±Ø§ Ø¨Ù‡ ØªÙˆÙ…Ø§Ù† Ùˆ Ø¨Ù‡â€ŒØµÙˆØ±Øª Ø¹Ø¯Ø¯ ØµØ­ÛŒØ­ ÙˆØ§Ø±Ø¯ Ú©Ù†.";
      if (!Number.isSafeInteger(draft.sellingPriceTomans) || draft.sellingPriceTomans < 1) return "Ù‚ÛŒÙ…Øª ÙØ±ÙˆØ´ Ø±Ø§ Ø¨Ù‡ ØªÙˆÙ…Ø§Ù† Ùˆ Ø¨Ù‡â€ŒØµÙˆØ±Øª Ø¹Ø¯Ø¯ ØµØ­ÛŒØ­ ÙˆØ§Ø±Ø¯ Ú©Ù†.";
    }
    if (index === 4 && draft.productMode === "new") {
      if (!imageFile) return "Ø¨Ø±Ø§ÛŒ Ù…Ø­ØµÙˆÙ„ Ø¬Ø¯ÛŒØ¯ ÛŒÚ© ØªØµÙˆÛŒØ± Ø§Ù†ØªØ®Ø§Ø¨ Ú©Ù†.";
      if (imageFile.size > 5 * 1024 * 1024) return "Ø­Ø¬Ù… ØªØµÙˆÛŒØ± Ù†Ø¨Ø§ÛŒØ¯ Ø¨ÛŒØ´ØªØ± Ø§Ø² Ûµ Ù…Ú¯Ø§Ø¨Ø§ÛŒØª Ø¨Ø§Ø´Ø¯.";
      if (draft.karat < 1 || draft.karat > 24) return "Ø¹ÛŒØ§Ø± Ø¨Ø§ÛŒØ¯ Ø¨ÛŒÙ† Û± ØªØ§ Û²Û´ Ø¨Ø§Ø´Ø¯.";
      if (draft.grossWeight <= 0 || draft.netGoldWeight <= 0) return "ÙˆØ²Ù† Ù†Ø§Ø®Ø§Ù„Øµ Ùˆ ÙˆØ²Ù† Ø®Ø§Ù„Øµ Ø¨Ø§ÛŒØ¯ Ø¨ÛŒØ´ØªØ± Ø§Ø² ØµÙØ± Ø¨Ø§Ø´Ù†Ø¯.";
      if (draft.netGoldWeight > draft.grossWeight) return "ÙˆØ²Ù† Ø®Ø§Ù„Øµ Ù†Ù…ÛŒâ€ŒØªÙˆØ§Ù†Ø¯ Ø¨ÛŒØ´ØªØ± Ø§Ø² ÙˆØ²Ù† Ù†Ø§Ø®Ø§Ù„Øµ Ø¨Ø§Ø´Ø¯.";
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
      onNotice("ØªØ£Ù…ÛŒÙ†â€ŒÚ©Ù†Ù†Ø¯Ù‡ Ø«Ø¨Øª Ø´Ø¯Ø› Ø­Ø§Ù„Ø§ Ø®Ø±ÛŒØ¯ Ø±Ø§ ØªÚ©Ù…ÛŒÙ„ Ú©Ù†.");
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
      onNotice("Ø®Ø±ÛŒØ¯ ØªØ£ÛŒÛŒØ¯ Ø´Ø¯Ø› Ø§Ù†Ø¨Ø§Ø±ØŒ Ù…Ø­ØµÙˆÙ„ØŒ Ù‚ÛŒÙ…Øª ÙØ±ÙˆØ´ Ùˆ Ù…ÙˆØ¬ÙˆØ¯ÛŒ Ø¨Ù‡â€ŒØ±ÙˆØ² Ø´Ø¯Ù†Ø¯.");
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
    <main
      className="module-main supplier-purchases-page"
      dir="rtl"
      style={{
        height: "100dvh",
        minWidth: 0,
        overflowX: "hidden",
        overflowY: "auto",
        overscrollBehaviorY: "contain",
        scrollbarGutter: "stable",
      }}
    >
      <style>{`
        .supplier-purchases-page {
          width: 100%;
          max-width: 100vw;
        }

        .supplier-purchases-page .module-metrics-grid {
          grid-template-columns: repeat(3, minmax(0, 1fr));
        }

        .supplier-purchases-page .table-card,
        .supplier-purchases-page .table-scroll {
          min-width: 0;
          max-width: 100%;
        }

        .supplier-purchases-page .table-scroll {
          overflow-x: auto;
          overscroll-behavior-x: contain;
        }

        .modal-layer:has(.purchase-wizard) {
          align-items: flex-start;
          justify-items: center;
          padding: 12px;
          overflow-y: auto;
          overscroll-behavior: contain;
        }

        .modal-card:has(.purchase-wizard) {
          display: flex;
          width: min(940px, calc(100vw - 24px));
          max-height: calc(100dvh - 24px);
          min-height: 0;
          flex-direction: column;
          overflow: hidden;
        }

        .modal-card:has(.purchase-wizard) > header {
          flex: 0 0 auto;
        }

        .modal-card:has(.purchase-wizard) > .modal-body {
          min-width: 0;
          min-height: 0;
          max-height: none;
          flex: 1 1 auto;
          overflow-x: hidden;
          overflow-y: auto;
          overscroll-behavior: contain;
          scrollbar-gutter: stable;
        }

        .purchase-wizard,
        .purchase-wizard .wizard-panel {
          min-width: 0;
          min-height: 0;
        }

        .purchase-wizard .wizard-actions {
          position: sticky;
          bottom: 0;
          z-index: 3;
          margin: 0 -2px -2px;
          padding: 12px 2px 2px;
          background: var(--card);
          border-top: 1px solid var(--border);
        }

        @media (max-height: 760px) {
          .modal-layer:has(.purchase-wizard) {
            padding-block: 8px;
          }

          .modal-card:has(.purchase-wizard) {
            max-height: calc(100dvh - 16px);
          }

          .purchase-wizard .wizard-panel {
            min-height: 0;
          }
        }

        @media (max-width: 1279px) {
          .supplier-purchases-page .module-metrics-grid {
            grid-template-columns: repeat(2, minmax(0, 1fr));
          }
        }

        @media (max-width: 767px) {
          .supplier-purchases-page .module-metrics-grid {
            grid-template-columns: 1fr;
          }

          .purchase-wizard .wizard-steps {
            grid-template-columns: repeat(3, minmax(0, 1fr));
            row-gap: 12px;
          }

          .purchase-wizard .wizard-steps li:not(:last-child)::after {
            display: none;
          }
        }
      `}</style>
      <PageHeader
        icon={Truck}
        title="ØªØ£Ù…ÛŒÙ†â€ŒÚ©Ù†Ù†Ø¯Ú¯Ø§Ù† Ùˆ Ø®Ø±ÛŒØ¯"
        description="Ø®Ø±ÛŒØ¯ Ø±Ø§ Ø§Ø² ØªØ£Ù…ÛŒÙ†â€ŒÚ©Ù†Ù†Ø¯Ù‡ Ø´Ø±ÙˆØ¹ Ú©Ù†Ø› Ø§Ù†Ø¨Ø§Ø±ØŒ Ù…Ø­ØµÙˆÙ„ØŒ Ù‚ÛŒÙ…Øª Ùˆ ØªØµÙˆÛŒØ± Ø¯Ø± ÛŒÚ© Ù…Ø³ÛŒØ± Ú©ÙˆØªØ§Ù‡ Ø«Ø¨Øª Ù…ÛŒâ€ŒØ´ÙˆÙ†Ø¯."
        actionLabel="Ø®Ø±ÛŒØ¯ Ø¬Ø¯ÛŒØ¯"
        onAction={() => openWizard()}
        secondary={
          <>
            <button className="secondary-button" type="button" onClick={() => setSupplierModalOpen(true)}>
              <PackagePlus size={16} /> ØªØ£Ù…ÛŒÙ†â€ŒÚ©Ù†Ù†Ø¯Ù‡ Ø¬Ø¯ÛŒØ¯
            </button>
            <RefreshButton refreshing={refreshing} onClick={() => void refresh()} />
          </>
        }
      />

      <div className="module-metrics-grid">
        <MetricTile label="Ø®Ø±ÛŒØ¯Ù‡Ø§ÛŒ Ø«Ø¨Øªâ€ŒØ´Ø¯Ù‡" value={String(data.supplierPurchases.length)} hint="Ø³Ù†Ø¯ Ø®Ø±ÛŒØ¯" />
        <MetricTile label="Ø¬Ù…Ø¹ Ø¨Ù‡Ø§ÛŒ Ø®Ø±ÛŒØ¯" value={formatMoney(totalPurchaseCost)} hint="Ø¨Ù‡Ø§ÛŒ ØªÙ…Ø§Ù…â€ŒØ´Ø¯Ù‡" />
        <MetricTile label="Ø³ÙˆØ¯ Ù…ÙˆØ±Ø¯ Ø§Ù†ØªØ¸Ø§Ø±" value={formatMoney(totalExpectedProfit)} hint="Ù¾ÛŒØ´ Ø§Ø² ÙØ±ÙˆØ´ Ùˆ ØªØ®ÙÛŒÙ" />
      </div>

      {data.suppliers.length ? (
        <TableCard>
          <div className="table-scroll">
            <table className="data-table">
              <thead><tr><th>Ú©Ø¯</th><th>Ù†Ø§Ù…</th><th>Ù…Ø³Ø¦ÙˆÙ„ ØªÙ…Ø§Ø³</th><th>ØªÙ„ÙÙ†</th><th>ÙˆØ¶Ø¹ÛŒØª</th><th /></tr></thead>
              <tbody>
                {data.suppliers.map((supplier) => (
                  <tr key={supplier.id}>
                    <td>{supplier.code}</td><td><strong>{supplier.name}</strong></td>
                    <td>{supplier.contactName || "â€”"}</td><td>{supplier.phoneNumber || "â€”"}</td>
                    <td><StatusBadge status={supplier.isActive ? "Active" : "Inactive"} /></td>
                    <td>{supplier.isActive && <button className="row-action" type="button" onClick={() => openWizard(supplier.id)}>Ø´Ø±ÙˆØ¹ Ø®Ø±ÛŒØ¯</button>}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </TableCard>
      ) : (
        <EmptyState title="ØªØ£Ù…ÛŒÙ†â€ŒÚ©Ù†Ù†Ø¯Ù‡â€ŒØ§ÛŒ Ø«Ø¨Øª Ù†Ø´Ø¯Ù‡" description="Ø§Ø¨ØªØ¯Ø§ ØªØ£Ù…ÛŒÙ†â€ŒÚ©Ù†Ù†Ø¯Ù‡ Ø±Ø§ Ø«Ø¨Øª Ú©Ù†Ø› Ø³Ù¾Ø³ ÙˆÛŒØ²Ø§Ø±Ø¯ Ø®Ø±ÛŒØ¯ Ø®ÙˆØ¯Ú©Ø§Ø± Ø¨Ø§Ø² Ù…ÛŒâ€ŒØ´ÙˆØ¯." />
      )}

      {data.supplierPurchases.length > 0 && (
        <section className="module-subsection">
          <h2 className="section-title">Ø¢Ø®Ø±ÛŒÙ† Ø®Ø±ÛŒØ¯Ù‡Ø§</h2>
          <TableCard><div className="table-scroll"><table className="data-table">
            <thead><tr><th>Ø³Ù†Ø¯</th><th>ØªØ£Ù…ÛŒÙ†â€ŒÚ©Ù†Ù†Ø¯Ù‡</th><th>Ù…Ø­ØµÙˆÙ„</th><th>ØªØ¹Ø¯Ø§Ø¯</th><th>Ø®Ø±ÛŒØ¯ ÙˆØ§Ø­Ø¯</th><th>ÙØ±ÙˆØ´ ÙˆØ§Ø­Ø¯</th><th>Ø³ÙˆØ¯ Ù…ÙˆØ±Ø¯ Ø§Ù†ØªØ¸Ø§Ø±</th><th>ØªØ§Ø±ÛŒØ®</th></tr></thead>
            <tbody>{data.supplierPurchases.map((purchase) => <tr key={purchase.id}>
              <td>{purchase.purchaseNumber}</td><td>{purchase.supplierName}</td>
              <td><strong>{purchase.productName} Â· {purchase.variantName}</strong><small>{purchase.sku}</small></td>
              <td>{purchase.quantity}</td><td>{formatMoney(purchase.unitCostRials)}</td>
              <td>{formatMoney(purchase.sellingUnitPriceRials)}</td>
              <td><strong className={purchase.expectedTotalProfitRials < 0 ? "negative-text" : "positive-text"}>{formatMoney(purchase.expectedTotalProfitRials)}</strong></td>
              <td>{formatDate(purchase.purchasedAt)}</td>
            </tr>)}</tbody>
          </table></div></TableCard>
        </section>
      )}

      <Modal open={supplierModalOpen} title="Ø«Ø¨Øª ØªØ£Ù…ÛŒÙ†â€ŒÚ©Ù†Ù†Ø¯Ù‡" onClose={() => !supplierSaving && setSupplierModalOpen(false)}>
        <form className="entity-form" onSubmit={submitSupplier}>
          <FormField label="Ú©Ø¯"><input name="code" dir="ltr" maxLength={50} required /></FormField>
          <FormField label="Ù†Ø§Ù… Ù…Ø¬Ù…ÙˆØ¹Ù‡"><input name="name" maxLength={200} required /></FormField>
          <FormField label="Ù…Ø³Ø¦ÙˆÙ„ ØªÙ…Ø§Ø³"><input name="contactName" /></FormField>
          <FormField label="ØªÙ„ÙÙ†"><input name="phoneNumber" /></FormField>
          <FormField label="Ø§ÛŒÙ…ÛŒÙ„"><input name="email" type="email" dir="ltr" /></FormField>
          <FormField label="Ø´Ù†Ø§Ø³Ù‡ Ù…Ù„ÛŒ"><input name="nationalId" /></FormField>
          <FormField label="Ù†Ø´Ø§Ù†ÛŒ" wide><textarea name="addressLine" /></FormField>
          <FormField label="ÛŒØ§Ø¯Ø¯Ø§Ø´Øª" wide><textarea name="notes" /></FormField>
          <InlineError message={supplierError} />
          <FormActions saving={supplierSaving} submitLabel="Ø«Ø¨Øª Ùˆ Ø´Ø±ÙˆØ¹ Ø®Ø±ÛŒØ¯" onCancel={() => setSupplierModalOpen(false)} />
        </form>
      </Modal>

      <Modal
        open={wizardOpen}
        title="Ø«Ø¨Øª Ø®Ø±ÛŒØ¯ ØªØ£Ù…ÛŒÙ†â€ŒÚ©Ù†Ù†Ø¯Ù‡"
        description="Ù‡Ù…Ù‡ Ù…Ø±Ø§Ø­Ù„ Ø¯Ø± Ù‡Ù…ÛŒÙ† Ù¾Ù†Ø¬Ø±Ù‡ Ø§Ù†Ø¬Ø§Ù… Ù…ÛŒâ€ŒØ´ÙˆØ¯Ø› Ø«Ø¨Øª Ù†Ù‡Ø§ÛŒÛŒ Ø¯Ø± Ù…Ø±Ø­Ù„Ù‡ Ø¢Ø®Ø± Ø§Ø³Øª."
        onClose={closeWizard}
      >
        <div className="purchase-wizard">
          <ol className="wizard-steps" aria-label="Ù…Ø±Ø§Ø­Ù„ Ø«Ø¨Øª Ø®Ø±ÛŒØ¯">
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
              <h3>Û±. ØªØ£Ù…ÛŒÙ†â€ŒÚ©Ù†Ù†Ø¯Ù‡ Ø±Ø§ Ø§Ù†ØªØ®Ø§Ø¨ Ú©Ù†</h3>
              <p>Ø´Ø±ÙˆØ¹ Ø¹Ù…Ù„ÛŒØ§Øª Ø§Ø² ØªØ£Ù…ÛŒÙ†â€ŒÚ©Ù†Ù†Ø¯Ù‡ Ø§Ø³ØªØ› ÙÙ‚Ø· ØªØ£Ù…ÛŒÙ†â€ŒÚ©Ù†Ù†Ø¯Ù‡â€ŒÙ‡Ø§ÛŒ ÙØ¹Ø§Ù„ Ù†Ù…Ø§ÛŒØ´ Ø¯Ø§Ø¯Ù‡ Ù…ÛŒâ€ŒØ´ÙˆÙ†Ø¯.</p>
              <FormField label="ØªØ£Ù…ÛŒÙ†â€ŒÚ©Ù†Ù†Ø¯Ù‡" wide><select value={draft.supplierId} onChange={(event) => update("supplierId", event.target.value)} required>
                <option value="">Ø§Ù†ØªØ®Ø§Ø¨ Ú©Ù†</option>{activeSuppliers.map((item) => <option value={item.id} key={item.id}>{item.name} ({item.code})</option>)}
              </select></FormField>
              {!activeSuppliers.length && <EmptyState title="ØªØ£Ù…ÛŒÙ†â€ŒÚ©Ù†Ù†Ø¯Ù‡ ÙØ¹Ø§Ù„ ÙˆØ¬ÙˆØ¯ Ù†Ø¯Ø§Ø±Ø¯" description="Ø§ÛŒÙ† Ù¾Ù†Ø¬Ø±Ù‡ Ø±Ø§ Ø¨Ø¨Ù†Ø¯ Ùˆ ÛŒÚ© ØªØ£Ù…ÛŒÙ†â€ŒÚ©Ù†Ù†Ø¯Ù‡ Ø¬Ø¯ÛŒØ¯ Ø«Ø¨Øª Ú©Ù†." />}
            </section>}

            {step === 1 && <section>
              <h3>Û². Ø§Ù†Ø¨Ø§Ø± Ù…Ù‚ØµØ¯</h3><p>ÛŒÚ© Ø§Ù†Ø¨Ø§Ø± Ù…ÙˆØ¬ÙˆØ¯ Ø±Ø§ Ø§Ù†ØªØ®Ø§Ø¨ Ú©Ù† ÛŒØ§ Ù‡Ù…ÛŒÙ†â€ŒØ¬Ø§ Ø§Ù†Ø¨Ø§Ø± Ø¬Ø¯ÛŒØ¯ Ø¨Ø³Ø§Ø².</p>
              <div className="wizard-mode-switch">
                <button type="button" className={draft.warehouseMode === "existing" ? "is-active" : ""} onClick={() => update("warehouseMode", "existing")}><WarehouseIcon size={17} /> Ø§Ù†Ø¨Ø§Ø± Ù…ÙˆØ¬ÙˆØ¯</button>
                <button type="button" className={draft.warehouseMode === "new" ? "is-active" : ""} onClick={() => update("warehouseMode", "new")}><PackagePlus size={17} /> Ø§Ù†Ø¨Ø§Ø± Ø¬Ø¯ÛŒØ¯</button>
              </div>
              {draft.warehouseMode === "existing" ? <div className="entity-form">
                <FormField label="Ø§Ù†Ø¨Ø§Ø±" wide><select value={draft.warehouseId} onChange={(event) => update("warehouseId", event.target.value)}><option value="">Ø§Ù†ØªØ®Ø§Ø¨ Ú©Ù†</option>{activeWarehouses.map((item) => <option value={item.id} key={item.id}>{item.name} ({item.code})</option>)}</select></FormField>
              </div> : <div className="entity-form">
                <FormField label="Ú©Ø¯ Ø§Ù†Ø¨Ø§Ø±"><input value={draft.warehouseCode} maxLength={50} dir="ltr" onChange={(event) => update("warehouseCode", event.target.value)} /></FormField>
                <FormField label="Ù†Ø§Ù… Ø§Ù†Ø¨Ø§Ø±"><input value={draft.warehouseName} maxLength={200} onChange={(event) => update("warehouseName", event.target.value)} /></FormField>
              </div>}
            </section>}

            {step === 2 && <section>
              <h3>Û³. Ù…Ø­ØµÙˆÙ„ Ùˆ Ù…Ø¯Ù„</h3><p>Ù…ÛŒâ€ŒØªÙˆØ§Ù†ÛŒ Ù…Ø­ØµÙˆÙ„ Ù…ÙˆØ¬ÙˆØ¯ Ø±Ø§ Ø§Ù†ØªØ®Ø§Ø¨ Ú©Ù†ÛŒ ÛŒØ§ Ù…Ø­ØµÙˆÙ„ Ø¬Ø¯ÛŒØ¯ Ø±Ø§ Ø¯Ø± Ù‡Ù…ÛŒÙ† Ø®Ø±ÛŒØ¯ Ø¨Ø³Ø§Ø²ÛŒ.</p>
              <div className="wizard-mode-switch">
                <button type="button" className={draft.productMode === "new" ? "is-active" : ""} onClick={() => update("productMode", "new")}><PackagePlus size={17} /> Ù…Ø­ØµÙˆÙ„ Ø¬Ø¯ÛŒØ¯</button>
                <button type="button" className={draft.productMode === "existing" ? "is-active" : ""} onClick={() => update("productMode", "existing")}><Check size={17} /> Ù…Ø­ØµÙˆÙ„ Ù…ÙˆØ¬ÙˆØ¯</button>
              </div>
              {draft.productMode === "existing" ? <div className="entity-form">
                <FormField label="Ù…Ø­ØµÙˆÙ„ Ùˆ Ù…Ø¯Ù„" wide><select value={draft.variantId} onChange={(event) => update("variantId", event.target.value)}><option value="">Ø§Ù†ØªØ®Ø§Ø¨ Ú©Ù†</option>{activeVariants.map((item) => <option value={item.id} key={item.id}>{item.productName} Â· {item.name} ({item.sku})</option>)}</select></FormField>
              </div> : <div className="entity-form">
                <FormField label="Ù†Ø§Ù… Ù…Ø­ØµÙˆÙ„"><input value={draft.productName} maxLength={200} onChange={(event) => update("productName", event.target.value)} /></FormField>
                <FormField label="Ø¯Ø³ØªÙ‡â€ŒØ¨Ù†Ø¯ÛŒ"><select value={draft.categoryId} onChange={(event) => update("categoryId", event.target.value)}><option value="">Ø¨Ø¯ÙˆÙ† Ø¯Ø³ØªÙ‡â€ŒØ¨Ù†Ø¯ÛŒ</option>{data.categories.filter((item) => item.isActive).map((item) => <option value={item.id} key={item.id}>{item.name}</option>)}</select></FormField>
                <FormField label="Ù†Ø§Ù… Ù…Ø¯Ù„"><input value={draft.variantName} maxLength={200} onChange={(event) => update("variantName", event.target.value)} /></FormField>
                <FormField label="Ú©Ø¯ SKU"><input value={draft.sku} maxLength={64} dir="ltr" onChange={(event) => update("sku", event.target.value)} /></FormField>
                <FormField label="ØªÙˆØ¶ÛŒØ­Ø§Øª Ù…Ø­ØµÙˆÙ„" wide><textarea value={draft.productDescription} maxLength={4000} onChange={(event) => update("productDescription", event.target.value)} /></FormField>
              </div>}
            </section>}

            {step === 3 && <section>
              <h3>Û´. ØªØ¹Ø¯Ø§Ø¯ Ùˆ Ù‚ÛŒÙ…Øªâ€ŒÙ‡Ø§</h3><p>Ù‡Ù…Ù‡ Ù…Ø¨Ø§Ù„Øº Ø§ÛŒÙ† ÙØ±Ù… Ùˆ Ø¨Ø®Ø´â€ŒÙ‡Ø§ÛŒ Ù‚Ø§Ø¨Ù„ Ù…Ø´Ø§Ù‡Ø¯Ù‡ Ø³Ø§Ù…Ø§Ù†Ù‡ Ø¨Ù‡ ØªÙˆÙ…Ø§Ù† Ù‡Ø³ØªÙ†Ø¯.</p>
              <div className="entity-form">
                <FormField label="ØªØ¹Ø¯Ø§Ø¯ Ø®Ø±ÛŒØ¯"><input type="number" min="1" step="1" value={draft.quantity} onChange={(event) => update("quantity", Number(event.target.value))} /></FormField>
                <FormField label="Ù‚ÛŒÙ…Øª Ø®Ø±ÛŒØ¯ Ù‡Ø± ÙˆØ§Ø­Ø¯ (ØªÙˆÙ…Ø§Ù†)"><input type="number" min="0" step="1" value={draft.unitCostTomans} onChange={(event) => update("unitCostTomans", Number(event.target.value))} /></FormField>
                <FormField label="Ù‚ÛŒÙ…Øª ÙØ±ÙˆØ´ Ù‡Ø± ÙˆØ§Ø­Ø¯ (ØªÙˆÙ…Ø§Ù†)"><input type="number" min="1" step="1" value={draft.sellingPriceTomans} onChange={(event) => update("sellingPriceTomans", Number(event.target.value))} /></FormField>
                <FormField label="Ø²Ù…Ø§Ù† Ø®Ø±ÛŒØ¯"><input type="datetime-local" value={draft.purchasedAt} onChange={(event) => update("purchasedAt", event.target.value)} /></FormField>
                <FormField label="Ø´Ù…Ø§Ø±Ù‡ ÙØ§Ú©ØªÙˆØ± ØªØ£Ù…ÛŒÙ†â€ŒÚ©Ù†Ù†Ø¯Ù‡"><input maxLength={100} value={draft.supplierReference} onChange={(event) => update("supplierReference", event.target.value)} /></FormField>
                <FormField label="ÛŒØ§Ø¯Ø¯Ø§Ø´Øª" wide><textarea maxLength={1000} value={draft.notes} onChange={(event) => update("notes", event.target.value)} /></FormField>
              </div>
              <div className={`purchase-profit-preview ${expectedProfitRials < 0 ? "is-negative" : ""}`}><span>Ø³ÙˆØ¯ Ù…ÙˆØ±Ø¯ Ø§Ù†ØªØ¸Ø§Ø± Ø§ÛŒÙ† Ø®Ø±ÛŒØ¯</span><strong>{formatMoney(expectedProfitRials)}</strong><small>Ø§Ø®ØªÙ„Ø§Ù Ù‚ÛŒÙ…Øª ÙØ±ÙˆØ´ Ùˆ Ø®Ø±ÛŒØ¯ Ã— ØªØ¹Ø¯Ø§Ø¯</small></div>
            </section>}

            {step === 4 && <section>
              <h3>Ûµ. ØªØµÙˆÛŒØ± Ùˆ Ù…Ø´Ø®ØµØ§Øª ØªÚ©Ù…ÛŒÙ„ÛŒ</h3>
              {draft.productMode === "existing" ? <div className="wizard-existing-summary"><Check size={24} /><div><strong>Ù…Ø´Ø®ØµØ§Øª Ù…Ø­ØµÙˆÙ„ Ù…ÙˆØ¬ÙˆØ¯ Ø­ÙØ¸ Ù…ÛŒâ€ŒØ´ÙˆØ¯</strong><p>ØªØµÙˆÛŒØ± Ùˆ Ù…Ø´Ø®ØµØ§Øª Ù‚Ø¨Ù„ÛŒ ØªØºÛŒÛŒØ± Ù†Ù…ÛŒâ€ŒÚ©Ù†Ù†Ø¯Ø› ÙÙ‚Ø· Ø®Ø±ÛŒØ¯ØŒ Ù…ÙˆØ¬ÙˆØ¯ÛŒ Ùˆ Ù‚ÛŒÙ…Øª ÙØ±ÙˆØ´ Ø«Ø¨Øª Ù…ÛŒâ€ŒØ´ÙˆØ¯.</p></div></div> : <>
                <p>ØªØµÙˆÛŒØ± Ø§ØµÙ„ÛŒ Ùˆ Ù…Ø´Ø®ØµØ§Øª Ø·Ù„Ø§ Ø±Ø§ Ù¾ÛŒØ´ Ø§Ø² ØªØ£ÛŒÛŒØ¯ Ù†Ù‡Ø§ÛŒÛŒ Ø§Ø¶Ø§ÙÙ‡ Ú©Ù†.</p>
                <div className="entity-form">
                  <FormField label="ØªØµÙˆÛŒØ± Ù…Ø­ØµÙˆÙ„" hint="JPGØŒ PNG ÛŒØ§ WebPØ› Ø­Ø¯Ø§Ú©Ø«Ø± Ûµ Ù…Ú¯Ø§Ø¨Ø§ÛŒØª" wide><input type="file" accept="image/jpeg,image/png,image/webp" onChange={handleImage} /></FormField>
                  <FormField label="Ø¹ÛŒØ§Ø±"><input type="number" min="1" max="24" step="1" value={draft.karat} onChange={(event) => update("karat", Number(event.target.value))} /></FormField>
                  <FormField label="ÙˆØ²Ù† Ù†Ø§Ø®Ø§Ù„Øµ (Ú¯Ø±Ù…)"><input type="number" min="0.001" step="0.001" value={draft.grossWeight} onChange={(event) => update("grossWeight", Number(event.target.value))} /></FormField>
                  <FormField label="ÙˆØ²Ù† Ø®Ø§Ù„Øµ Ø·Ù„Ø§ (Ú¯Ø±Ù…)"><input type="number" min="0.001" step="0.001" value={draft.netGoldWeight} onChange={(event) => update("netGoldWeight", Number(event.target.value))} /></FormField>
                  <FormField label="ÙˆØ²Ù† Ø³Ù†Ú¯ (Ú¯Ø±Ù…)"><input type="number" min="0" step="0.001" value={draft.stoneWeight} onChange={(event) => update("stoneWeight", Number(event.target.value))} /></FormField>
                  <FormField label="ÙˆØ²Ù† Ø³Ø§ÛŒØ± Ù…ÙˆØ§Ø¯ (Ú¯Ø±Ù…)"><input type="number" min="0" step="0.001" value={draft.otherMaterialWeight} onChange={(event) => update("otherMaterialWeight", Number(event.target.value))} /></FormField>
                  <label className="checkbox-field"><input type="checkbox" checked={draft.hasStone} onChange={(event) => update("hasStone", event.target.checked)} /><span>Ø¯Ø§Ø±Ø§ÛŒ Ø³Ù†Ú¯</span></label>
                  <label className="checkbox-field"><input type="checkbox" checked={draft.isWeightVariable} onChange={(event) => update("isWeightVariable", event.target.checked)} /><span>ÙˆØ²Ù† Ù…ØªØºÛŒØ±</span></label>
                </div>
              </>}
            </section>}

            {step === 5 && <section>
              <h3>Û¶. Ø¨Ø±Ø±Ø³ÛŒ Ùˆ ØªØ£ÛŒÛŒØ¯</h3><p>Ø¨Ø¹Ø¯ Ø§Ø² ØªØ£ÛŒÛŒØ¯ØŒ Ø®Ø±ÛŒØ¯ Ùˆ Ù‡Ù…Ù‡ Ù…ÙˆØ§Ø±Ø¯ Ø¬Ø¯ÛŒØ¯ Ø¨Ù‡â€ŒØªØ±ØªÛŒØ¨ Ø«Ø¨Øª Ù…ÛŒâ€ŒØ´ÙˆÙ†Ø¯.</p>
              <dl className="wizard-review">
                <div><dt>ØªØ£Ù…ÛŒÙ†â€ŒÚ©Ù†Ù†Ø¯Ù‡</dt><dd>{selectedSupplier?.name || "â€”"}</dd></div>
                <div><dt>Ø§Ù†Ø¨Ø§Ø±</dt><dd>{draft.warehouseMode === "new" ? `${draft.warehouseName} (Ø¬Ø¯ÛŒØ¯)` : selectedWarehouse?.name || "â€”"}</dd></div>
                <div><dt>Ù…Ø­ØµÙˆÙ„</dt><dd>{draft.productMode === "new" ? `${draft.productName} Â· ${draft.variantName}` : selectedVariant ? `${selectedVariant.productName} Â· ${selectedVariant.name}` : "â€”"}</dd></div>
                <div><dt>ØªØ¹Ø¯Ø§Ø¯</dt><dd>{draft.quantity}</dd></div>
                <div><dt>Ù‚ÛŒÙ…Øª Ø®Ø±ÛŒØ¯ ÙˆØ§Ø­Ø¯</dt><dd>{formatMoney(tomansToRials(draft.unitCostTomans))}</dd></div>
                <div><dt>Ù‚ÛŒÙ…Øª ÙØ±ÙˆØ´ ÙˆØ§Ø­Ø¯</dt><dd>{formatMoney(tomansToRials(draft.sellingPriceTomans))}</dd></div>
                <div><dt>Ø¨Ù‡Ø§ÛŒ Ú©Ù„ Ø®Ø±ÛŒØ¯</dt><dd>{formatMoney(tomansToRials(draft.unitCostTomans * draft.quantity))}</dd></div>
                <div><dt>Ø³ÙˆØ¯ Ù…ÙˆØ±Ø¯ Ø§Ù†ØªØ¸Ø§Ø±</dt><dd className={expectedProfitRials < 0 ? "negative-text" : "positive-text"}>{formatMoney(expectedProfitRials)}</dd></div>
              </dl>
            </section>}
          </div>

          <InlineError message={wizardError} />
          <div className="wizard-actions">
            <button className="secondary-button" type="button" onClick={step === 0 ? closeWizard : () => setStep((current) => current - 1)} disabled={saving}>{step === 0 ? "Ø§Ù†ØµØ±Ø§Ù" : "Ù…Ø±Ø­Ù„Ù‡ Ù‚Ø¨Ù„"}</button>
            {step < steps.length - 1 ? <button className="primary-button" type="button" onClick={nextStep} disabled={saving}>Ù…Ø±Ø­Ù„Ù‡ Ø¨Ø¹Ø¯</button> : <button className="primary-button" type="button" onClick={() => void submitPurchase()} disabled={saving}>{saving ? <><RefreshCw className="spin" size={16} /> Ø¯Ø± Ø­Ø§Ù„ Ø«Ø¨Øªâ€¦</> : <><Check size={17} /> ØªØ£ÛŒÛŒØ¯ Ùˆ Ø«Ø¨Øª Ø®Ø±ÛŒØ¯</>}</button>}
          </div>
        </div>
      </Modal>
    </main>
  );
}