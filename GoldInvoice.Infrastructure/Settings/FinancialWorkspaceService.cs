using System.Text.Json;

using GoldInvoice.Application.Settings;
using GoldInvoice.Domain.Platform;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoldInvoice.Infrastructure.Settings;

internal sealed class FinancialWorkspaceService(GoldInvoiceDbContext dbContext)
    : IFinancialWorkspaceService
{
    private const string KeyPrefix = "Finance.Workspace.Entry.";
    private const string DataType = "json:FinancialWorkspaceEntry.v1";

    public async Task<IReadOnlyList<FinancialWorkspaceEntryInfo>> ListAsync(
        CancellationToken cancellationToken)
    {
        var settings = await dbContext.SystemSettings
            .AsNoTracking()
            .Where(setting => setting.Key.StartsWith(KeyPrefix))
            .ToListAsync(cancellationToken);

        var entries = new List<FinancialWorkspaceEntryInfo>(settings.Count);

        foreach (var setting in settings)
        {
            if (!string.Equals(setting.DataType, DataType, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(setting.Value))
            {
                continue;
            }

            try
            {
                var document = JsonSerializer.Deserialize<FinancialWorkspaceEntryDocument>(setting.Value);
                if (document is null)
                {
                    continue;
                }

                entries.Add(Map(document));
            }
            catch (JsonException)
            {
                // Ignore only this malformed finance entry instead of breaking the whole workspace.
            }
        }

        return entries
            .OrderByDescending(entry => entry.OccurredOn)
            .ThenByDescending(entry => entry.Id)
            .ToArray();
    }

    public async Task<FinancialWorkspaceEntryInfo> CreateAsync(
        CreateFinancialWorkspaceEntryCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var scope = NormalizeScope(command.Scope);
        var entryType = NormalizeEntryType(command.EntryType);

        if (command.OccurredOn == default)
        {
            throw new ArgumentException("A valid occurrence date is required.", nameof(command.OccurredOn));
        }

        if (command.AmountRials <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command.AmountRials));
        }

        string? reason = null;

        if (string.Equals(scope, "Warehouse", StringComparison.Ordinal))
        {
            if (!string.Equals(entryType, "Expense", StringComparison.Ordinal))
            {
                throw new ArgumentException("Warehouse entries must be expenses.", nameof(command.EntryType));
            }

            reason = Required(command.Reason, nameof(command.Reason), 500);
        }

        var document = new FinancialWorkspaceEntryDocument(
            Guid.NewGuid(),
            scope,
            entryType,
            command.OccurredOn,
            command.AmountRials,
            reason);

        var json = JsonSerializer.Serialize(document);
        var setting = new SystemSetting(
            $"{KeyPrefix}{document.Id:N}",
            DataType,
            json,
            secretReference: null);

        dbContext.SystemSettings.Add(setting);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Map(document);
    }

    private static string NormalizeScope(string? value) =>
        value?.Trim() switch
        {
            "Warehouse" => "Warehouse",
            "Houman" => "Houman",
            "Ali" => "Ali",
            _ => throw new ArgumentException("The finance scope is invalid.", nameof(value)),
        };

    private static string NormalizeEntryType(string? value) =>
        value?.Trim() switch
        {
            "Expense" => "Expense",
            "Asset" => "Asset",
            _ => throw new ArgumentException("The finance entry type is invalid.", nameof(value)),
        };

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

    private static FinancialWorkspaceEntryInfo Map(FinancialWorkspaceEntryDocument document) =>
        new(
            document.Id,
            document.Scope,
            document.EntryType,
            document.OccurredOn,
            document.AmountRials,
            document.Reason);

    private sealed record FinancialWorkspaceEntryDocument(
        Guid Id,
        string Scope,
        string EntryType,
        DateOnly OccurredOn,
        long AmountRials,
        string? Reason);
}
