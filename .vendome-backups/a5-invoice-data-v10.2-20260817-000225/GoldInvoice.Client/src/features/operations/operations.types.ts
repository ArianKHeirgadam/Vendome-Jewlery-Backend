export interface PagedResponse<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface ProductCategory {
  id: string;
  name: string;
  slug: string;
  parentCategoryId?: string | null;
  displayOrder: number;
  isActive: boolean;
  rowVersion: string;
}

export interface GoldProductDetail {
  karat: number;
  grossWeight: number;
  netGoldWeight: number;
  stoneWeight: number;
  otherMaterialWeight: number;
  manufacturingWageType: string;
  manufacturingWageValue: number;
  profitPercentage: number;
  taxPercentage: number;
  hasStone: boolean;
  isWeightVariable: boolean;
  rowVersion: string;
}

export interface ProductVariant {
  id: string;
  productId: string;
  sku: string;
  name: string;
  isActive: boolean;
  goldDetail?: GoldProductDetail | null;
  rowVersion: string;
}

export interface ProductImage {
  id: string;
  productId: string;
  productVariantId?: string | null;
  contentType: string;
  altText?: string | null;
  sortOrder: number;
  isPrimary: boolean;
  rowVersion: string;
}

export interface Product {
  id: string;
  productCategoryId?: string | null;
  name: string;
  slug: string;
  description?: string | null;
  isActive: boolean;
  variants: ProductVariant[];
  images: ProductImage[];
  rowVersion: string;
}

export interface ProductPricingRule {
  id: string;
  productVariantId: string;
  pricingMethod: string;
  goldMarketPriceType?: string | null;
  fixedPriceRials?: number | null;
  fixedGoldPricePerGramRials?: number | null;
  wageType: string;
  wageValue: number;
  profitPercentage: number;
  taxPercentage: number;
  effectiveFrom: string;
  effectiveTo?: string | null;
  isActive: boolean;
  rowVersion: string;
}

export interface Warehouse {
  id: string;
  code: string;
  name: string;
  isActive: boolean;
  rowVersion: string;
}

export interface InventoryItem {
  id: string;
  warehouseId: string;
  productVariantId: string;
  quantityOnHand: number;
  quantityReserved: number;
  quantityAvailable: number;
  averageUnitCostRials: number;
  hasAcquisitionCost: boolean;
  rowVersion: string;
}

export interface OrderAddress {
  id: string;
  customerAddressId?: string | null;
  recipientName: string;
  phoneNumber: string;
  province: string;
  city: string;
  postalCode: string;
  addressLine: string;
}

export interface OrderItem {
  id: string;
  orderItemId?: string | null;
  inventoryItemId?: string | null;
  inventoryUnitId?: string | null;
  productVariantId: string;
  sku: string;
  productName: string;
  variantName: string;
  grossWeightGrams?: number | null;
  netGoldWeightGrams?: number | null;
  karat?: number | null;
  quantity: number;
  unitPriceRials: number;
  lineTotalRials: number;
  profitRials?: number | null;
  taxRials?: number | null;
  wageRials?: number | null;
  acquisitionUnitCostRials?: number | null;
  acquisitionTotalCostRials?: number | null;
  grossProfitRials?: number | null;
}

export interface StoreIdentity {
  id: string;
  tradeName: string;
  legalName: string;
  nationalId?: string | null;
  economicCode?: string | null;
  registrationNumber?: string | null;
  phoneNumber: string;
  postalCode: string;
  addressLine: string;
}

export interface Order {
  id: string;
  customerId: string;
  orderNumber: string;
  status: string;
  itemsSubtotalRials: number;
  discountRials: number;
  shippingRials: number;
  grandTotalRials: number;
  customerNameSnapshot?: string | null;
  customerNationalIdSnapshot?: string | null;
  paidAt?: string | null;
  cancelledAt?: string | null;
  address?: OrderAddress | null;
  store?: StoreIdentity | null;
  items: OrderItem[];
  rowVersion: string;
}

