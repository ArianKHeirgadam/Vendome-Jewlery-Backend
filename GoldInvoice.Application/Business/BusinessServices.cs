using GoldInvoice.Application.Common;
using GoldInvoice.Domain.Business;

namespace GoldInvoice.Application.Business;

public sealed record SupplierInfo(
    Guid Id,
    string Code,
    string Name,
    string? ContactName,
    string? PhoneNumber,
    string? Email,
    string? NationalId,
    string? AddressLine,
    string? Notes,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string RowVersion);

public sealed record CreateSupplierCommand(
    string Code,
    string Name,
    string? ContactName,
    string? PhoneNumber,
    string? Email,
    string? NationalId,
    string? AddressLine,
    string? Notes);

public sealed record UpdateSupplierCommand(
    string Code,
    string Name,
    string? ContactName,
    string? PhoneNumber,
    string? Email,
    string? NationalId,
    string? AddressLine,
    string? Notes,
    bool IsActive,
    string RowVersion);

public sealed record CustomerInteractionInfo(
    Guid Id,
    Guid CustomerId,
    string CustomerName,
    string InteractionType,
    string Subject,
    string? Notes,
    DateTimeOffset OccurredAt,
    DateTimeOffset? NextFollowUpAt,
    string Status,
    DateTimeOffset? CompletedAt,
    string RowVersion);

public sealed record CreateCustomerInteractionCommand(
    Guid CustomerId,
    CustomerInteractionType InteractionType,
    string Subject,
    string? Notes,
    DateTimeOffset OccurredAt,
    DateTimeOffset? NextFollowUpAt);

public sealed record ChangeCustomerInteractionStatusCommand(
    CustomerInteractionStatus Status,
    string RowVersion);

public interface ISupplierService
{
    Task<PagedResult<SupplierInfo>> GetSuppliersAsync(
        int page,
        int pageSize,
        string? query,
        bool includeInactive,
        CancellationToken cancellationToken);

    Task<SupplierInfo> GetSupplierAsync(Guid supplierId, CancellationToken cancellationToken);

    Task<SupplierInfo> CreateSupplierAsync(
        CreateSupplierCommand command,
        CancellationToken cancellationToken);

    Task<SupplierInfo> UpdateSupplierAsync(
        Guid supplierId,
        UpdateSupplierCommand command,
        CancellationToken cancellationToken);

    Task DeleteSupplierAsync(Guid supplierId, CancellationToken cancellationToken);
}

public interface ICustomerInteractionService
{
    Task<PagedResult<CustomerInteractionInfo>> GetInteractionsAsync(
        int page,
        int pageSize,
        Guid? customerId,
        CustomerInteractionStatus? status,
        CancellationToken cancellationToken);

    Task<CustomerInteractionInfo> CreateInteractionAsync(
        CreateCustomerInteractionCommand command,
        CancellationToken cancellationToken);

    Task<CustomerInteractionInfo> ChangeStatusAsync(
        Guid interactionId,
        ChangeCustomerInteractionStatusCommand command,
        CancellationToken cancellationToken);
}
