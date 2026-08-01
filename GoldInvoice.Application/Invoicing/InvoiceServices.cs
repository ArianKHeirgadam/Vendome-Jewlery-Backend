using GoldInvoice.Application.Common;
using GoldInvoice.Application.Orders;
using GoldInvoice.Domain.Invoicing;

namespace GoldInvoice.Application.Invoicing;

public sealed record InvoiceItemInfo(
    Guid Id,
    Guid? OrderItemId,
    Guid? PriceCalculationSnapshotId,
    Guid? InventoryUnitId,
    int LineNumber,
    string Sku,
    string ProductName,
    string VariantName,
    decimal GrossWeightGrams,
    decimal? NetGoldWeightGrams,
    int? Karat,
    int Quantity,
    long? MarketUnitPriceRials,
    long? GoldValueRials,
    long? WageRials,
    long? ProfitRials,
    long? TaxRials,
    long UnitPriceRials,
    long LineTotalRials,
    string? RoundingPolicy);

public sealed record InvoiceAddressSnapshotInfo(
    Guid Id,
    Guid OrderAddressSnapshotId,
    string RecipientName,
    string PhoneNumber,
    string Province,
    string City,
    string PostalCode,
    string AddressLine);

public sealed record InvoiceInfo(
    Guid Id,
    Guid OrderId,
    Guid CustomerId,
    Guid? PaymentId,
    string InvoiceNumber,
    InvoiceStatus Status,
    DateTimeOffset IssuedAt,
    long SubtotalRials,
    long DiscountRials,
    long ShippingRials,
    long GrandTotalRials,
    string? CustomerNameSnapshot,
    string? CustomerNationalIdSnapshot,
    DateTimeOffset? VoidedAt,
    string? VoidReason,
    InvoiceAddressSnapshotInfo? Address,
    StoreIdentitySnapshotInfo? Store,
    IReadOnlyList<InvoiceItemInfo> Items,
    string RowVersion);

public sealed record VoidInvoiceCommand(
    Guid ActorUserId,
    string Reason,
    string RowVersion);

public interface IInvoiceService
{
    Task<PagedResult<InvoiceInfo>> GetInvoicesAsync(
        Guid actorUserId,
        bool canReadAll,
        int page,
        int pageSize,
        InvoiceStatus? status,
        CancellationToken cancellationToken);

    Task<InvoiceInfo> GetInvoiceAsync(
        Guid invoiceId,
        Guid actorUserId,
        bool canReadAll,
        CancellationToken cancellationToken);

    Task<InvoiceInfo> VoidInvoiceAsync(
        Guid invoiceId,
        VoidInvoiceCommand command,
        CancellationToken cancellationToken);
}

public interface IInvoiceIssuanceService
{
    Task<InvoiceInfo> IssueForPaidOrderAsync(
        Guid orderId,
        Guid paymentId,
        DateTimeOffset issuedAt,
        CancellationToken cancellationToken);
}