export interface InvoicePrintJob {
  id: string;
  invoiceId: string;
  requestedByUserId: string;
  status: string;
  copies: number;
  isReprint: boolean;
  reprintReason?: string | null;
  printerName?: string | null;
  completedAt?: string | null;
  failureCode?: string | null;
  createdAt: string;
  rowVersion: string;
}

export interface Invoice {
  id: string;
  orderId: string;
  customerId: string;
  paymentId?: string | null;
  invoiceNumber: string;
  status: string;
  issuedAt: string;
  subtotalRials: number;
  discountRials: number;
  shippingRials: number;
  grandTotalRials: number;
  customerNameSnapshot?: string | null;
  customerNationalIdSnapshot?: string | null;
  voidedAt?: string | null;
  voidReason?: string | null;
  address?: OrderAddress | null;
  store?: StoreIdentity | null;
  items: OrderItem[];
  rowVersion: string;
}

export interface Payment {
  id: string;
  orderId: string;
  paymentGatewayId?: string | null;
  provider: string;
  method: string;
  status: string;
  amountRials: number;
  authority?: string | null;
  gatewayPaymentId?: string | null;
  verifiedAt?: string | null;
  failedAt?: string | null;
  cancelledAt?: string | null;
  failureCode?: string | null;
  invoiceId?: string | null;
  rowVersion: string;
}

export interface Person {
  id: string;
  displayName: string;
  email?: string | null;
  phoneNumber?: string | null;
  isActive: boolean;
  mfaEnabled: boolean;
  roles: string[];
  orderCount: number;
  invoiceCount: number;
  addressCount: number;
  createdAt: string;
  lastActivityAt?: string | null;
}

export interface Supplier {
  id: string;
  code: string;
  name: string;
  contactName?: string | null;
  phoneNumber?: string | null;
  email?: string | null;
  nationalId?: string | null;
  addressLine?: string | null;
  notes?: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
  rowVersion: string;
}

export interface SupplierPurchase {
  id: string;
  purchaseNumber: string;
  supplierId: string;
  supplierName: string;
  warehouseId: string;
  warehouseName: string;
  productVariantId: string;
  productName: string;
  variantName: string;
  sku: string;
  inventoryItemId: string;
  quantity: number;
  unitCostRials: number;
  totalCostRials: number;
  sellingUnitPriceRials: number;
  expectedUnitProfitRials: number;
  expectedTotalProfitRials: number;
  purchasedAt: string;
  supplierReference?: string | null;
  notes?: string | null;
}

export interface CustomerInteraction {
  id: string;
  customerId: string;
  customerName: string;
  interactionType: string;
  subject: string;
  notes?: string | null;
  occurredAt: string;
  nextFollowUpAt?: string | null;
  status: string;
  completedAt?: string | null;
  rowVersion: string;
}

export interface CustomerAddress {
  id: string;
  customerId: string;
  title: string;
  recipientName: string;
  phoneNumber: string;
  province: string;
  city: string;
  postalCode: string;
  addressLine: string;
  isDefault: boolean;
  rowVersion: string;
}

export interface StoreProfile {
  tradeName: string;
  legalName: string;
  nationalId?: string | null;
  economicCode?: string | null;
  registrationNumber?: string | null;
  phoneNumber: string;
  postalCode: string;
  addressLine: string;
  rowVersion: string;
}

export interface MarketPrice {
  id: string;
  sourceId: string;
  priceType: string;
  buyPriceRials: number;
  sellPriceRials: number;
  capturedAt: string;
  validationStatus: string;
}

export interface UserSession {
  id: string;
  createdAt: string;
  lastSeenAt: string;
  expiresAt: string;
  revokedAt?: string | null;
  ipAddress?: string | null;
  isCurrent: boolean;
}

export interface OperationalSnapshot {
  products: Product[];
  categories: ProductCategory[];
  warehouses: Warehouse[];
  inventoryItems: InventoryItem[];
  orders: Order[];
  invoices: Invoice[];
  payments: Payment[];
  customers: Person[];
  employees: Person[];
  suppliers: Supplier[];
  supplierPurchases: SupplierPurchase[];
  interactions: CustomerInteraction[];
  marketPrices: MarketPrice[];
  storeProfile: StoreProfile | null;
  sessions: UserSession[];
}
