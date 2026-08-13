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
    long? AcquisitionUnitCostRials,
    long? AcquisitionTotalCostRials,
    long? GrossProfitRials,
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

public sealed record CorrectInvoiceDocumentCommand(
    Guid ActorUserId,
    string CustomerName,
    string? CustomerNationalId,
    string RecipientName,
    string PhoneNumber,
    string Province,
    string City,
    string PostalCode,
    string AddressLine,
    string Reason,
    string RowVersion);

public sealed record RequestInvoicePrintCommand(
    Guid ActorUserId,
    int Copies,
    bool CanReprint,
    string? ReprintReason);

public sealed record CompleteInvoicePrintCommand(
    Guid ActorUserId,
    bool Succeeded,
    string? PrinterName,
    string? FailureCode,
    string RowVersion);

public sealed record InvoicePrintInfo(
    Guid Id,
    Guid InvoiceId,
    Guid RequestedByUserId,
    InvoicePrintStatus Status,
    int Copies,
    bool IsReprint,
    string? ReprintReason,
    string? PrinterName,
    DateTimeOffset? CompletedAt,
    string? FailureCode,
    DateTimeOffset CreatedAt,
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

    Task<InvoiceInfo> CorrectDocumentAsync(
        Guid invoiceId,
        CorrectInvoiceDocumentCommand command,
        CancellationToken cancellationToken);

    Task<InvoicePrintInfo> RequestPrintAsync(
        Guid invoiceId,
        RequestInvoicePrintCommand command,
        CancellationToken cancellationToken);

    Task<InvoicePrintInfo> CompletePrintAsync(
        Guid invoiceId,
        Guid printJobId,
        CompleteInvoicePrintCommand command,
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
