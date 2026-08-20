using GoldInvoice.Api.Security;
using GoldInvoice.Application.Platform;
using GoldInvoice.Application.Security;
using GoldInvoice.Contracts.Common;
using GoldInvoice.Contracts.Devices;
using GoldInvoice.Domain.Platform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GoldInvoice.Api.Controllers;

[ApiController]
[Authorize]
[RequestSizeLimit(64 * 1024)]
[Route("api/v1/devices")]
public sealed class DesktopDevicesController(
    IDesktopDeviceService deviceService,
    IInvoicePrintJobService jobService) : ControllerBase
{
    [Authorize(Policy = SecurityPermissions.DesktopDevicesManage)]
    [HttpPost("registration-tokens")]
    public async Task<ActionResult<DeviceRegistrationTokenResponse>> IssueRegistrationToken(
        IssueDeviceRegistrationTokenRequest request,
        CancellationToken cancellationToken)
    {
        var result = await deviceService.IssueRegistrationTokenAsync(
            new IssueDeviceRegistrationTokenCommand(
                User.GetRequiredUserId(),
                request.ExpiresInMinutes),
            cancellationToken);
        return Ok(new DeviceRegistrationTokenResponse
        {
            RawToken = result.RawToken,
            ExpiresAt = result.ExpiresAt
        });
    }

    [AllowAnonymous]
    [HttpPost("enroll")]
    public async Task<ActionResult<DeviceResponse>> Enroll(
        EnrollDeviceRequest request,
        CancellationToken cancellationToken) =>
        Ok(MapDevice(await deviceService.EnrollAsync(
            new EnrollDeviceCommand(
                request.RegistrationToken,
                request.DeviceIdentifierHash,
                request.DisplayName,
                request.PublicKeyPem),
            cancellationToken)));

    [Authorize(Policy = SecurityPermissions.DesktopDevicesManage)]
    [HttpPost("{deviceId:guid}/approve")]
    public async Task<ActionResult<DeviceResponse>> Approve(
        Guid deviceId,
        ApproveDeviceRequest request,
        CancellationToken cancellationToken) =>
        Ok(MapDevice(await deviceService.ApproveAsync(
            deviceId,
            new ApproveDeviceCommand(User.GetRequiredUserId(), request.RowVersion),
            cancellationToken)));

    [Authorize(Policy = SecurityPermissions.DesktopDevicesManage)]
    [HttpPost("{deviceId:guid}/revoke")]
    public async Task<ActionResult<DeviceResponse>> Revoke(
        Guid deviceId,
        ApproveDeviceRequest request,
        CancellationToken cancellationToken) =>
        Ok(MapDevice(await deviceService.RevokeAsync(
            deviceId,
            new RevokeDeviceCommand(User.GetRequiredUserId(), request.RowVersion),
            cancellationToken)));

    [AllowAnonymous]
    [HttpPost("{deviceId:guid}/heartbeat")]
    public async Task<IActionResult> Heartbeat(
        Guid deviceId,
        DeviceHeartbeatRequest request,
        CancellationToken cancellationToken)
    {
        await deviceService.HeartbeatAsync(
            deviceId,
            new DeviceHeartbeatCommand(request.Timestamp, request.Signature),
            cancellationToken);
        return NoContent();
    }

    [AllowAnonymous]
    [HttpGet("{deviceId:guid}/print-jobs/pending")]
    public async Task<ActionResult<PendingDevicePrintJobResponse[]>> GetPendingJobs(
        Guid deviceId,
        [FromQuery] DateTimeOffset timestamp,
        [FromQuery] string signature,
        CancellationToken cancellationToken)
    {
        var jobs = await jobService.GetPendingJobsAsync(
            deviceId,
            new DeviceHeartbeatCommand(timestamp, signature),
            cancellationToken);
        return Ok(jobs.Select(MapPending).ToArray());
    }

    [AllowAnonymous]
    [HttpGet("{deviceId:guid}/print-jobs/{jobId:guid}/document")]
    public async Task<ActionResult<PrintDocumentResponse>> GetPrintDocument(
        Guid deviceId,
        Guid jobId,
        [FromQuery] DateTimeOffset timestamp,
        [FromQuery] string signature,
        CancellationToken cancellationToken)
    {
        var document = await jobService.GetPrintDocumentAsync(
            jobId,
            new DeviceHeartbeatCommand(timestamp, signature),
            cancellationToken);
        return Ok(new PrintDocumentResponse
        {
            JobId = document.JobId,
            InvoiceId = document.InvoiceId,
            Html = document.Html
        });
    }

    [AllowAnonymous]
    [HttpPost("print-jobs/{jobId:guid}/complete")]
    public async Task<ActionResult<InvoicePrintJobResponse>> CompletePrintJob(
        Guid jobId,
        CompleteDevicePrintRequest request,
        CancellationToken cancellationToken) =>
        Ok(MapJob(await jobService.CompleteDevicePrintAsync(
            jobId,
            new CompleteDevicePrintCommand(
                request.Timestamp,
                request.Succeeded,
                request.PrinterName,
                request.FailureCode,
                request.Signature),
            cancellationToken)));

    [Authorize(Policy = SecurityPermissions.DevicePrintersManage)]
    [HttpPost("print-jobs/{jobId:guid}/retry")]
    public async Task<ActionResult<InvoicePrintJobResponse>> RetryPrintJob(
        Guid jobId,
        ApproveDeviceRequest request,
        CancellationToken cancellationToken) =>
        Ok(MapJob(await jobService.RetryDevicePrintAsync(
            jobId,
            User.GetRequiredUserId(),
            request.RowVersion,
            cancellationToken)));

    [Authorize(Policy = SecurityPermissions.DesktopDevicesView)]
    [HttpGet]
    public async Task<ActionResult<PagedResponse<DeviceResponse>>> GetDevices(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await deviceService.GetDevicesAsync(
            User.GetRequiredUserId(),
            page,
            pageSize,
            cancellationToken);
        return Ok(new PagedResponse<DeviceResponse>
        {
            Items = result.Items.Select(MapDevice).ToArray(),
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount
        });
    }

    [Authorize(Policy = SecurityPermissions.DesktopDevicesView)]
    [HttpGet("{deviceId:guid}")]
    public async Task<ActionResult<DeviceResponse>> GetDevice(
        Guid deviceId,
        CancellationToken cancellationToken) =>
        Ok(MapDevice(await deviceService.GetDeviceAsync(
            deviceId,
            User.GetRequiredUserId(),
            cancellationToken)));

    [Authorize(Policy = SecurityPermissions.DevicePrintersManage)]
    [HttpPost("{deviceId:guid}/printers")]
    public async Task<ActionResult<DevicePrinterResponse>> RegisterPrinter(
        Guid deviceId,
        RegisterDevicePrinterRequest request,
        CancellationToken cancellationToken)
    {
        var printer = await deviceService.RegisterPrinterAsync(
            new RegisterDevicePrinterCommand(
                User.GetRequiredUserId(),
                deviceId,
                request.SystemPrinterName,
                request.DisplayName,
                ParseEnum<PrinterType>(request.PrinterType)),
            cancellationToken);
        return CreatedAtAction(nameof(GetDevice), new { deviceId }, MapPrinter(printer));
    }

    [Authorize(Policy = SecurityPermissions.DevicePrintersManage)]
    [HttpPut("{deviceId:guid}/printers/{printerId:guid}/default")]
    public async Task<ActionResult<DevicePrinterResponse>> SetPrinterDefault(
        Guid deviceId,
        Guid printerId,
        SetDevicePrinterDefaultRequest request,
        CancellationToken cancellationToken) =>
        Ok(MapPrinter(await deviceService.SetPrinterDefaultAsync(
            new SetDevicePrinterDefaultCommand(
                User.GetRequiredUserId(),
                deviceId,
                printerId,
                request.IsDefault,
                request.RowVersion),
            cancellationToken)));

    [Authorize(Policy = SecurityPermissions.DevicePrintersManage)]
    [HttpPut("{deviceId:guid}/printers/{printerId:guid}/enabled")]
    public async Task<ActionResult<DevicePrinterResponse>> SetPrinterEnabled(
        Guid deviceId,
        Guid printerId,
        SetDevicePrinterEnabledRequest request,
        CancellationToken cancellationToken) =>
        Ok(MapPrinter(await deviceService.SetPrinterEnabledAsync(
            new SetDevicePrinterEnabledCommand(
                User.GetRequiredUserId(),
                deviceId,
                printerId,
                request.IsEnabled,
                request.RowVersion),
            cancellationToken)));

    [Authorize(Policy = SecurityPermissions.DevicePrintProfilesManage)]
    [HttpPost("{deviceId:guid}/print-profiles")]
    public async Task<ActionResult<PrintProfileResponse>> CreatePrintProfile(
        Guid deviceId,
        CreateDevicePrintProfileRequest request,
        CancellationToken cancellationToken)
    {
        var profile = await deviceService.CreatePrintProfileAsync(
            new CreateDevicePrintProfileCommand(
                User.GetRequiredUserId(),
                deviceId,
                request.Name,
                ParseEnum<PaperSize>(request.PaperSize),
                ParseEnum<PrintOrientation>(request.Orientation),
                request.Copies,
                ParseEnum<ColorMode>(request.ColorMode),
                request.MarginLeftMillimeters,
                request.MarginRightMillimeters,
                request.MarginTopMillimeters,
                request.MarginBottomMillimeters),
            cancellationToken);
        return CreatedAtAction(nameof(GetDevice), new { deviceId }, MapProfile(profile));
    }

    [Authorize(Policy = SecurityPermissions.DevicePrintProfilesManage)]
    [HttpPut("{deviceId:guid}/print-profiles/{profileId:guid}/default")]
    public async Task<ActionResult<PrintProfileResponse>> SetPrintProfileDefault(
        Guid deviceId,
        Guid profileId,
        SetDevicePrintProfileDefaultRequest request,
        CancellationToken cancellationToken) =>
        Ok(MapProfile(await deviceService.SetPrintProfileDefaultAsync(
            new SetDevicePrintProfileDefaultCommand(
                User.GetRequiredUserId(),
                deviceId,
                profileId,
                request.IsDefault,
                request.RowVersion),
            cancellationToken)));

    [Authorize(Policy = SecurityPermissions.DevicePrintProfilesManage)]
    [HttpPut("{deviceId:guid}/print-profiles/{profileId:guid}/enabled")]
    public async Task<ActionResult<PrintProfileResponse>> SetPrintProfileEnabled(
        Guid deviceId,
        Guid profileId,
        SetDevicePrintProfileEnabledRequest request,
        CancellationToken cancellationToken) =>
        Ok(MapProfile(await deviceService.SetPrintProfileEnabledAsync(
            new SetDevicePrintProfileEnabledCommand(
                User.GetRequiredUserId(),
                deviceId,
                profileId,
                request.IsEnabled,
                request.RowVersion),
            cancellationToken)));

    [Authorize(Policy = SecurityPermissions.InvoicesPrint)]
    [HttpPost("~/api/v1/invoices/{invoiceId:guid}/device-print-jobs")]
    public async Task<ActionResult<InvoicePrintJobResponse>> RequestDevicePrint(
        Guid invoiceId,
        RequestDevicePrintRequest request,
        CancellationToken cancellationToken)
    {
        var job = await jobService.RequestDevicePrintAsync(
            invoiceId,
            new RequestDevicePrintCommand(
                User.GetRequiredUserId(),
                request.DesktopDeviceId,
                request.DevicePrinterId,
                request.PrintProfileId,
                request.Copies,
                User.HasPermission(SecurityPermissions.InvoicesReprint),
                request.ReprintReason,
                request.IdempotencyKey),
            cancellationToken);
        return CreatedAtAction(
            nameof(RequestDevicePrint),
            new { invoiceId },
            MapJob(job));
    }

    [Authorize(Policy = SecurityPermissions.InvoicePrintJobsView)]
    [HttpGet("~/api/v1/device-print-jobs")]
    public async Task<ActionResult<PagedResponse<InvoicePrintJobResponse>>> GetPrintJobs(
        [FromQuery] Guid? deviceId = null,
        [FromQuery] Guid? invoiceId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await jobService.GetJobsAsync(
            User.GetRequiredUserId(),
            deviceId,
            invoiceId,
            page,
            pageSize,
            cancellationToken);
        return Ok(new PagedResponse<InvoicePrintJobResponse>
        {
            Items = result.Items.Select(MapJob).ToArray(),
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount
        });
    }

    private static TEnum ParseEnum<TEnum>(string value)
        where TEnum : struct, Enum
    {
        if (!Enum.TryParse<TEnum>(value, ignoreCase: true, out var result))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "The enum value is not supported.");
        }

        return result;
    }

    private static DeviceResponse MapDevice(DeviceInfo device) => new()
    {
        Id = device.Id,
        DisplayName = device.DisplayName,
        IsActive = device.IsActive,
        ApprovedAt = device.ApprovedAt,
        LastSeenAt = device.LastSeenAt,
        RevokedAt = device.RevokedAt,
        CreatedAt = device.CreatedAt,
        RowVersion = device.RowVersion,
        Printers = device.Printers.Select(MapPrinter).ToArray(),
        Profiles = device.Profiles.Select(MapProfile).ToArray()
    };

    private static DevicePrinterResponse MapPrinter(DevicePrinterInfo printer) => new()
    {
        Id = printer.Id,
        SystemPrinterName = printer.SystemPrinterName,
        DisplayName = printer.DisplayName,
        PrinterType = printer.PrinterType.ToString(),
        IsDefault = printer.IsDefault,
        IsEnabled = printer.IsEnabled,
        LastSeenAt = printer.LastSeenAt,
        CreatedAt = printer.CreatedAt,
        RowVersion = printer.RowVersion
    };

    private static PrintProfileResponse MapProfile(PrintProfileInfo profile) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        PaperSize = profile.PaperSize.ToString(),
        Orientation = profile.Orientation.ToString(),
        Copies = profile.Copies,
        ColorMode = profile.ColorMode.ToString(),
        MarginLeftMillimeters = profile.MarginLeftMillimeters,
        MarginRightMillimeters = profile.MarginRightMillimeters,
        MarginTopMillimeters = profile.MarginTopMillimeters,
        MarginBottomMillimeters = profile.MarginBottomMillimeters,
        IsDefault = profile.IsDefault,
        IsEnabled = profile.IsEnabled,
        CreatedAt = profile.CreatedAt,
        RowVersion = profile.RowVersion
    };

    private static PendingDevicePrintJobResponse MapPending(PendingDevicePrintJobInfo job) => new()
    {
        JobId = job.JobId,
        InvoiceId = job.InvoiceId,
        InvoiceNumber = job.InvoiceNumber,
        Copies = job.Copies,
        IsReprint = job.IsReprint,
        ReprintReason = job.ReprintReason,
        RetryCount = job.RetryCount,
        DevicePrinterId = job.DevicePrinterId,
        PrinterName = job.PrinterName,
        Profile = job.Profile is null ? null : new SystemPrinterSettingsResponse
        {
            ProfileId = job.Profile.ProfileId,
            ProfileName = job.Profile.ProfileName,
            PaperSize = job.Profile.PaperSize.ToString(),
            Orientation = job.Profile.Orientation.ToString(),
            Copies = job.Profile.Copies,
            ColorMode = job.Profile.ColorMode.ToString(),
            MarginLeftMillimeters = job.Profile.MarginLeftMillimeters,
            MarginRightMillimeters = job.Profile.MarginRightMillimeters,
            MarginTopMillimeters = job.Profile.MarginTopMillimeters,
            MarginBottomMillimeters = job.Profile.MarginBottomMillimeters
        },
        CreatedAt = job.CreatedAt
    };

    private static InvoicePrintJobResponse MapJob(InvoicePrintJobInfo job) => new()
    {
        Id = job.Id,
        InvoiceId = job.InvoiceId,
        DesktopDeviceId = job.DesktopDeviceId,
        RequestedByUserId = job.RequestedByUserId,
        Status = job.Status.ToString(),
        Copies = job.Copies,
        RetryCount = job.RetryCount,
        IsReprint = job.IsReprint,
        ReprintReason = job.ReprintReason,
        PrinterName = job.PrinterName,
        CompletedAt = job.CompletedAt,
        FailureCode = job.FailureCode,
        CreatedAt = job.CreatedAt,
        RowVersion = job.RowVersion
    };
}
