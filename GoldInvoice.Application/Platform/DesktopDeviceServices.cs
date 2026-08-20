using GoldInvoice.Application.Common;
using GoldInvoice.Domain.Invoicing;
using GoldInvoice.Domain.Platform;

namespace GoldInvoice.Application.Platform;

public sealed record DeviceRegistrationTokenInfo(
    string RawToken,
    DateTimeOffset ExpiresAt);

public sealed record DeviceInfo(
    Guid Id,
    string DisplayName,
    bool IsActive,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset CreatedAt,
    string RowVersion,
    IReadOnlyList<DevicePrinterInfo> Printers,
    IReadOnlyList<PrintProfileInfo> Profiles);

public sealed record DevicePrinterInfo(
    Guid Id,
    string SystemPrinterName,
    string DisplayName,
    PrinterType PrinterType,
    bool IsDefault,
    bool IsEnabled,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset CreatedAt,
    string RowVersion);

public sealed record PrintProfileInfo(
    Guid Id,
    string Name,
    PaperSize PaperSize,
    PrintOrientation Orientation,
    int Copies,
    ColorMode ColorMode,
    int MarginLeftMillimeters,
    int MarginRightMillimeters,
    int MarginTopMillimeters,
    int MarginBottomMillimeters,
    bool IsDefault,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    string RowVersion);

public sealed record IssueDeviceRegistrationTokenCommand(
    Guid ActorUserId,
    int ExpiresInMinutes);

public sealed record EnrollDeviceCommand(
    string RegistrationToken,
    string DeviceIdentifierHash,
    string DisplayName,
    string PublicKeyPem);

public sealed record ApproveDeviceCommand(
    Guid ActorUserId,
    string RowVersion);

public sealed record RevokeDeviceCommand(
    Guid ActorUserId,
    string RowVersion);

public sealed record DeviceHeartbeatCommand(
    DateTimeOffset Timestamp,
    string Signature);

public sealed record RegisterDevicePrinterCommand(
    Guid ActorUserId,
    Guid DeviceId,
    string SystemPrinterName,
    string DisplayName,
    PrinterType PrinterType);

public sealed record SetDevicePrinterDefaultCommand(
    Guid ActorUserId,
    Guid DeviceId,
    Guid PrinterId,
    bool IsDefault,
    string RowVersion);

public sealed record SetDevicePrinterEnabledCommand(
    Guid ActorUserId,
    Guid DeviceId,
    Guid PrinterId,
    bool IsEnabled,
    string RowVersion);

public sealed record CreateDevicePrintProfileCommand(
    Guid ActorUserId,
    Guid DeviceId,
    string Name,
    PaperSize PaperSize,
    PrintOrientation Orientation,
    int Copies,
    ColorMode ColorMode,
    int MarginLeftMillimeters,
    int MarginRightMillimeters,
    int MarginTopMillimeters,
    int MarginBottomMillimeters);

public sealed record SetDevicePrintProfileDefaultCommand(
    Guid ActorUserId,
    Guid DeviceId,
    Guid ProfileId,
    bool IsDefault,
    string RowVersion);

public sealed record SetDevicePrintProfileEnabledCommand(
    Guid ActorUserId,
    Guid DeviceId,
    Guid ProfileId,
    bool IsEnabled,
    string RowVersion);

public interface IDesktopDeviceService
{
    Task<DeviceRegistrationTokenInfo> IssueRegistrationTokenAsync(
        IssueDeviceRegistrationTokenCommand command,
        CancellationToken cancellationToken);

    Task<DeviceInfo> EnrollAsync(
        EnrollDeviceCommand command,
        CancellationToken cancellationToken);

    Task<DeviceInfo> ApproveAsync(
        Guid deviceId,
        ApproveDeviceCommand command,
        CancellationToken cancellationToken);

    Task<DeviceInfo> RevokeAsync(
        Guid deviceId,
        RevokeDeviceCommand command,
        CancellationToken cancellationToken);

    Task HeartbeatAsync(
        Guid deviceId,
        DeviceHeartbeatCommand command,
        CancellationToken cancellationToken);

