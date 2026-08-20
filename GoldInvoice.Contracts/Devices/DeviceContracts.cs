using System.ComponentModel.DataAnnotations;

namespace GoldInvoice.Contracts.Devices;

public sealed class IssueDeviceRegistrationTokenRequest
{
    [Range(1, 1440)]
    public int ExpiresInMinutes { get; init; } = 60;
}

public sealed class DeviceRegistrationTokenResponse
{
    [Required]
    public string RawToken { get; init; } = string.Empty;

    public required DateTimeOffset ExpiresAt { get; init; }
}

public sealed class EnrollDeviceRequest
{
    [Required, StringLength(128)]
    public string RegistrationToken { get; init; } = string.Empty;

    [Required, StringLength(128)]
    public string DeviceIdentifierHash { get; init; } = string.Empty;

    [Required, StringLength(200)]
    public string DisplayName { get; init; } = string.Empty;

    [Required, StringLength(4000)]
    public string PublicKeyPem { get; init; } = string.Empty;
}

public sealed class DeviceHeartbeatRequest
{
    public required DateTimeOffset Timestamp { get; init; }

    [Required, StringLength(1024)]
    public string Signature { get; init; } = string.Empty;
}

public sealed class ApproveDeviceRequest
{
    [Required, StringLength(256)]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class RegisterDevicePrinterRequest
{
    [Required, StringLength(300)]
    public string SystemPrinterName { get; init; } = string.Empty;

    [Required, StringLength(200)]
    public string DisplayName { get; init; } = string.Empty;

    [Required]
    public string PrinterType { get; init; } = string.Empty;
}

public sealed class SetDevicePrinterDefaultRequest
{
    public required bool IsDefault { get; init; }

    [Required, StringLength(256)]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class SetDevicePrinterEnabledRequest
{
    public required bool IsEnabled { get; init; }

    [Required, StringLength(256)]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class CreateDevicePrintProfileRequest
{
    [Required, StringLength(200)]
    public string Name { get; init; } = string.Empty;

    [Required]
    public string PaperSize { get; init; } = string.Empty;

    [Required]
    public string Orientation { get; init; } = string.Empty;

    [Range(1, 20)]
    public int Copies { get; init; } = 1;

    [Required]
    public string ColorMode { get; init; } = string.Empty;

    [Range(0, 1000)]
    public int MarginLeftMillimeters { get; init; }

    [Range(0, 1000)]
    public int MarginRightMillimeters { get; init; }

    [Range(0, 1000)]
    public int MarginTopMillimeters { get; init; }

    [Range(0, 1000)]
    public int MarginBottomMillimeters { get; init; }
}

public sealed class SetDevicePrintProfileDefaultRequest
{
    public required bool IsDefault { get; init; }

    [Required, StringLength(256)]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class SetDevicePrintProfileEnabledRequest
{
    public required bool IsEnabled { get; init; }

    [Required, StringLength(256)]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class DevicePrinterResponse
{
    public required Guid Id { get; init; }
    [Required]
    public string SystemPrinterName { get; init; } = string.Empty;
    [Required]
    public string DisplayName { get; init; } = string.Empty;
    [Required]
    public string PrinterType { get; init; } = string.Empty;
    public required bool IsDefault { get; init; }
    public required bool IsEnabled { get; init; }
    public DateTimeOffset? LastSeenAt { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    [Required]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class PrintProfileResponse
{
    public required Guid Id { get; init; }
    [Required]
    public string Name { get; init; } = string.Empty;
    [Required]
    public string PaperSize { get; init; } = string.Empty;
    [Required]
    public string Orientation { get; init; } = string.Empty;
    public required int Copies { get; init; }
    [Required]
    public string ColorMode { get; init; } = string.Empty;
    public required int MarginLeftMillimeters { get; init; }
    public required int MarginRightMillimeters { get; init; }
    public required int MarginTopMillimeters { get; init; }
    public required int MarginBottomMillimeters { get; init; }
    public required bool IsDefault { get; init; }
    public required bool IsEnabled { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    [Required]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class DeviceResponse
{
    public required Guid Id { get; init; }
    [Required]
    public string DisplayName { get; init; } = string.Empty;
    public required bool IsActive { get; init; }
    public DateTimeOffset? ApprovedAt { get; init; }
    public DateTimeOffset? LastSeenAt { get; init; }
    public DateTimeOffset? RevokedAt { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    [Required]
    public string RowVersion { get; init; } = string.Empty;
    public DevicePrinterResponse[] Printers { get; init; } = [];
    public PrintProfileResponse[] Profiles { get; init; } = [];
}

public sealed class RequestDevicePrintRequest
{
    public required Guid DesktopDeviceId { get; init; }

    public Guid? DevicePrinterId { get; init; }

    public Guid? PrintProfileId { get; init; }

    [Range(1, 20)]
    public int Copies { get; init; } = 1;

    [StringLength(1000)]
    public string? ReprintReason { get; init; }

    [StringLength(128)]
    public string? IdempotencyKey { get; init; }
}

public sealed class PendingDevicePrintJobResponse
{
    public required Guid JobId { get; init; }
    public required Guid InvoiceId { get; init; }
    [Required]
    public string InvoiceNumber { get; init; } = string.Empty;
    public required int Copies { get; init; }
    public required bool IsReprint { get; init; }
    public string? ReprintReason { get; init; }
    public required int RetryCount { get; init; }
    public Guid? DevicePrinterId { get; init; }
    public string? PrinterName { get; init; }
    public SystemPrinterSettingsResponse? Profile { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed class SystemPrinterSettingsResponse
{
    public Guid? ProfileId { get; init; }
    public string? ProfileName { get; init; }
    [Required]
    public string PaperSize { get; init; } = string.Empty;
    [Required]
    public string Orientation { get; init; } = string.Empty;
    public required int Copies { get; init; }
    [Required]
    public string ColorMode { get; init; } = string.Empty;
    public required int MarginLeftMillimeters { get; init; }
    public required int MarginRightMillimeters { get; init; }
    public required int MarginTopMillimeters { get; init; }
    public required int MarginBottomMillimeters { get; init; }
}

public sealed class CompleteDevicePrintRequest
{
    public required DateTimeOffset Timestamp { get; init; }

    public required bool Succeeded { get; init; }

    [StringLength(300)]
    public string? PrinterName { get; init; }

    [StringLength(100)]
    public string? FailureCode { get; init; }

    [Required, StringLength(1024)]
    public string Signature { get; init; } = string.Empty;
}

public sealed class InvoicePrintJobResponse
{
    public required Guid Id { get; init; }
    public required Guid InvoiceId { get; init; }
    public required Guid DesktopDeviceId { get; init; }
    public required Guid RequestedByUserId { get; init; }
    [Required]
    public string Status { get; init; } = string.Empty;
    public required int Copies { get; init; }
    public required int RetryCount { get; init; }
    public required bool IsReprint { get; init; }
    public string? ReprintReason { get; init; }
    public string? PrinterName { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? FailureCode { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    [Required]
    public string RowVersion { get; init; } = string.Empty;
}

public sealed class PrintDocumentResponse
{
    public required Guid JobId { get; init; }

    public required Guid InvoiceId { get; init; }

    [Required]
    public string Html { get; init; } = string.Empty;
}