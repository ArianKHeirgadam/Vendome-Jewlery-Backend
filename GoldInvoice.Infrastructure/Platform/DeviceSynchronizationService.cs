using System.Security.Cryptography;
using System.Text;
using GoldInvoice.Application.Devices;
using GoldInvoice.Contracts.Devices;
using GoldInvoice.Domain.Platform;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoldInvoice.Infrastructure.Platform;

public sealed class DeviceSynchronizationService : IDeviceSynchronizationService
{
    private readonly GoldInvoiceDbContext _db;

    public DeviceSynchronizationService(GoldInvoiceDbContext db) => _db = db;

    public async Task<DeviceSynchronizationResult> SynchronizeAsync(
        Guid userId,
        IReadOnlyCollection<DeviceSnapshotRequest> devices,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty) throw new ArgumentException("Authenticated user id is required.", nameof(userId));

        var normalized = devices
            .Where(x => x is not null)
            .Select(x => new
            {
                Identifier = x.Identifier?.Trim() ?? string.Empty,
                DisplayName = x.DisplayName?.Trim() ?? string.Empty,
                Model = string.IsNullOrWhiteSpace(x.Model) ? null : x.Model.Trim(),
                Type = ParseType(x.Type)
            })
            .Where(x => x.Identifier.Length is > 0 and <= 1000 && x.DisplayName.Length is > 0 and <= 200)
            .GroupBy(x => Hash(x.Identifier), StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        var seenHashes = normalized.Select(x => Hash(x.Identifier)).ToHashSet(StringComparer.Ordinal);
        var owned = await _db.DesktopDevices.Where(x => x.RegisteredByUserId == userId).ToListAsync(cancellationToken);
        var allMatches = seenHashes.Count == 0
            ? []
            : await _db.DesktopDevices.Where(x => seenHashes.Contains(x.DeviceIdentifierHash)).ToListAsync(cancellationToken);
        var byHash = allMatches.ToDictionary(x => x.DeviceIdentifierHash, StringComparer.Ordinal);

        var added = 0;
        var updated = 0;
        var skipped = 0;
        var offline = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var item in normalized)
        {
            var hash = Hash(item.Identifier);
            if (byHash.TryGetValue(hash, out var existing))
            {
                if (existing.RegisteredByUserId != userId)
                {
                    skipped++;
                    continue;
                }

                existing.Refresh(item.DisplayName, item.Type, item.Model, now);
                updated++;
                continue;
            }

            _db.DesktopDevices.Add(new DesktopDevice(userId, hash, item.DisplayName, item.Type, item.Model, true));
            added++;
        }

        foreach (var device in owned)
        {
            if (!seenHashes.Contains(device.DeviceIdentifierHash) && device.IsOnline)
            {
                device.MarkOffline();
                offline++;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new DeviceSynchronizationResult(added, updated, offline, skipped);
    }

    private static DeviceType ParseType(string? value) =>
        Enum.TryParse<DeviceType>(value, true, out var parsed) && parsed is DeviceType.Printer or DeviceType.Scanner
            ? parsed
            : throw new ArgumentException("Device type must be Printer or Scanner.");

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