    Task<PagedResult<DeviceInfo>> GetDevicesAsync(
        Guid actorUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<DeviceInfo> GetDeviceAsync(
        Guid deviceId,
        Guid actorUserId,
        CancellationToken cancellationToken);

    Task<DevicePrinterInfo> RegisterPrinterAsync(
        RegisterDevicePrinterCommand command,
        CancellationToken cancellationToken);

    Task<DevicePrinterInfo> SetPrinterDefaultAsync(
        SetDevicePrinterDefaultCommand command,
        CancellationToken cancellationToken);

    Task<DevicePrinterInfo> SetPrinterEnabledAsync(
        SetDevicePrinterEnabledCommand command,
        CancellationToken cancellationToken);

    Task<PrintProfileInfo> CreatePrintProfileAsync(
        CreateDevicePrintProfileCommand command,
        CancellationToken cancellationToken);

    Task<PrintProfileInfo> SetPrintProfileDefaultAsync(
        SetDevicePrintProfileDefaultCommand command,
        CancellationToken cancellationToken);

    Task<PrintProfileInfo> SetPrintProfileEnabledAsync(
        SetDevicePrintProfileEnabledCommand command,
        CancellationToken cancellationToken);
}

public sealed record RequestDevicePrintCommand(
    Guid ActorUserId,
    Guid DesktopDeviceId,
    Guid? DevicePrinterId,
    Guid? PrintProfileId,
    int Copies,
    bool CanReprint,
    string? ReprintReason,
    string? IdempotencyKey);

public sealed record PendingDevicePrintJobInfo(
    Guid JobId,
    Guid InvoiceId,
    string InvoiceNumber,
    int Copies,
    bool IsReprint,
    string? ReprintReason,
    int RetryCount,
    Guid? DevicePrinterId,
    string? PrinterName,
    SystemPrinterSettingsInfo? Profile,
    DateTimeOffset CreatedAt);

public sealed record SystemPrinterSettingsInfo(
    Guid? ProfileId,
    string? ProfileName,
    PaperSize PaperSize,
    PrintOrientation Orientation,
    int Copies,
    ColorMode ColorMode,
    int MarginLeftMillimeters,
    int MarginRightMillimeters,
    int MarginTopMillimeters,
    int MarginBottomMillimeters);

public sealed record CompleteDevicePrintCommand(
    DateTimeOffset Timestamp,
    bool Succeeded,
    string? PrinterName,
    string? FailureCode,
    string Signature);

public sealed record InvoicePrintJobInfo(
    Guid Id,
    Guid InvoiceId,
    Guid DesktopDeviceId,
    Guid RequestedByUserId,
    InvoicePrintStatus Status,
    int Copies,
    int RetryCount,
    bool IsReprint,
    string? ReprintReason,
    string? PrinterName,
    DateTimeOffset? CompletedAt,
    string? FailureCode,
    DateTimeOffset CreatedAt,
    string RowVersion);

public sealed record InvoicePrintDocumentInfo(
    Guid JobId,
    Guid InvoiceId,
    string Html);

public interface IInvoicePrintJobService
{
    Task<InvoicePrintJobInfo> RequestDevicePrintAsync(
        Guid invoiceId,
        RequestDevicePrintCommand command,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PendingDevicePrintJobInfo>> GetPendingJobsAsync(
        Guid deviceId,
        DeviceHeartbeatCommand authorization,
        CancellationToken cancellationToken);

    Task<InvoicePrintJobInfo> CompleteDevicePrintAsync(
        Guid jobId,
        CompleteDevicePrintCommand command,
        CancellationToken cancellationToken);

    Task<InvoicePrintJobInfo> RetryDevicePrintAsync(
        Guid jobId,
        Guid actorUserId,
        string rowVersion,
        CancellationToken cancellationToken);

    Task<InvoicePrintDocumentInfo> GetPrintDocumentAsync(
        Guid jobId,
        DeviceHeartbeatCommand authorization,
        CancellationToken cancellationToken);

    Task<PagedResult<InvoicePrintJobInfo>> GetJobsAsync(
        Guid actorUserId,
        Guid? deviceId,
        Guid? invoiceId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}