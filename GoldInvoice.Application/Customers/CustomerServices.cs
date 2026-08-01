namespace GoldInvoice.Application.Customers;

public sealed record CustomerAddressInfo(
    Guid Id,
    Guid CustomerId,
    string Title,
    string RecipientName,
    string PhoneNumber,
    string Province,
    string City,
    string PostalCode,
    string AddressLine,
    bool IsDefault,
    string RowVersion);

public sealed record CreateCustomerAddressCommand(
    Guid ActorUserId,
    Guid CustomerId,
    bool CanManageCustomer,
    string Title,
    string RecipientName,
    string PhoneNumber,
    string Province,
    string City,
    string PostalCode,
    string AddressLine,
    bool IsDefault);

public sealed record UpdateCustomerAddressCommand(
    Guid ActorUserId,
    Guid CustomerId,
    bool CanManageCustomer,
    string Title,
    string RecipientName,
    string PhoneNumber,
    string Province,
    string City,
    string PostalCode,
    string AddressLine,
    bool IsDefault,
    string RowVersion);

public interface ICustomerAddressService
{
    Task<IReadOnlyList<CustomerAddressInfo>> GetAddressesAsync(
        Guid actorUserId,
        Guid customerId,
        bool canManageCustomer,
        CancellationToken cancellationToken);

    Task<CustomerAddressInfo> GetAddressAsync(
        Guid addressId,
        Guid actorUserId,
        bool canManageCustomer,
        CancellationToken cancellationToken);

    Task<CustomerAddressInfo> CreateAddressAsync(
        CreateCustomerAddressCommand command,
        CancellationToken cancellationToken);

    Task<CustomerAddressInfo> UpdateAddressAsync(
        Guid addressId,
        UpdateCustomerAddressCommand command,
        CancellationToken cancellationToken);

    Task DeleteAddressAsync(
        Guid addressId,
        Guid actorUserId,
        bool canManageCustomer,
        string rowVersion,
        CancellationToken cancellationToken);
}
