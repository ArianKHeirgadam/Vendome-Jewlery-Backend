import { Info, Search } from "lucide-react";
import { FormEvent, useEffect, useMemo, useState } from "react";
import { createIdempotencyKey, OperationalApiError } from "./features/operations/operationsApi";
import { useOperations } from "./features/operations/OperationsContext";
import type { CustomerAddress, Order } from "./features/operations/operations.types";
import { tomansToRials, formatMoney } from "./lib/money";

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : "Operation could not be completed.";
}

export function BoltNewInvoicePage({ onNavigate, onNotice }: { onNavigate: (path: string) => void; onNotice: (message: string) => void }) {
  const { data, request, refresh } = useOperations();
  const [customerId, setCustomerId] = useState("");
  const [customerSearch, setCustomerSearch] = useState("");
  const [customerNationalId, setCustomerNationalId] = useState("");
  const [addresses, setAddresses] = useState<CustomerAddress[]>([]);
  const [addressId, setAddressId] = useState("");
  const [inventoryItemId, setInventoryItemId] = useState("");
  const [quantity, setQuantity] = useState(1);
  const [discount, setDiscount] = useState(0);
  const [shipping, setShipping] = useState(0);
    const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [paid, setPaid] = useState(false);

  const variants = useMemo(() => data.products.flatMap(product =>
    product.variants.map(variant => ({ ...variant, productName: product.name }))
  ), [data.products]);

  const customerSuggestions = data.customers.filter(item => item.isActive && (!customerSearch.trim() || `${item.displayName} ${item.phoneNumber || ""}`.toLocaleLowerCase().includes(customerSearch.trim().toLocaleLowerCase()))).slice(0, 5);

  const selectedCustomer = data.customers.find(item => item.id === customerId);

  const selectedInventory = data.inventoryItems.find(item => item.id === inventoryItemId);
  const selectedVariant = selectedInventory
    ? variants.find(variant => variant.id === selectedInventory.productVariantId)
    : undefined;
  const latestPurchase = selectedVariant
    ? [...data.supplierPurchases]
        .filter(purchase => purchase.productVariantId === selectedVariant.id)
        .sort((a, b) => new Date(b.purchasedAt).getTime() - new Date(a.purchasedAt).getTime())[0]
    : undefined;
  const unitPrice = latestPurchase?.sellingUnitPriceRials ?? selectedInventory?.averageUnitCostRials ?? 0;
  const metalSubtotal = unitPrice * quantity;
  const discountRials = tomansToRials(discount);
  const shippingRials = tomansToRials(shipping);
  const grandTotal = Math.max(0, metalSubtotal - discountRials + shippingRials);

  useEffect(() => {
    if (!customerId) {
      setAddresses([]);
      setAddressId("");
      return;
    }
    let active = true;
    void request<CustomerAddress[]>(`/api/v1/customers/${customerId}/addresses`)
      .then(result => {
        if (!active) return;
        setAddresses(result);
        setAddressId(result.find(item => item.isDefault)?.id ?? result[0]?.id ?? "");
      })
      .catch(err => {
        if (!active) return;
        setAddresses([]);
        setAddressId("");
        setError(messageOf(err));
      });
    return () => { active = false; };
  }, [customerId, request]);

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError(null);
    if (!data.storeProfile) {
      setError("Complete store settings before creating an invoice.");
      return;
    }
    if (!customerId || !addressId) {
      setError("Select a customer and one of their saved addresses.");
      return;
    }
    if (!selectedInventory || selectedInventory.quantityAvailable < quantity) {
      setError("Selected inventory is unavailable or does not have enough stock.");
      return;
    }
    setSaving(true);
    try {
      await request<Order>("/api/v1/orders", {
        method: "POST",
        headers: { "Idempotency-Key": createIdempotencyKey("bolt-invoice") },
        body: JSON.stringify({
          customerId,
          customerAddressId: addressId,
          customerNationalId: customerNationalId.trim() || null,
          lines: [{
            inventoryItemId: selectedInventory.id,
            inventoryUnitId: null,
            quantity,
            inventoryRowVersion: selectedInventory.rowVersion,
          }],
          reservationLifetimeMinutes: 15,
          discountRials,
          shippingRials,
        }),
      });
      await refresh();
      onNotice(paid
        ? "Order saved. Continue to payment to issue the official invoice."
        : "Order saved and inventory reserved. Continue to payment.");
      onNavigate("/orders");
    } catch (err) {
      const friendly = err instanceof OperationalApiError && err.status === 404
        ? "Customer, address, or inventory is no longer available."
        : err instanceof OperationalApiError && err.status === 422
          ? "Complete the store profile in Settings before creating an order."
          : messageOf(err);
      setError(friendly);
    } finally {
      setSaving(false);
    }
  };

  return (
    <main className="bolt-new-invoice-page">
      <header className="bolt-new-invoice-header">
        <div>
          <div className="bolt-new-invoice-eyebrow"><span className="bolt-bag-mark">▢</span> Vendome Management</div>
          <h1>Generate Invoice</h1>
          <p>Step-by-step transaction workflow</p>
        </div>
        <span className="bolt-header-action-badge"><b>Action</b> Automatic Rate Lock Active</span>
      </header>

      <div className="bolt-header-divider" />

      {!data.storeProfile && (
        <div className="bolt-warning"><Info size={16}/><span>Complete store settings before creating an invoice.</span><button type="button" onClick={() => onNavigate("/settings")}>Settings</button></div>
      )}

      <form className="bolt-invoice-layout" onSubmit={submit}>
        <div className="bolt-invoice-left">
          <section className="bolt-form-card">
            <div className="bolt-section-heading">
              <h2>1. ASSOCIATE CUSTOMER</h2>
              <span className="bolt-session-badge"><b>Click 1</b> Search Returning Customer</span>
            </div>
            <div className="bolt-customer-entry">
              <Search size={16}/>
              <input
                value={customerSearch}
                onChange={event => {
                  setCustomerSearch(event.target.value);
                  if (customerId) setCustomerId("");
                  setAddressId("");
                }}
                placeholder="Search customer..."
                required={!customerId}
              />
              <button type="button" onClick={() => onNavigate("/customers?new=1")}>+ Add Profile</button>
            </div>
            <div className="bolt-customer-results">
              {customerId ? (
                <>
                  <strong>{selectedCustomer?.displayName}</strong>
                  <span>{selectedCustomer?.phoneNumber || "No phone"}</span>
                  <b>[TAB TO AUTO-FILL]</b>
                  {addresses.length ? (
                    <div className="bolt-address-line">
                      {addresses.map(address => (
                        <button type="button" className={address.id === addressId ? "selected" : ""} key={address.id} onClick={() => setAddressId(address.id)}>
                          {address.title}{address.city ? ` · ${address.city}` : ""}
                        </button>
                      ))}
                    </div>
                  ) : <div>No saved address for this customer.</div>}
                </>
              ) : (
                customerSuggestions.map(item => (
                  <button type="button" className="bolt-customer-suggestion" key={item.id} onClick={() => {
                    setCustomerId(item.id);
                    setCustomerSearch(item.displayName);
                  }}>
                    <strong>{item.displayName}</strong><span>{item.phoneNumber || "No phone"}</span>
                  </button>
                ))
              )}
            </div>
          </section>

          <section className="bolt-form-card bolt-metal-card">
            <h2>2. METAL ITEMS SPECIFICATION</h2>
            <div className="bolt-metal-grid bolt-metal-labels"><span>Metal Category</span><span>Purity</span><span>Weight (g)</span><span>Making ($/g)</span><span>Total</span></div>
            <div className="bolt-metal-grid">
              <select value={inventoryItemId} onChange={event => setInventoryItemId(event.target.value)} required>
                <option value="">Select inventory item</option>
                {data.inventoryItems.filter(item => item.quantityAvailable > 0).map(item => {
                  const variant = variants.find(value => value.id === item.productVariantId);
                  return <option value={item.id} key={item.id}>{variant ? `${variant.productName} · ${variant.name}` : item.id}</option>;
                })}
              </select>
              <span>{selectedVariant?.goldDetail?.karat ? `${selectedVariant.goldDetail.karat}K` : "—"}</span>
              <input type="number" min="1" value={quantity} onChange={event => setQuantity(Math.max(1, Number(event.target.value) || 1))}/>
              <span>{unitPrice ? formatMoney(unitPrice) : "—"}</span>
              <strong>{formatMoney(metalSubtotal)}</strong>
            </div>
            <div className="bolt-metal-grid bolt-faded-row"><span>Click to add next item...</span><span>--</span><span>0.00</span><span>0.00</span><span>{formatMoney(0)}</span></div>
          </section>
        </div>

        <section className="bolt-payment-card">
          <h2>3. PAYMENT &amp; FINALIZATION</h2>
          <div className="bolt-payment-lines">
            <span>Metal Subtotal <b>{formatMoney(metalSubtotal)}</b></span>
            <span>Making Charges <b>{formatMoney(0)}</b></span>
            <span>Taxes <b>{formatMoney(0)}</b></span>
          </div>
          <div className="bolt-grand-total"><span>GRAND TOTAL</span><b>{formatMoney(grandTotal)}</b></div>
          <label className="bolt-payment-label">Payment Status Toggle</label>
          <div className="bolt-paid-toggle">
            <button className={!paid ? "selected" : ""} type="button" onClick={() => setPaid(false)}>Unpaid/Credit</button>
            <button className={paid ? "selected" : ""} type="button" onClick={() => setPaid(true)}>Fully Paid</button>
          </div>
          <button className="bolt-save-button" type="submit" disabled={saving}>{saving ? "Saving..." : "▣  One-Tap Print & Save"}</button>
          {error && <div className="bolt-form-error">{error}</div>}
          <span className="bolt-session-badge bolt-submit-badge"><b>Click 3</b> Submit Invoice Instantly</span>
        </section>
      </form>
    </main>
  );
}
