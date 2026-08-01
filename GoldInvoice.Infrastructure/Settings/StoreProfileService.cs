using System.Text.Json;
using GoldInvoice.Application.Common;
using GoldInvoice.Application.Settings;
using GoldInvoice.Domain.Platform;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoldInvoice.Infrastructure.Settings;

internal sealed class StoreProfileService(GoldInvoiceDbContext dbContext) : IStoreProfileService
{
    internal const string SettingKey = "Store.Profile";
    private const string DataType = "json:StoreProfile.v1";

    public async Task<StoreProfileInfo> GetAsync(CancellationToken cancellationToken)
    {
        var setting = await dbContext.SystemSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Key == SettingKey, cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        return Map(setting);
    }

    public async Task<StoreProfileInfo> UpsertAsync(
        UpdateStoreProfileCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var document = Normalize(command);
        var json = JsonSerializer.Serialize(document);
        var setting = await dbContext.SystemSettings
            .SingleOrDefaultAsync(candidate => candidate.Key == SettingKey, cancellationToken);
        if (setting is null)
        {
            if (!string.IsNullOrWhiteSpace(command.RowVersion))
            {
                throw new ApplicationConflictException();
            }

            setting = new SystemSetting(SettingKey, DataType, json, secretReference: null);
            dbContext.SystemSettings.Add(setting);
        }
        else
        {
            SetOriginalRowVersion(setting, command.RowVersion);
            setting.UpdateValue(DataType, json);
        }

        await SaveChangesAsync(cancellationToken);
        return Map(setting);
    }

    private static StoreProfileDocument Normalize(UpdateStoreProfileCommand command) => new(
        Required(command.TradeName, nameof(command.TradeName), 200),
        Required(command.LegalName, nameof(command.LegalName), 200),
        Optional(command.NationalId, nameof(command.NationalId), 32),
        Optional(command.EconomicCode, nameof(command.EconomicCode), 32),
        Optional(command.RegistrationNumber, nameof(command.RegistrationNumber), 32),
        Required(command.PhoneNumber, nameof(command.PhoneNumber), 32),
        Required(command.PostalCode, nameof(command.PostalCode), 20),
        Required(command.AddressLine, nameof(command.AddressLine), 1000));

    private static StoreProfileInfo Map(SystemSetting setting)
    {
        if (!string.Equals(setting.DataType, DataType, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(setting.Value))
        {
            throw new ApplicationConflictException();
        }

        StoreProfileDocument document;
        try
        {
            document = JsonSerializer.Deserialize<StoreProfileDocument>(setting.Value) ??
                throw new JsonException("The store profile is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The stored store profile is invalid.", exception);
        }

        return new StoreProfileInfo(
            document.TradeName,
            document.LegalName,
            document.NationalId,
            document.EconomicCode,
            document.RegistrationNumber,
            document.PhoneNumber,
            document.PostalCode,
            document.AddressLine,
            Convert.ToBase64String(setting.RowVersion));
    }

    private void SetOriginalRowVersion(SystemSetting setting, string? value) =>
        dbContext.Entry(setting).Property(item => item.RowVersion).OriginalValue = DecodeRowVersion(value);

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ApplicationConcurrencyException();
        }
        catch (DbUpdateException)
        {
            throw new ApplicationConflictException();
        }
    }

    private static byte[] DecodeRowVersion(string? value)
    {
        try
        {
            return Convert.FromBase64String(value ?? string.Empty);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("The concurrency token is invalid.", nameof(value), exception);
        }
    }

    private static string Required(string? value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return normalized;
    }

    private static string? Optional(string? value, string parameterName, int maximumLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, parameterName, maximumLength);

    private sealed record StoreProfileDocument(
        string TradeName,
        string LegalName,
        string? NationalId,
        string? EconomicCode,
        string? RegistrationNumber,
        string PhoneNumber,
        string PostalCode,
        string AddressLine);
}
