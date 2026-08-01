using System.ComponentModel.DataAnnotations;

namespace GoldInvoice.Contracts.Settings;

public sealed class StoreProfileResponse
{
    public required string TradeName { get; init; }
    public required string LegalName { get; init; }
    public string? NationalId { get; init; }
    public string? EconomicCode { get; init; }
    public string? RegistrationNumber { get; init; }
    public required string PhoneNumber { get; init; }
    public required string PostalCode { get; init; }
    public required string AddressLine { get; init; }
    public required string RowVersion { get; init; }
}

public sealed class UpdateStoreProfileRequest
{
    [Required, StringLength(200)]
    public string TradeName { get; init; } = string.Empty;

    [Required, StringLength(200)]
    public string LegalName { get; init; } = string.Empty;

    [StringLength(32)]
    public string? NationalId { get; init; }

    [StringLength(32)]
    public string? EconomicCode { get; init; }

    [StringLength(32)]
    public string? RegistrationNumber { get; init; }

    [Required, StringLength(32)]
    public string PhoneNumber { get; init; } = string.Empty;

    [Required, StringLength(20)]
    public string PostalCode { get; init; } = string.Empty;

    [Required, StringLength(1000)]
    public string AddressLine { get; init; } = string.Empty;

    [StringLength(256)]
    public string? RowVersion { get; init; }
}
