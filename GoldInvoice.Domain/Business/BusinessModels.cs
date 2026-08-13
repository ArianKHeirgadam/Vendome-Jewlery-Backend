using GoldInvoice.Domain.Common;

namespace GoldInvoice.Domain.Business;

public enum CustomerInteractionType
{
    Call,
    Message,
    Meeting,
    FollowUp,
    Note
}

public enum CustomerInteractionStatus
{
    Open,
    Completed,
    Cancelled
}

public sealed class Supplier : SoftDeletableEntity
{
    private Supplier()
    {
    }

    public Supplier(
        string code,
        string name,
        string? contactName,
        string? phoneNumber,
        string? email,
        string? nationalId,
        string? addressLine,
        string? notes)
    {
        SetValues(code, name, contactName, phoneNumber, email, nationalId, addressLine, notes);
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? ContactName { get; private set; }

    public string? PhoneNumber { get; private set; }

    public string? Email { get; private set; }

    public string? NationalId { get; private set; }

    public string? AddressLine { get; private set; }

    public string? Notes { get; private set; }

    public bool IsActive { get; private set; } = true;

    public void Update(
        string code,
        string name,
        string? contactName,
        string? phoneNumber,
        string? email,
        string? nationalId,
        string? addressLine,
        string? notes,
        bool isActive)
    {
        SetValues(code, name, contactName, phoneNumber, email, nationalId, addressLine, notes);
        IsActive = isActive;
    }

    private void SetValues(
        string code,
        string name,
        string? contactName,
        string? phoneNumber,
        string? email,
        string? nationalId,
        string? addressLine,
        string? notes)
    {
        Code = Guard.Required(code, nameof(code), 64).ToUpperInvariant();
        Name = Guard.Required(name, nameof(name), 200);
        ContactName = Guard.Optional(contactName, nameof(contactName), 200);
        PhoneNumber = Guard.Optional(phoneNumber, nameof(phoneNumber), 32);
        Email = Guard.Optional(email, nameof(email), 256)?.ToLowerInvariant();
        NationalId = Guard.Optional(nationalId, nameof(nationalId), 32);
        AddressLine = Guard.Optional(addressLine, nameof(addressLine), 1000);
        Notes = Guard.Optional(notes, nameof(notes), 2000);
    }
}

public sealed class SupplierPurchase : AuditableEntity, IAppendOnlyEntity, IProtectedFromHardDelete
{
    private SupplierPurchase()
    {
    }

    public SupplierPurchase(
        string purchaseNumber,
        Guid supplierId,
        Guid warehouseId,
        Guid productVariantId,
        Guid inventoryItemId,
        Guid stockMovementId,
        Guid pricingRuleId,
        int quantity,
        long unitCostRials,
        long sellingUnitPriceRials,
        DateTimeOffset purchasedAt,
        string? supplierReference,
        string? notes)
    {
        Guard.AgainstEmpty(supplierId, nameof(supplierId));
        Guard.AgainstEmpty(warehouseId, nameof(warehouseId));
        Guard.AgainstEmpty(productVariantId, nameof(productVariantId));
        Guard.AgainstEmpty(inventoryItemId, nameof(inventoryItemId));
        Guard.AgainstEmpty(stockMovementId, nameof(stockMovementId));
        Guard.AgainstEmpty(pricingRuleId, nameof(pricingRuleId));
        Guard.AgainstNonPositive(quantity, nameof(quantity));
        Guard.AgainstNegative(unitCostRials, nameof(unitCostRials));
        Guard.AgainstNonPositive(sellingUnitPriceRials, nameof(sellingUnitPriceRials));
        Guard.AgainstDefault(purchasedAt, nameof(purchasedAt));

        PurchaseNumber = Guard.Required(purchaseNumber, nameof(purchaseNumber), 50).ToUpperInvariant();
        SupplierId = supplierId;
        WarehouseId = warehouseId;
        ProductVariantId = productVariantId;
        InventoryItemId = inventoryItemId;
        StockMovementId = stockMovementId;
        PricingRuleId = pricingRuleId;
        Quantity = quantity;
        UnitCostRials = unitCostRials;
        TotalCostRials = checked(unitCostRials * quantity);
        SellingUnitPriceRials = sellingUnitPriceRials;
        PurchasedAt = purchasedAt;
        SupplierReference = Guard.Optional(supplierReference, nameof(supplierReference), 100);
        Notes = Guard.Optional(notes, nameof(notes), 1000);
    }

    public string PurchaseNumber { get; private set; } = string.Empty;
    public Guid SupplierId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid ProductVariantId { get; private set; }
    public Guid InventoryItemId { get; private set; }
    public Guid StockMovementId { get; private set; }
    public Guid PricingRuleId { get; private set; }
    public int Quantity { get; private set; }
    public long UnitCostRials { get; private set; }
    public long TotalCostRials { get; private set; }
    public long SellingUnitPriceRials { get; private set; }
    public long ExpectedUnitProfitRials => SellingUnitPriceRials - UnitCostRials;
    public long ExpectedTotalProfitRials => checked(ExpectedUnitProfitRials * Quantity);
    public DateTimeOffset PurchasedAt { get; private set; }
    public string? SupplierReference { get; private set; }
    public string? Notes { get; private set; }
}

public sealed class CustomerInteraction : AuditableEntity
{
    private CustomerInteraction()
    {
    }

    public CustomerInteraction(
        Guid customerId,
        CustomerInteractionType interactionType,
        string subject,
        string? notes,
        DateTimeOffset occurredAt,
        DateTimeOffset? nextFollowUpAt)
    {
        Guard.AgainstEmpty(customerId, nameof(customerId));
        Guard.AgainstDefault(occurredAt, nameof(occurredAt));
        if (nextFollowUpAt is not null && nextFollowUpAt <= occurredAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nextFollowUpAt),
                "A follow-up must be later than the interaction.");
        }

        CustomerId = customerId;
        InteractionType = interactionType;
        Subject = Guard.Required(subject, nameof(subject), 200);
        Notes = Guard.Optional(notes, nameof(notes), 4000);
        OccurredAt = occurredAt;
        NextFollowUpAt = nextFollowUpAt;
    }

    public Guid CustomerId { get; private set; }

    public CustomerInteractionType InteractionType { get; private set; }

    public string Subject { get; private set; } = string.Empty;

    public string? Notes { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? NextFollowUpAt { get; private set; }

    public CustomerInteractionStatus Status { get; private set; } = CustomerInteractionStatus.Open;

    public DateTimeOffset? CompletedAt { get; private set; }

    public void ChangeStatus(CustomerInteractionStatus status, DateTimeOffset changedAt)
    {
        Guard.AgainstDefault(changedAt, nameof(changedAt));
        Status = status;
        CompletedAt = status == CustomerInteractionStatus.Completed ? changedAt : null;
    }
}
