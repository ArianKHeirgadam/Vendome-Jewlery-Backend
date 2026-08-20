using GoldInvoice.Domain.Common;

namespace GoldInvoice.Domain.Platform;

public enum PrinterType
{
    Receipt,
    A4,
    Label,
    Thermal,
    DotMatrix
}

public enum PaperSize
{
    A4,
    A5,
    Receipt80,
    Receipt58,
    Label,
    Custom
}

public enum PrintOrientation
{
    Portrait,
    Landscape
}

public enum ColorMode
{
    Monochrome,
    Color
}

public sealed class DeviceRegistrationToken : AuditableEntity, IProtectedFromHardDelete
{
    private DeviceRegistrationToken()
    {
    }

    public DeviceRegistrationToken(Guid createdById, string tokenValueHash, DateTimeOffset expiresAt)
    {
        Guard.AgainstEmpty(createdById, nameof(createdById));
        CreatedById = createdById;
        TokenValueHash = Guard.Required(tokenValueHash, nameof(tokenValueHash), 128).ToUpperInvariant();
        Guard.AgainstDefault(expiresAt, nameof(expiresAt));
        if (expiresAt <= DateTimeOffset.MinValue)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        }

        ExpiresAt = expiresAt;
    }

    public Guid CreatedById { get; private set; }

    public string TokenValueHash { get; private set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public bool IsUsableAt(DateTimeOffset now) => UsedAt is null && ExpiresAt > now;

    public void MarkUsed(DateTimeOffset usedAt)
    {
        Guard.AgainstDefault(usedAt, nameof(usedAt));
        if (UsedAt is not null || ExpiresAt <= usedAt)
        {
            throw new DomainConflictException("The registration token is no longer usable.");
        }

        UsedAt = usedAt;
    }
}

public sealed class DevicePrinter : AuditableEntity, IProtectedFromHardDelete
{
    private DevicePrinter()
    {
    }

    public DevicePrinter(
        Guid desktopDeviceId,
        string systemPrinterName,
        string displayName,
        PrinterType printerType)
    {
        Guard.AgainstEmpty(desktopDeviceId, nameof(desktopDeviceId));
        DesktopDeviceId = desktopDeviceId;
        SystemPrinterName = Guard.Required(systemPrinterName, nameof(systemPrinterName), 300);
        DisplayName = Guard.Required(displayName, nameof(displayName), 200);
        PrinterType = printerType;
    }

    public Guid DesktopDeviceId { get; private set; }

    public string SystemPrinterName { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public PrinterType PrinterType { get; private set; }

    public bool IsDefault { get; private set; }

    public bool IsEnabled { get; private set; } = true;

    public DateTimeOffset? LastSeenAt { get; private set; }

    public void SetDefault(bool isDefault) => IsDefault = isDefault;

    public void UpdateDetails(string displayName, PrinterType printerType)
    {
        DisplayName = Guard.Required(displayName, nameof(displayName), 200);
        PrinterType = printerType;
    }

    public void SetEnabled(bool isEnabled) => IsEnabled = isEnabled;

    public void MarkSeen(DateTimeOffset seenAt)
    {
        Guard.AgainstDefault(seenAt, nameof(seenAt));
        LastSeenAt = seenAt;
    }
}

public sealed class PrintProfile : AuditableEntity, IProtectedFromHardDelete
{
    private PrintProfile()
    {
    }

    public PrintProfile(
        Guid desktopDeviceId,
        string name,
        PaperSize paperSize,
        PrintOrientation orientation,
        int copies,
        ColorMode colorMode,
        int marginLeftMillimeters,
        int marginRightMillimeters,
        int marginTopMillimeters,
        int marginBottomMillimeters)
    {
        Guard.AgainstEmpty(desktopDeviceId, nameof(desktopDeviceId));
        DesktopDeviceId = desktopDeviceId;
        Name = Guard.Required(name, nameof(name), 200);
        PaperSize = paperSize;
        Orientation = orientation;
        if (copies is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(copies), "The copy count must be between 1 and 20.");
        }

        Copies = copies;
        ColorMode = colorMode;
        marginLeftMillimeters = GuardNonNegativeMargin(marginLeftMillimeters, nameof(marginLeftMillimeters));
        marginRightMillimeters = GuardNonNegativeMargin(marginRightMillimeters, nameof(marginRightMillimeters));
        marginTopMillimeters = GuardNonNegativeMargin(marginTopMillimeters, nameof(marginTopMillimeters));
        marginBottomMillimeters = GuardNonNegativeMargin(marginBottomMillimeters, nameof(marginBottomMillimeters));
        MarginLeftMillimeters = marginLeftMillimeters;
        MarginRightMillimeters = marginRightMillimeters;
        MarginTopMillimeters = marginTopMillimeters;
        MarginBottomMillimeters = marginBottomMillimeters;
    }

    public Guid DesktopDeviceId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public PaperSize PaperSize { get; private set; }

    public PrintOrientation Orientation { get; private set; }

    public int Copies { get; private set; }

    public ColorMode ColorMode { get; private set; }

    public int MarginLeftMillimeters { get; private set; }

    public int MarginRightMillimeters { get; private set; }

    public int MarginTopMillimeters { get; private set; }

    public int MarginBottomMillimeters { get; private set; }

    public bool IsDefault { get; private set; }

    public bool IsEnabled { get; private set; } = true;

    public void SetDefault(bool isDefault) => IsDefault = isDefault;

    public void SetEnabled(bool isEnabled) => IsEnabled = isEnabled;

    private static int GuardNonNegativeMargin(int value, string parameterName)
    {
        if (value is < 0 or > 1000)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The margin must be between 0 and 1000 millimeters.");
        }

        return value;
    }
}