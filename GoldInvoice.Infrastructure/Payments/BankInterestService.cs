using System.Text.Json;
using GoldInvoice.Application.Common;
using GoldInvoice.Application.Payments;
using GoldInvoice.Domain.Platform;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoldInvoice.Infrastructure.Payments;

internal sealed class BankInterestService(
    GoldInvoiceDbContext dbContext,
    TimeProvider timeProvider) : IBankInterestService
{
    private const string DepositPrefix = "Finance.BankInterest.Deposit.";
    private const string EntryPrefix = "Finance.BankInterest.Entry.";
    private const string DepositDataType = "json:BankDeposit.v1";
    private const string EntryDataType = "json:BankInterestEntry.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<BankInterestSnapshotInfo> GetSnapshotAsync(
        CancellationToken cancellationToken)
    {
        var deposits = await LoadDepositsAsync(asTracking: false, cancellationToken);
        var entries = await LoadEntriesAsync(cancellationToken);

        return new BankInterestSnapshotInfo(
            deposits
                .OrderByDescending(item => item.IsActive)
                .ThenByDescending(item => item.OpenedOn)
                .Select(MapDeposit)
                .ToArray(),
            entries
                .OrderByDescending(item => item.OccurredOn)
                .ThenByDescending(item => item.CreatedAt)
                .Select(MapEntry)
                .ToArray());
    }

    public async Task<BankDepositInfo> CreateDepositAsync(
        CreateBankDepositCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var bankName = Required(command.BankName, nameof(command.BankName), 120);
        var title = Required(command.Title, nameof(command.Title), 160);
        var accountNumber = Optional(command.AccountNumber, nameof(command.AccountNumber), 64);

        if (command.PrincipalRials <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command.PrincipalRials));
        }

        if (command.AnnualInterestRatePercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(command.AnnualInterestRatePercent));
        }

        if (command.OpenedOn == default)
        {
            throw new ArgumentException("A valid opening date is required.", nameof(command.OpenedOn));
        }

        if (command.MaturityOn is not null && command.MaturityOn < command.OpenedOn)
        {
            throw new ArgumentException("Maturity cannot be before opening date.", nameof(command.MaturityOn));
        }

        var document = new BankDepositDocument(
            Guid.NewGuid(),
            bankName,
            title,
            accountNumber,
            command.PrincipalRials,
            command.AnnualInterestRatePercent,
            command.OpenedOn,
            command.MaturityOn,
            IsActive: true,
            CreatedAt: timeProvider.GetUtcNow(),
            ClosedAt: null);

        dbContext.SystemSettings.Add(NewSetting(
            $"{DepositPrefix}{document.Id:N}",
            DepositDataType,
            document));

        await dbContext.SaveChangesAsync(cancellationToken);
        return MapDeposit(document);
    }

    public async Task<BankInterestEntryInfo> AddEntryAsync(
        AddBankInterestEntryCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var direction = command.Direction?.Trim() switch
        {
            "Received" => "Received",
            "Paid" => "Paid",
            _ => throw new ArgumentException("Direction must be Received or Paid.", nameof(command.Direction)),
        };

        var bankName = Required(command.BankName, nameof(command.BankName), 120);
        var reference = Optional(command.Reference, nameof(command.Reference), 200);

        if (command.OccurredOn == default)
        {
            throw new ArgumentException("A valid occurrence date is required.", nameof(command.OccurredOn));
        }

        if (command.AmountRials <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command.AmountRials));
        }

        if (command.DepositId is not null)
        {
            var deposit = (await LoadDepositsAsync(asTracking: false, cancellationToken))
                .SingleOrDefault(item => item.Id == command.DepositId.Value)
                ?? throw new ApplicationResourceNotFoundException();

            if (!deposit.IsActive)
            {
                throw new ApplicationConflictException();
            }
        }

        var document = new BankInterestEntryDocument(
            Guid.NewGuid(),
            command.DepositId,
            direction,
            bankName,
            command.OccurredOn,
            command.AmountRials,
            reference,
            timeProvider.GetUtcNow());

        dbContext.SystemSettings.Add(NewSetting(
            $"{EntryPrefix}{document.Id:N}",
            EntryDataType,
            document));

        await dbContext.SaveChangesAsync(cancellationToken);
        return MapEntry(document);
    }

    public async Task<BankDepositInfo> CloseDepositAsync(
        Guid depositId,
        CancellationToken cancellationToken)
    {
        if (depositId == Guid.Empty)
        {
            throw new ArgumentException("A valid deposit is required.", nameof(depositId));
        }

        var settings = await dbContext.SystemSettings
            .Where(setting => setting.Key.StartsWith(DepositPrefix))
            .ToListAsync(cancellationToken);

        var tracked = settings
            .Select(setting => new
            {
                Setting = setting,
                Document = Deserialize<BankDepositDocument>(setting, DepositDataType),
            })
            .SingleOrDefault(item => item.Document?.Id == depositId)
            ?? throw new ApplicationResourceNotFoundException();

        var document = tracked.Document!;
        if (!document.IsActive)
        {
            return MapDeposit(document);
        }

        var updated = document with
        {
            IsActive = false,
            ClosedAt = timeProvider.GetUtcNow(),
        };

        tracked.Setting.UpdateValue(
            DepositDataType,
            JsonSerializer.Serialize(updated, JsonOptions));

        await dbContext.SaveChangesAsync(cancellationToken);
        return MapDeposit(updated);
    }

    private async Task<List<BankDepositDocument>> LoadDepositsAsync(
        bool asTracking,
        CancellationToken cancellationToken)
    {
        var query = dbContext.SystemSettings
            .Where(setting => setting.Key.StartsWith(DepositPrefix));

        var settings = asTracking
            ? await query.ToListAsync(cancellationToken)
            : await query.AsNoTracking().ToListAsync(cancellationToken);

        return settings
            .Select(setting => Deserialize<BankDepositDocument>(setting, DepositDataType))
            .Where(item => item is not null)
            .Cast<BankDepositDocument>()
            .ToList();
    }

    private async Task<List<BankInterestEntryDocument>> LoadEntriesAsync(
        CancellationToken cancellationToken)
    {
        var settings = await dbContext.SystemSettings
            .AsNoTracking()
            .Where(setting => setting.Key.StartsWith(EntryPrefix))
            .ToListAsync(cancellationToken);

        return settings
            .Select(setting => Deserialize<BankInterestEntryDocument>(setting, EntryDataType))
            .Where(item => item is not null)
            .Cast<BankInterestEntryDocument>()
            .ToList();
    }

    private static SystemSetting NewSetting<T>(
        string key,
        string dataType,
        T document) =>
        new(
            key,
            dataType,
            JsonSerializer.Serialize(document, JsonOptions),
            secretReference: null);

    private static T? Deserialize<T>(
        SystemSetting setting,
        string expectedDataType)
    {
        if (!string.Equals(setting.DataType, expectedDataType, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(setting.Value))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(setting.Value, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static BankDepositInfo MapDeposit(BankDepositDocument item) =>
        new(
            item.Id,
            item.BankName,
            item.Title,
            item.AccountNumber,
            item.PrincipalRials,
            item.AnnualInterestRatePercent,
            item.OpenedOn,
            item.MaturityOn,
            item.IsActive,
            item.CreatedAt,
            item.ClosedAt);

    private static BankInterestEntryInfo MapEntry(BankInterestEntryDocument item) =>
        new(
            item.Id,
            item.DepositId,
            item.Direction,
            item.BankName,
            item.OccurredOn,
            item.AmountRials,
            item.Reference,
            item.CreatedAt);

    private static string Required(
        string? value,
        string parameterName,
        int maximumLength)
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

    private static string? Optional(
        string? value,
        string parameterName,
        int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Required(value, parameterName, maximumLength);

    private sealed record BankDepositDocument(
        Guid Id,
        string BankName,
        string Title,
        string? AccountNumber,
        long PrincipalRials,
        decimal AnnualInterestRatePercent,
        DateOnly OpenedOn,
        DateOnly? MaturityOn,
        bool IsActive,
        DateTimeOffset CreatedAt,
        DateTimeOffset? ClosedAt);

    private sealed record BankInterestEntryDocument(
        Guid Id,
        Guid? DepositId,
        string Direction,
        string BankName,
        DateOnly OccurredOn,
        long AmountRials,
        string? Reference,
        DateTimeOffset CreatedAt);
}
