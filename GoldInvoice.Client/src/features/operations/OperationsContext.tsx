import {
  createContext,
  type PropsWithChildren,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
} from "react";
import { useAuthentication } from "../auth/AuthContext";
import { apiRequest, optionalApiRequest } from "./operationsApi";
import type {
  CustomerInteraction,
  Invoice,
  InventoryItem,
  MarketPrice,
  OperationalSnapshot,
  Order,
  PagedResponse,
  Payment,
  Person,
  Product,
  ProductCategory,
  StoreProfile,
  Supplier,
  SupplierPurchase,
  UserSession,
  Warehouse,
} from "./operations.types";

const emptySnapshot: OperationalSnapshot = {
  products: [],
  categories: [],
  warehouses: [],
  inventoryItems: [],
  orders: [],
  invoices: [],
  payments: [],
  customers: [],
  employees: [],
  suppliers: [],
  supplierPurchases: [],
  interactions: [],
  marketPrices: [],
  storeProfile: null,
  sessions: [],
};

interface OperationsContextValue {
  data: OperationalSnapshot;
  loading: boolean;
  refreshing: boolean;
  error: string | null;
  request: ReturnType<typeof useOperationsRequest>;
  refresh(): Promise<void>;
}

const OperationsContext = createContext<OperationsContextValue | null>(null);

function useOperationsRequest() {
  const { authorizedFetch } = useAuthentication();
  return useCallback(
    <T,>(path: string, init?: RequestInit) =>
      apiRequest<T>(authorizedFetch, path, init),
    [authorizedFetch],
  );
}

function successful<T>(
  result: PromiseSettledResult<T>,
  fallback: T,
): T {
  return result.status === "fulfilled" ? result.value : fallback;
}

function failureMessages(results: PromiseSettledResult<unknown>[]): string[] {
  return results
    .filter((result): result is PromiseRejectedResult => result.status === "rejected")
    .map((result) =>
      result.reason instanceof Error ? result.reason.message : "خطای نامشخص سرور",
    )
    .filter((message, index, all) => all.indexOf(message) === index);
}

export function OperationsProvider({ children }: PropsWithChildren) {
  const auth = useAuthentication();
  const request = useOperationsRequest();
  const [data, setData] = useState<OperationalSnapshot>(emptySnapshot);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    setRefreshing(true);
    const fetcher = auth.authorizedFetch;
    const results = await Promise.allSettled([
      apiRequest<PagedResponse<Product>>(fetcher, "/api/v1/catalog/products?page=1&pageSize=100"),
      apiRequest<ProductCategory[]>(fetcher, "/api/v1/catalog/categories?includeInactive=true"),
      apiRequest<Warehouse[]>(fetcher, "/api/v1/inventory/warehouses?includeInactive=true"),
      apiRequest<PagedResponse<InventoryItem>>(fetcher, "/api/v1/inventory/items?page=1&pageSize=100"),
      apiRequest<PagedResponse<Order>>(fetcher, "/api/v1/orders?page=1&pageSize=100"),
      apiRequest<PagedResponse<Invoice>>(fetcher, "/api/v1/invoices?page=1&pageSize=100"),
      apiRequest<PagedResponse<Payment>>(fetcher, "/api/v1/payments?page=1&pageSize=100"),
      apiRequest<Person[]>(fetcher, "/api/v1/people/customers"),
      apiRequest<Person[]>(fetcher, "/api/v1/people/employees"),
      apiRequest<PagedResponse<Supplier>>(fetcher, "/api/v1/suppliers?page=1&pageSize=100&includeInactive=true"),
      apiRequest<PagedResponse<SupplierPurchase>>(fetcher, "/api/v1/inventory/supplier-purchases?page=1&pageSize=100"),
      apiRequest<PagedResponse<CustomerInteraction>>(fetcher, "/api/v1/crm/interactions?page=1&pageSize=100"),
      optionalApiRequest<StoreProfile>(fetcher, "/api/v1/settings/store-profile"),
      apiRequest<UserSession[]>(fetcher, "/api/v1/auth/sessions"),
      optionalApiRequest<MarketPrice>(fetcher, "/api/v1/pricing/market/latest/Gold18K"),
      optionalApiRequest<MarketPrice>(fetcher, "/api/v1/pricing/market/latest/Gold24K"),
      optionalApiRequest<MarketPrice>(fetcher, "/api/v1/pricing/market/latest/Silver"),
      optionalApiRequest<MarketPrice>(fetcher, "/api/v1/pricing/market/latest/Coin"),
      optionalApiRequest<MarketPrice>(fetcher, "/api/v1/pricing/market/latest/Currency"),
    ] as const);

    setData((current) => {
      const marketPrices = results
        .slice(14)
        .flatMap((result) => {
          if (result.status !== "fulfilled" || result.value === null) return [];
          return [result.value as MarketPrice];
        });
      return {
        products: successful(results[0], { items: current.products } as PagedResponse<Product>).items,
        categories: successful(results[1], current.categories),
        warehouses: successful(results[2], current.warehouses),
        inventoryItems: successful(results[3], { items: current.inventoryItems } as PagedResponse<InventoryItem>).items,
        orders: successful(results[4], { items: current.orders } as PagedResponse<Order>).items,
        invoices: successful(results[5], { items: current.invoices } as PagedResponse<Invoice>).items,
        payments: successful(results[6], { items: current.payments } as PagedResponse<Payment>).items,
        customers: successful(results[7], current.customers),
        employees: successful(results[8], current.employees),
        suppliers: successful(results[9], { items: current.suppliers } as PagedResponse<Supplier>).items,
        supplierPurchases: successful(results[10], { items: current.supplierPurchases } as PagedResponse<SupplierPurchase>).items,
        interactions: successful(results[11], { items: current.interactions } as PagedResponse<CustomerInteraction>).items,
        storeProfile: successful(results[12], current.storeProfile),
        sessions: successful(results[13], current.sessions),
        marketPrices,
      };
    });

    const failures = failureMessages(results.slice(0, 14));
    setError(failures.length ? failures[0] : null);
    setLoading(false);
    setRefreshing(false);
  }, [auth.authorizedFetch]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const value = useMemo<OperationsContextValue>(
    () => ({ data, loading, refreshing, error, request, refresh }),
    [data, error, loading, refreshing, request, refresh],
  );

  return (
    <OperationsContext.Provider value={value}>
      {children}
    </OperationsContext.Provider>
  );
}

export function useOperations(): OperationsContextValue {
  const context = useContext(OperationsContext);
  if (!context) {
    throw new Error("useOperations must be used inside OperationsProvider.");
  }
  return context;
}
