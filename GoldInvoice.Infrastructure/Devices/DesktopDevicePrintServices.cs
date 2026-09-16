using System.Security.Cryptography;
using System.Text;
using GoldInvoice.Application.Common;
using GoldInvoice.Application.Platform;
using GoldInvoice.Application.Security;
using GoldInvoice.Domain.Invoicing;
using GoldInvoice.Domain.Payments;
using GoldInvoice.Domain.Platform;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoldInvoice.Infrastructure.Devices;

internal static class DevicePublicKeyService
{
    private static readonly string[] FailureCodeAllowList =
    [
        "PRINTER_UNAVAILABLE",
        "PRINTER_OFFLINE",
        "OUT_OF_PAPER",
        "PRINTER_JAM",
        "PRINT_CANCELLED",
        "UNSUPPORTED_PAPER_SIZE",
        "GENERIC_FAILURE"
    ];

    public static string ComputeThumbprint(string publicKeyPem)
    {
        var der = DecodePublicKey(publicKeyPem);
        return Convert.ToHexString(SHA256.HashData(der));
    }

    public static bool IsValidFailureCode(string? failureCode) =>
        failureCode is not null && FailureCodeAllowList.Contains(failureCode, StringComparer.Ordinal);

    public static bool Verify(string? publicKeyPem, string payload, string signature)
    {
        if (string.IsNullOrWhiteSpace(publicKeyPem)) return false;
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            return rsa.VerifyData(Encoding.UTF8.GetBytes(payload), Convert.FromBase64String(signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException) { return false; }
    }

    private static byte[] DecodePublicKey(string publicKeyPem)
    {
        var base64 = new StringBuilder();
        foreach (var line in publicKeyPem.Split('\n', StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith("-----BEGIN", StringComparison.Ordinal) && !line.StartsWith("-----END", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(line)) base64.Append(line);
        }
        return Convert.FromBase64String(base64.ToString());
    }
}

internal sealed class DesktopDeviceService(
    GoldInvoiceDbContext dbContext,
    TimeProvider timeProvider) : IDesktopDeviceService
{
    private const int MaximumPageSize = 100;
    private static readonly TimeSpan MaximumSignatureAge = TimeSpan.FromMinutes(5);

    public async Task<DeviceRegistrationTokenInfo> IssueRegistrationTokenAsync(IssueDeviceRegistrationTokenCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateActor(command.ActorUserId);
        if (command.ExpiresInMinutes is < 1 or > 1440) throw new ArgumentOutOfRangeException(nameof(command));
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var token = new DeviceRegistrationToken(command.ActorUserId, HashToken(rawToken), timeProvider.GetUtcNow().AddMinutes(command.ExpiresInMinutes));
        dbContext.DeviceRegistrationTokens.Add(token);
        await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken);
        return new DeviceRegistrationTokenInfo(rawToken, token.ExpiresAt);
    }

    public async Task<DeviceInfo> EnrollAsync(EnrollDeviceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.RegistrationToken) || command.RegistrationToken.Length > 128 || string.IsNullOrWhiteSpace(command.DeviceIdentifierHash) || command.DeviceIdentifierHash.Length > 128 || string.IsNullOrWhiteSpace(command.DisplayName) || command.DisplayName.Length > 200 || string.IsNullOrWhiteSpace(command.PublicKeyPem) || command.PublicKeyPem.Length > 4000) throw new ArgumentException("A complete enrollment payload is required.", nameof(command));
        await using var transaction = await PersistenceUtilities.BeginSerializableTransactionAsync(dbContext, cancellationToken);
        var tokenHash = HashToken(command.RegistrationToken);
        var token = await dbContext.DeviceRegistrationTokens.SingleOrDefaultAsync(candidate => candidate.TokenValueHash == tokenHash, cancellationToken) ?? throw new SecurityAccessDeniedException();
        if (!token.IsUsableAt(timeProvider.GetUtcNow())) throw new SecurityAccessDeniedException();
        token.MarkUsed(timeProvider.GetUtcNow());
        var device = new DesktopDevice(token.CreatedById, command.DeviceIdentifierHash, command.DisplayName, command.PublicKeyPem, DevicePublicKeyService.ComputeThumbprint(command.PublicKeyPem));
        dbContext.DesktopDevices.Add(device);
        await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken);
        await PersistenceUtilities.CommitAsync(transaction, cancellationToken);
        return await GetDeviceAsync(device.Id, token.CreatedById, cancellationToken);
    }

    public async Task<DeviceInfo> ApproveAsync(Guid deviceId, ApproveDeviceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command); ValidateActor(command.ActorUserId);
        await using var transaction = await PersistenceUtilities.BeginSerializableTransactionAsync(dbContext, cancellationToken);
        var device = await dbContext.DesktopDevices.FindAsync([deviceId], cancellationToken) ?? throw new ApplicationResourceNotFoundException();
        PersistenceUtilities.SetOriginalRowVersion(dbContext, device, command.RowVersion); device.Approve(timeProvider.GetUtcNow());
        await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken); await PersistenceUtilities.CommitAsync(transaction, cancellationToken);
        return await GetDeviceAsync(device.Id, command.ActorUserId, cancellationToken);
    }

    public async Task<DeviceInfo> RevokeAsync(Guid deviceId, RevokeDeviceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command); ValidateActor(command.ActorUserId);
        await using var transaction = await PersistenceUtilities.BeginSerializableTransactionAsync(dbContext, cancellationToken);
        var device = await dbContext.DesktopDevices.FindAsync([deviceId], cancellationToken) ?? throw new ApplicationResourceNotFoundException();
        PersistenceUtilities.SetOriginalRowVersion(dbContext, device, command.RowVersion); device.Revoke(timeProvider.GetUtcNow());
        await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken); await PersistenceUtilities.CommitAsync(transaction, cancellationToken);
        return await GetDeviceAsync(device.Id, command.ActorUserId, cancellationToken);
    }

    public async Task HeartbeatAsync(Guid deviceId, DeviceHeartbeatCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var device = await dbContext.DesktopDevices.SingleOrDefaultAsync(candidate => candidate.Id == deviceId, cancellationToken) ?? throw new ApplicationResourceNotFoundException();
        VerifyDeviceSignature(device, $"heartbeat|{device.Id:N}|{command.Timestamp:o}", command.Signature, command.Timestamp);
        device.Heartbeat(timeProvider.GetUtcNow()); await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken);
    }

    public async Task<PagedResult<DeviceInfo>> GetDevicesAsync(Guid actorUserId, int page, int pageSize, CancellationToken cancellationToken)
    {
        ValidateActor(actorUserId); ValidatePage(page, pageSize); var query = dbContext.DesktopDevices.AsNoTracking().OrderBy(device => device.CreatedAt);
        var totalCount = await query.CountAsync(cancellationToken); var devices = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var infos = await Task.WhenAll(devices.Select(device => MapDeviceAsync(device, cancellationToken))); return new PagedResult<DeviceInfo>(infos, page, pageSize, totalCount);
    }

    public async Task<DeviceInfo> GetDeviceAsync(Guid deviceId, Guid actorUserId, CancellationToken cancellationToken)
    {
        ValidateActor(actorUserId); var device = await dbContext.DesktopDevices.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == deviceId, cancellationToken) ?? throw new ApplicationResourceNotFoundException();
        return await MapDeviceAsync(device, cancellationToken);
    }

    public async Task<DevicePrinterInfo> RegisterPrinterAsync(RegisterDevicePrinterCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command); ValidateActor(command.ActorUserId);
        if (string.IsNullOrWhiteSpace(command.SystemPrinterName) || command.SystemPrinterName.Length > 300 || string.IsNullOrWhiteSpace(command.DisplayName) || command.DisplayName.Length > 200) throw new ArgumentException("A complete printer payload is required.", nameof(command));
        var device = await LoadActiveDeviceAsync(command.DeviceId, cancellationToken); var printer = new DevicePrinter(device.Id, command.SystemPrinterName, command.DisplayName, command.PrinterType); printer.MarkSeen(timeProvider.GetUtcNow()); dbContext.DevicePrinters.Add(printer);
        try { await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken); } catch (DbUpdateException) { throw new ApplicationConflictException(); } return MapPrinter(printer);
    }

    public async Task<DevicePrinterInfo> SetPrinterDefaultAsync(SetDevicePrinterDefaultCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command); ValidateActor(command.ActorUserId); await using var transaction = await PersistenceUtilities.BeginSerializableTransactionAsync(dbContext, cancellationToken);
        var printer = await LoadOwnedPrinterAsync(command.DeviceId, command.PrinterId, cancellationToken); if (command.IsDefault && !printer.IsEnabled) throw new ApplicationConflictException();
        if (command.IsDefault) foreach (var other in await dbContext.DevicePrinters.Where(x => x.DesktopDeviceId == printer.DesktopDeviceId && x.Id != printer.Id && x.IsDefault).ToListAsync(cancellationToken)) other.SetDefault(false);
        PersistenceUtilities.SetOriginalRowVersion(dbContext, printer, command.RowVersion); printer.SetDefault(command.IsDefault); await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken); await PersistenceUtilities.CommitAsync(transaction, cancellationToken); return MapPrinter(printer);
    }

    public async Task<DevicePrinterInfo> SetPrinterEnabledAsync(SetDevicePrinterEnabledCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command); ValidateActor(command.ActorUserId); await using var transaction = await PersistenceUtilities.BeginSerializableTransactionAsync(dbContext, cancellationToken);
        var printer = await LoadOwnedPrinterAsync(command.DeviceId, command.PrinterId, cancellationToken); if (!command.IsEnabled && printer.IsDefault) printer.SetDefault(false);
        PersistenceUtilities.SetOriginalRowVersion(dbContext, printer, command.RowVersion); printer.SetEnabled(command.IsEnabled); await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken); await PersistenceUtilities.CommitAsync(transaction, cancellationToken); return MapPrinter(printer);
    }

    public async Task<PrintProfileInfo> CreatePrintProfileAsync(CreateDevicePrintProfileCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command); ValidateActor(command.ActorUserId); if (string.IsNullOrWhiteSpace(command.Name) || command.Name.Length > 200) throw new ArgumentException("A profile name is required.", nameof(command));
        var device = await LoadActiveDeviceAsync(command.DeviceId, cancellationToken); var profile = new PrintProfile(device.Id, command.Name, command.PaperSize, command.Orientation, command.Copies, command.ColorMode, command.MarginLeftMillimeters, command.MarginRightMillimeters, command.MarginTopMillimeters, command.MarginBottomMillimeters); dbContext.PrintProfiles.Add(profile);
        try { await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken); } catch (DbUpdateException) { throw new ApplicationConflictException(); } return MapProfile(profile);
    }

    public async Task<PrintProfileInfo> SetPrintProfileDefaultAsync(SetDevicePrintProfileDefaultCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command); ValidateActor(command.ActorUserId); await using var transaction = await PersistenceUtilities.BeginSerializableTransactionAsync(dbContext, cancellationToken); var profile = await LoadOwnedProfileAsync(command.DeviceId, command.ProfileId, cancellationToken); if (command.IsDefault && !profile.IsEnabled) throw new ApplicationConflictException();
        if (command.IsDefault) foreach (var other in await dbContext.PrintProfiles.Where(x => x.DesktopDeviceId == profile.DesktopDeviceId && x.Id != profile.Id && x.IsDefault).ToListAsync(cancellationToken)) other.SetDefault(false);
        PersistenceUtilities.SetOriginalRowVersion(dbContext, profile, command.RowVersion); profile.SetDefault(command.IsDefault); await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken); await PersistenceUtilities.CommitAsync(transaction, cancellationToken); return MapProfile(profile);
    }

    public async Task<PrintProfileInfo> SetPrintProfileEnabledAsync(SetDevicePrintProfileEnabledCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command); ValidateActor(command.ActorUserId); await using var transaction = await PersistenceUtilities.BeginSerializableTransactionAsync(dbContext, cancellationToken); var profile = await LoadOwnedProfileAsync(command.DeviceId, command.ProfileId, cancellationToken); if (!command.IsEnabled && profile.IsDefault) profile.SetDefault(false);
        PersistenceUtilities.SetOriginalRowVersion(dbContext, profile, command.RowVersion); profile.SetEnabled(command.IsEnabled); await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken); await PersistenceUtilities.CommitAsync(transaction, cancellationToken); return MapProfile(profile);
    }

    private async Task<DesktopDevice> LoadActiveDeviceAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        var device = await dbContext.DesktopDevices.SingleOrDefaultAsync(x => x.Id == deviceId, cancellationToken) ?? throw new ApplicationResourceNotFoundException(); if (!device.IsActive || device.RevokedAt is not null || device.ApprovedAt is null) throw new ApplicationConflictException(); return device;
    }
    private async Task<DevicePrinter> LoadOwnedPrinterAsync(Guid deviceId, Guid printerId, CancellationToken cancellationToken) => await dbContext.DevicePrinters.SingleOrDefaultAsync(x => x.Id == printerId && x.DesktopDeviceId == deviceId, cancellationToken) ?? throw new ApplicationResourceNotFoundException();
    private async Task<PrintProfile> LoadOwnedProfileAsync(Guid deviceId, Guid profileId, CancellationToken cancellationToken) => await dbContext.PrintProfiles.SingleOrDefaultAsync(x => x.Id == profileId && x.DesktopDeviceId == deviceId, cancellationToken) ?? throw new ApplicationResourceNotFoundException();

    private async Task<DeviceInfo> MapDeviceAsync(DesktopDevice device, CancellationToken cancellationToken)
    {
        var printers = await dbContext.DevicePrinters.AsNoTracking().Where(x => x.DesktopDeviceId == device.Id).OrderBy(x => x.CreatedAt).ToListAsync(cancellationToken); var profiles = await dbContext.PrintProfiles.AsNoTracking().Where(x => x.DesktopDeviceId == device.Id).OrderBy(x => x.CreatedAt).ToListAsync(cancellationToken);
        return new DeviceInfo(device.Id, device.DisplayName, device.IsActive, device.ApprovedAt, device.LastSeenAt, device.RevokedAt, device.CreatedAt, Convert.ToBase64String(device.RowVersion), printers.Select(MapPrinter).ToArray(), profiles.Select(MapProfile).ToArray());
    }
    private static DevicePrinterInfo MapPrinter(DevicePrinter printer) => new(printer.Id, printer.SystemPrinterName, printer.DisplayName, printer.PrinterType, printer.IsDefault, printer.IsEnabled, printer.LastSeenAt, printer.CreatedAt, Convert.ToBase64String(printer.RowVersion));
    private static PrintProfileInfo MapProfile(PrintProfile profile) => new(profile.Id, profile.Name, profile.PaperSize, profile.Orientation, profile.Copies, profile.ColorMode, profile.MarginLeftMillimeters, profile.MarginRightMillimeters, profile.MarginTopMillimeters, profile.MarginBottomMillimeters, profile.IsDefault, profile.IsEnabled, profile.CreatedAt, Convert.ToBase64String(profile.RowVersion));

    internal void VerifyDeviceSignature(DesktopDevice device, string payload, string signature, DateTimeOffset signedAt)
    {
        var now = timeProvider.GetUtcNow(); if (signedAt > now.Add(MaximumSignatureAge) || signedAt < now.Add(-MaximumSignatureAge) || !DevicePublicKeyService.Verify(device.PublicKeyPem, payload, signature)) throw new SecurityAccessDeniedException();
    }
    private static string HashToken(string rawToken) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
    private static void ValidateActor(Guid actorUserId) { if (actorUserId == Guid.Empty) throw new ArgumentException("A valid actor identifier is required.", nameof(actorUserId)); }
    private static void ValidatePage(int page, int pageSize) { if (page < 1 || pageSize is < 1 or > MaximumPageSize || ((long)page - 1) * pageSize > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(page)); }
}

