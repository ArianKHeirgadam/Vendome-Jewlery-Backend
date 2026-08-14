import { useCallback, useRef } from "react";
import { useHidScanner } from "../../platform/useHidScanner";
import { useOperations } from "./OperationsContext";
import { OperationalApiError } from "./operationsApi";

interface InventoryUnitLookup {
  id: string;
  productId: string;
  productVariantId: string;
  warehouseId: string;
  inventoryItemId: string;
  serialNumber?: string | null;
  barcode?: string | null;
  actualGrossWeight?: number | null;
  actualNetGoldWeight?: number | null;
  karat?: number | null;
  status: string;
  receivedAt: string;
  soldAt?: string | null;
  rowVersion: string;
}

interface InventoryScannerOptions {
  setSearchTerm(value: string): void;
  onNotice(message: string): void;
}

function scannerErrorMessage(error: unknown): string {
  if (error instanceof OperationalApiError) {
    if (error.status === 404) return "قطعه‌ای با این بارکد، سریال یا شناسه پیدا نشد.";
    if (error.status === 403) return "حساب فعلی مجوز مشاهده موجودی را ندارد.";
  }

  return error instanceof Error ? error.message : "جست‌وجوی قطعه اسکن‌شده کامل نشد.";
}

/**
 * Connects a HID scanner to the existing secured inventory-unit lookup API.
 * The QR/barcode is treated only as an opaque identifier; authoritative
 * product, weight and status data always come from the backend/database.
 */
export function useInventoryScanner({
  setSearchTerm,
  onNotice,
}: InventoryScannerOptions): void {
  const { data, request } = useOperations();
  const busyRef = useRef(false);
  const lastScanRef = useRef<{ code: string; at: number } | null>(null);

  const lookup = useCallback(async (rawCode: string) => {
    const code = rawCode.trim();
    if (code.length < 3 || code.length > 128 || busyRef.current) return;

    const now = Date.now();
    const previous = lastScanRef.current;
    if (previous && previous.code === code && now - previous.at < 800) return;
    lastScanRef.current = { code, at: now };

    busyRef.current = true;
    try {
      const unit = await request<InventoryUnitLookup>(
        `/api/v1/inventory/units/lookup?identifier=${encodeURIComponent(code)}`,
      );

      const product = data.products.find((item) => item.id === unit.productId);
      const variant = product?.variants.find((item) => item.id === unit.productVariantId);
      const warehouse = data.warehouses.find((item) => item.id === unit.warehouseId);

      setSearchTerm(variant?.sku || variant?.name || product?.name || code);

      const label = product && variant
        ? `${product.name} · ${variant.name} (${variant.sku})`
        : product?.name || variant?.name || code;
      const location = warehouse?.name ? ` · ${warehouse.name}` : "";
      const state = unit.status ? ` · ${unit.status}` : "";
      onNotice(`اسکن موفق: ${label}${location}${state}`);
    } catch (error) {
      onNotice(scannerErrorMessage(error));
    } finally {
      busyRef.current = false;
    }
  }, [data.products, data.warehouses, onNotice, request, setSearchTerm]);

  useHidScanner({
    onScan: (code) => {
      void lookup(code);
    },
  });
}
