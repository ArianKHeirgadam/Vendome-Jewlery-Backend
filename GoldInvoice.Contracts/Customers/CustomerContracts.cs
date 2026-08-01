using System.ComponentModel.DataAnnotations;

namespace GoldInvoice.Contracts.Customers;

public sealed class CustomerAddressResponse
{
    public required Guid Id { get; init; }
    public required Guid CustomerId { get; init; }
    public required string Title { get; init; }
    public required string RecipientName { get; init; }
    public required string PhoneNumber { get; init; }
    public required string Province { get; init; }
    public required string City { get; init; }
    public required string PostalCode { get; init; }
    public required string AddressLine { get; init; }
    public required bool IsDefault { get; init; }
    public required string RowVersion { get; init; }
}

public class CreateCustomerAddressRequest
{
    [Required, StringLength(100)]
    public string Title { get; init; } = string.Empty;

    [Required, StringLength(200)]
    public string RecipientName { get; init; } = string.Empty;

    [Required, StringLength(32)]
    public string PhoneNumber { get; init; } = string.Empty;

    [Required, StringLength(100)]
    public string Province { get; init; } = string.Empty;

    [Required, StringLength(100)]
    public string City { get; init; } = string.Empty;

    [Required, StringLength(20)]
    public string PostalCode { get; init; } = string.Empty;

    [Required, StringLength(1000)]
    public string AddressLine { get; init; } = string.Empty;

    public bool IsDefault { get; init; }
}

public sealed class UpdateCustomerAddressRequest : CreateCustomerAddressRequest
{
    [Required, StringLength(256)]
    public string RowVersion { get; init; } = string.Empty;
}