internal sealed class InvoicePrintJobService(GoldInvoiceDbContext dbContext, DesktopDeviceService deviceService, TimeProvider timeProvider) : IInvoicePrintJobService
{
    private const int MaximumPageSize = 100;
    private const int LeaseMinutes = 3;

    public async Task<InvoicePrintJobInfo> RequestDevicePrintAsync(Guid invoiceId, RequestDevicePrintCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command); if (command.ActorUserId == Guid.Empty) throw new ArgumentException("A valid actor identifier is required.", nameof(command)); if (command.Copies is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(command)); if (!string.IsNullOrWhiteSpace(command.IdempotencyKey) && command.IdempotencyKey.Length > 128) throw new ArgumentOutOfRangeException(nameof(command));
        await using var transaction = await PersistenceUtilities.BeginSerializableTransactionAsync(dbContext, cancellationToken);
        var invoice = await dbContext.Invoices.AsNoTracking().SingleOrDefaultAsync(x => x.Id == invoiceId, cancellationToken) ?? throw new ApplicationResourceNotFoundException();
        if (invoice.Status != InvoiceStatus.Issued || invoice.PaymentId is null || !await dbContext.Payments.AnyAsync(x => x.Id == invoice.PaymentId && x.OrderId == invoice.OrderId && x.Status == PaymentStatus.Verified, cancellationToken)) throw new ApplicationConflictException();
        var device = await dbContext.DesktopDevices.SingleOrDefaultAsync(x => x.Id == command.DesktopDeviceId, cancellationToken) ?? throw new ApplicationResourceNotFoundException(); if (!device.IsActive || device.RevokedAt is not null || device.ApprovedAt is null) throw new ApplicationConflictException();
        if (command.DevicePrinterId is not null) { var printer = await dbContext.DevicePrinters.SingleOrDefaultAsync(x => x.Id == command.DevicePrinterId && x.DesktopDeviceId == device.Id, cancellationToken); if (printer is null) throw new ApplicationResourceNotFoundException(); if (!printer.IsEnabled) throw new ApplicationConflictException(); }
        if (command.PrintProfileId is not null) { var profile = await dbContext.PrintProfiles.SingleOrDefaultAsync(x => x.Id == command.PrintProfileId && x.DesktopDeviceId == device.Id, cancellationToken); if (profile is null) throw new ApplicationResourceNotFoundException(); if (!profile.IsEnabled) throw new ApplicationConflictException(); }
        var idempotencyKeyHash = string.IsNullOrWhiteSpace(command.IdempotencyKey) ? null : command.IdempotencyKey.Trim().ToUpperInvariant();
        if (idempotencyKeyHash is not null) { var existing = await dbContext.InvoicePrintJobs.AsNoTracking().SingleOrDefaultAsync(x => x.DesktopDeviceId == device.Id && x.IdempotencyKeyHash == idempotencyKeyHash, cancellationToken); if (existing is not null) { if (existing.InvoiceId != invoice.Id) throw new ApplicationConflictException(); await PersistenceUtilities.CommitAsync(transaction, cancellationToken); return MapJob(existing); } }
        var requestedAt = timeProvider.GetUtcNow(); var deadline = requestedAt.AddMinutes(-5);
        if (await dbContext.InvoicePrintJobs.AnyAsync(x => x.InvoiceId == invoice.Id && x.DesktopDeviceId == device.Id && x.Status == InvoicePrintStatus.Requested && x.CreatedAt >= deadline, cancellationToken)) throw new ApplicationConflictException();
        var previousPrints = await dbContext.InvoicePrintLogs.AsNoTracking().Where(x => x.InvoiceId == invoice.Id).ToListAsync(cancellationToken); foreach (var stale in previousPrints.Where(x => x.Status == InvoicePrintStatus.Requested)) { var tracked = await dbContext.InvoicePrintLogs.FindAsync([stale.Id], cancellationToken); tracked?.MarkFailed(requestedAt, "PRINT_ACK_TIMEOUT"); }
        var isReprint = previousPrints.Any(x => x.Status == InvoicePrintStatus.Succeeded); if (isReprint && !command.CanReprint) throw new SecurityAccessDeniedException();
        var job = new InvoicePrintJob(invoice.Id, command.ActorUserId, device.Id, command.Copies, isReprint, isReprint ? command.ReprintReason : null, idempotencyKeyHash); job.AssignResources(command.DevicePrinterId, command.PrintProfileId); dbContext.InvoicePrintJobs.Add(job); dbContext.InvoicePrintLogs.Add(new InvoicePrintLog(invoice.Id, command.ActorUserId, command.Copies, isReprint, isReprint ? command.ReprintReason : null, job.Id, device.Id)); await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken); await PersistenceUtilities.CommitAsync(transaction, cancellationToken); return MapJob(job);
    }

    public async Task<IReadOnlyList<PendingDevicePrintJobInfo>> GetPendingJobsAsync(Guid deviceId, DeviceHeartbeatCommand authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization); var device = await dbContext.DesktopDevices.SingleOrDefaultAsync(x => x.Id == deviceId, cancellationToken) ?? throw new ApplicationResourceNotFoundException(); deviceService.VerifyDeviceSignature(device, $"poll|{device.Id:N}|{authorization.Timestamp:o}", authorization.Signature, authorization.Timestamp); if (!device.IsActive || device.RevokedAt is not null || device.ApprovedAt is null) throw new SecurityAccessDeniedException();
        await using var transaction = await PersistenceUtilities.BeginSerializableTransactionAsync(dbContext, cancellationToken);
        var now = timeProvider.GetUtcNow(); var jobs = await dbContext.InvoicePrintJobs.Where(x => x.DesktopDeviceId == device.Id && x.Status == InvoicePrintStatus.Requested && (x.LeaseExpiresAt == null || x.LeaseExpiresAt <= now || x.ClaimedByDeviceId == device.Id)).OrderBy(x => x.CreatedAt).Take(50).ToListAsync(cancellationToken);
        foreach (var job in jobs) job.Claim(device.Id, Guid.NewGuid(), now, now.AddMinutes(LeaseMinutes));
        await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken); await PersistenceUtilities.CommitAsync(transaction, cancellationToken);
        var infos = new List<PendingDevicePrintJobInfo>(jobs.Count);
        foreach (var job in jobs) { var invoiceNumber = await dbContext.Invoices.AsNoTracking().Where(x => x.Id == job.InvoiceId).Select(x => x.InvoiceNumber).SingleAsync(cancellationToken); string? printerName = job.DevicePrinterId is null ? null : await dbContext.DevicePrinters.AsNoTracking().Where(x => x.Id == job.DevicePrinterId).Select(x => x.SystemPrinterName).SingleOrDefaultAsync(cancellationToken); SystemPrinterSettingsInfo? profile = null; if (job.PrintProfileId is not null) { var p = await dbContext.PrintProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == job.PrintProfileId, cancellationToken); if (p is not null) profile = new SystemPrinterSettingsInfo(p.Id, p.Name, p.PaperSize, p.Orientation, p.Copies, p.ColorMode, p.MarginLeftMillimeters, p.MarginRightMillimeters, p.MarginTopMillimeters, p.MarginBottomMillimeters); } infos.Add(new PendingDevicePrintJobInfo(job.Id, job.InvoiceId, invoiceNumber, job.Copies, job.IsReprint, job.ReprintReason, job.RetryCount, job.DevicePrinterId, printerName, profile, job.CreatedAt, job.ClaimToken, job.LeaseExpiresAt)); }
        return infos;
    }

    public async Task<InvoicePrintJobInfo> CompleteDevicePrintAsync(Guid jobId, CompleteDevicePrintCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command); var job = await dbContext.InvoicePrintJobs.SingleOrDefaultAsync(x => x.Id == jobId, cancellationToken) ?? throw new ApplicationResourceNotFoundException(); var device = await dbContext.DesktopDevices.SingleOrDefaultAsync(x => x.Id == job.DesktopDeviceId, cancellationToken) ?? throw new ApplicationResourceNotFoundException(); deviceService.VerifyDeviceSignature(device, $"complete|{job.Id:N}|{device.Id:N}|{command.ClaimToken:N}|{command.Timestamp:o}|{command.Succeeded}|{command.PrinterName ?? string.Empty}|{command.FailureCode ?? string.Empty}", command.Signature, command.Timestamp); if (!device.IsActive || device.RevokedAt is not null || device.ApprovedAt is null) throw new SecurityAccessDeniedException();
        if (job.Status != InvoicePrintStatus.Requested) return MapJob(job); var now = timeProvider.GetUtcNow(); if (job.ClaimedByDeviceId != device.Id || job.ClaimToken != command.ClaimToken || job.LeaseExpiresAt is null || job.LeaseExpiresAt < now) throw new SecurityAccessDeniedException();
        await using var transaction = await PersistenceUtilities.BeginSerializableTransactionAsync(dbContext, cancellationToken); var completedAt = timeProvider.GetUtcNow(); var log = await dbContext.InvoicePrintLogs.Where(x => x.PrintJobId == job.Id && x.Status == InvoicePrintStatus.Requested).OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (command.Succeeded) { var printerName = command.PrinterName?.Trim(); if (string.IsNullOrWhiteSpace(printerName) || printerName.Length > 300) throw new ArgumentException("A printer name is required for success.", nameof(command)); job.MarkSucceeded(completedAt, printerName, command.Signature); log?.MarkSucceeded(completedAt, printerName); } else { if (!DevicePublicKeyService.IsValidFailureCode(command.FailureCode)) throw new ArgumentException("The failure code is not sanitized.", nameof(command)); job.MarkFailed(completedAt, command.FailureCode!); log?.MarkFailed(completedAt, command.FailureCode!); }
        await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken); await PersistenceUtilities.CommitAsync(transaction, cancellationToken); return MapJob(job);
    }

    public async Task<InvoicePrintJobInfo> RetryDevicePrintAsync(Guid jobId, Guid actorUserId, string rowVersion, CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty) throw new ArgumentException("A valid actor identifier is required.", nameof(actorUserId)); await using var transaction = await PersistenceUtilities.BeginSerializableTransactionAsync(dbContext, cancellationToken); var job = await dbContext.InvoicePrintJobs.FindAsync([jobId], cancellationToken) ?? throw new ApplicationResourceNotFoundException(); PersistenceUtilities.SetOriginalRowVersion(dbContext, job, rowVersion); job.Retry(timeProvider.GetUtcNow()); dbContext.InvoicePrintLogs.Add(new InvoicePrintLog(job.InvoiceId, job.RequestedByUserId, job.Copies, job.IsReprint, job.IsReprint ? job.ReprintReason : null, job.Id, job.DesktopDeviceId)); await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken); await PersistenceUtilities.CommitAsync(transaction, cancellationToken); return MapJob(job);
    }

    public async Task<InvoicePrintDocumentInfo> GetPrintDocumentAsync(Guid jobId, DeviceHeartbeatCommand authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization); var job = await dbContext.InvoicePrintJobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == jobId, cancellationToken) ?? throw new ApplicationResourceNotFoundException(); var device = await dbContext.DesktopDevices.SingleOrDefaultAsync(x => x.Id == job.DesktopDeviceId, cancellationToken) ?? throw new ApplicationResourceNotFoundException(); deviceService.VerifyDeviceSignature(device, $"document|{job.Id:N}|{device.Id:N}|{authorization.Timestamp:o}", authorization.Signature, authorization.Timestamp); if (!device.IsActive || device.RevokedAt is not null || device.ApprovedAt is null) throw new SecurityAccessDeniedException();
        var invoice = await dbContext.Invoices.AsNoTracking().SingleAsync(x => x.Id == job.InvoiceId, cancellationToken); var items = await dbContext.InvoiceItems.AsNoTracking().Where(x => x.InvoiceId == invoice.Id).OrderBy(x => x.LineNumber).ToListAsync(cancellationToken); var address = await dbContext.InvoiceAddressSnapshots.AsNoTracking().SingleOrDefaultAsync(x => x.InvoiceId == invoice.Id, cancellationToken); var store = await dbContext.InvoiceStoreSnapshots.AsNoTracking().SingleOrDefaultAsync(x => x.InvoiceId == invoice.Id, cancellationToken); return new InvoicePrintDocumentInfo(job.Id, invoice.Id, InvoicePrintDocumentBuilder.Build(invoice, items, address, store, job));
    }

    public async Task<PagedResult<InvoicePrintJobInfo>> GetJobsAsync(Guid actorUserId, Guid? deviceId, Guid? invoiceId, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (actorUserId == Guid.Empty) throw new ArgumentException("A valid actor identifier is required.", nameof(actorUserId)); ValidatePage(page, pageSize); var query = dbContext.InvoicePrintJobs.AsNoTracking(); if (deviceId is not null) query = query.Where(x => x.DesktopDeviceId == deviceId); if (invoiceId is not null) query = query.Where(x => x.InvoiceId == invoiceId); var totalCount = await query.CountAsync(cancellationToken); var jobs = await query.OrderByDescending(x => x.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken); return new PagedResult<InvoicePrintJobInfo>(jobs.Select(MapJob).ToArray(), page, pageSize, totalCount);
    }

    private static InvoicePrintJobInfo MapJob(InvoicePrintJob job) => new(job.Id, job.InvoiceId, job.DesktopDeviceId, job.RequestedByUserId, job.Status, job.Copies, job.RetryCount, job.IsReprint, job.ReprintReason, job.PrintedAtPrinterName, job.CompletedAt, job.FailureCode, job.CreatedAt, Convert.ToBase64String(job.RowVersion));
    private static void ValidatePage(int page, int pageSize) { if (page < 1 || pageSize is < 1 or > MaximumPageSize || ((long)page - 1) * pageSize > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(page)); }
}
