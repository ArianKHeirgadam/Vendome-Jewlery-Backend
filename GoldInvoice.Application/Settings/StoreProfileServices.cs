namespace GoldInvoice.Application.Settings;

public sealed record StoreProfileInfo(
    string TradeName,
    string LegalName,
    string? NationalId,
    string? EconomicCode,
    string? RegistrationNumber,
    string PhoneNumber,
    string PostalCode,
    string AddressLine,
    string RowVersion);

public sealed record UpdateStoreProfileCommand(
    string TradeName,
    string LegalName,
    string? NationalId,
    string? EconomicCode,
    string? RegistrationNumber,
    string PhoneNumber,
    string PostalCode,
    string AddressLine,
    string? RowVersion);

public interface IStoreProfileService
{
    Task<StoreProfileInfo> GetAsync(CancellationToken cancellationToken);

    Task<StoreProfileInfo> UpsertAsync(
        UpdateStoreProfileCommand command,
        CancellationToken cancellationToken);
}
