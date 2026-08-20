using System.Security.Cryptography;
using System.Text;
using GoldInvoice.Application.Common;
using GoldInvoice.Application.Platform;
using GoldInvoice.Application.Security;
using GoldInvoice.Domain.Common;
using GoldInvoice.Domain.Invoicing;
using GoldInvoice.Domain.Orders;
using GoldInvoice.Domain.Payments;
using GoldInvoice.Domain.Platform;
using GoldInvoice.Infrastructure.Devices;
using GoldInvoice.Infrastructure.Identity;
using GoldInvoice.Infrastructure.Persistence;
using GoldInvoice.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace GoldInvoice.IntegrationTests;

public sealed class PhaseSevenCBDevicePrintingTests
{
    private static readonly DateTimeOffset FixedNow =
        DateTimeOffset.Parse("2026-08-15T10:00:00+00:00");

    [Fact]
    public async Task DeviceEnrollment_TokenApprovalHeartbeatRevocation_Lifecycle()
    {
        await using var scenario = await CreateScenarioAsync();
        var token = await scenario.DeviceService.IssueRegistrationTokenAsync(
            new IssueDeviceRegistrationTokenCommand(scenario.Manager.Id, 60),
            CancellationToken.None);
        Assert.Equal(TimeSpan.FromMinutes(60), token.ExpiresAt - FixedNow);

        var pending = await scenario.DeviceService.EnrollAsync(
            new EnrollDeviceCommand(
                token.RawToken,
                "dev-identifier-hash-1",
                "Cashier station 1",
                scenario.DeviceRsa.ExportSubjectPublicKeyInfoPem()),
            CancellationToken.None);
        Assert.False(pending.IsActive);
        Assert.Null(pending.ApprovedAt);
        Assert.Null(pending.LastSeenAt);
        Assert.Empty(pending.Printers);
        Assert.Empty(pending.Profiles);

        var approved = await scenario.DeviceService.ApproveAsync(
            pending.Id,
            new ApproveDeviceCommand(scenario.Manager.Id, pending.RowVersion),
            CancellationToken.None);
        Assert.True(approved.IsActive);
        Assert.Equal(FixedNow, approved.ApprovedAt);

        var agentTs = FixedNow.AddSeconds(1);
        await scenario.DeviceService.HeartbeatAsync(
            approved.Id,
            new DeviceHeartbeatCommand(
                agentTs,
                scenario.Sign("heartbeat", approved.Id, agentTs)),
            CancellationToken.None);
        var seen = await scenario.DeviceService.GetDeviceAsync(approved.Id, scenario.Manager.Id, CancellationToken.None);
        Assert.Equal(FixedNow, seen.LastSeenAt);

        var revoked = await scenario.DeviceService.RevokeAsync(
            approved.Id,
            new RevokeDeviceCommand(scenario.Manager.Id, approved.RowVersion),
            CancellationToken.None);
        Assert.False(revoked.IsActive);
        Assert.Equal(FixedNow, revoked.RevokedAt);

        await Assert.ThrowsAsync<DomainConflictException>(() =>
            scenario.DeviceService.HeartbeatAsync(
                revoked.Id,
                new DeviceHeartbeatCommand(
                    agentTs,
                    scenario.Sign("heartbeat", revoked.Id, agentTs)),
                CancellationToken.None));
    }

    [Fact]
    public async Task Enroll_WithInvalidOrReplayedToken_IsDenied()
    {
        await using var scenario = await CreateScenarioAsync();
        await Assert.ThrowsAsync<SecurityAccessDeniedException>(() =>
            scenario.DeviceService.EnrollAsync(
                new EnrollDeviceCommand(
                    "no-such-token",
                    "dev-identifier-hash-2",
                    "Rogue station",
                    scenario.DeviceRsa.ExportSubjectPublicKeyInfoPem()),
                CancellationToken.None));

        var token = await scenario.DeviceService.IssueRegistrationTokenAsync(
            new IssueDeviceRegistrationTokenCommand(scenario.Manager.Id, 60),
            CancellationToken.None);
        await scenario.DeviceService.EnrollAsync(
            new EnrollDeviceCommand(
                token.RawToken,
                "dev-identifier-hash-3",
                "Legit station",
                scenario.DeviceRsa.ExportSubjectPublicKeyInfoPem()),
            CancellationToken.None);
        await Assert.ThrowsAsync<SecurityAccessDeniedException>(() =>
            scenario.DeviceService.EnrollAsync(
                new EnrollDeviceCommand(
                    token.RawToken,
                    "dev-identifier-hash-4",
                    "Token-replay station",
                    scenario.DeviceRsa.ExportSubjectPublicKeyInfoPem()),
                CancellationToken.None));
    }

    [Fact]
    public async Task PrinterOwnership_AndEnabledCheck_AreEnforcedBeforeDispatch()
    {
        await using var scenario = await CreateScenarioAsync();
        var deviceA = await ApproveNewDeviceAsync(scenario, "device-a-1");
        var deviceB = await ApproveNewDeviceAsync(scenario, "device-b-1");

        var printerA = await scenario.DeviceService.RegisterPrinterAsync(
            new RegisterDevicePrinterCommand(
                scenario.Manager.Id,
                deviceA.Id,
                "EPSON TM-T20",
                "Counter printer",
                PrinterType.Receipt),
            CancellationToken.None);
        var printerB = await scenario.DeviceService.RegisterPrinterAsync(
            new RegisterDevicePrinterCommand(
                scenario.Manager.Id,
                deviceB.Id,
                "HP LaserJet",
                "B printer",
                PrinterType.A4),
            CancellationToken.None);
        var printerDisabled = await scenario.DeviceService.RegisterPrinterAsync(
            new RegisterDevicePrinterCommand(
                scenario.Manager.Id,
                deviceA.Id,
                "OLD PRINTER",
                "Disabled printer",
                PrinterType.Receipt),
            CancellationToken.None);
        await scenario.DeviceService.SetPrinterEnabledAsync(
            new SetDevicePrinterEnabledCommand(
                scenario.Manager.Id,
                deviceA.Id,
                printerDisabled.Id,
                false,
                printerDisabled.RowVersion),
            CancellationToken.None);

        await Assert.ThrowsAsync<ApplicationResourceNotFoundException>(() =>
            scenario.JobService.RequestDevicePrintAsync(
                scenario.Invoice.Id,
                new RequestDevicePrintCommand(
                    scenario.Manager.Id,
                    deviceA.Id,
                    printerB.Id,
                    null,
                    1,
                    CanReprint: false,
                    null,
                    $"key-{Guid.NewGuid():N}"),
                CancellationToken.None));

        await Assert.ThrowsAsync<ApplicationConflictException>(() =>
            scenario.JobService.RequestDevicePrintAsync(
                scenario.Invoice.Id,
                new RequestDevicePrintCommand(
                    scenario.Manager.Id,
                    deviceA.Id,
                    printerDisabled.Id,
                    null,
                    1,
                    CanReprint: false,
                    null,
                    $"key-{Guid.NewGuid():N}"),
                CancellationToken.None));

        var profile = await scenario.DeviceService.CreatePrintProfileAsync(
            new CreateDevicePrintProfileCommand(
                scenario.Manager.Id,
                deviceA.Id,
                "A4 Receipt",
                PaperSize.A4,
                PrintOrientation.Portrait,
                1,
                ColorMode.Monochrome,
                10,
                10,
                10,
                10),
            CancellationToken.None);

        var job = await scenario.JobService.RequestDevicePrintAsync(
            scenario.Invoice.Id,
            new RequestDevicePrintCommand(
                scenario.Manager.Id,
                deviceA.Id,
                printerA.Id,
                profile.Id,
                2,
                CanReprint: false,
                null,
                $"key-{Guid.NewGuid():N}"),
            CancellationToken.None);
        Assert.Equal(InvoicePrintStatus.Requested, job.Status);
        Assert.Equal(2, job.Copies);

        var log = await scenario.Context.InvoicePrintLogs.SingleAsync();
        Assert.Equal(job.Id, log.PrintJobId);
        Assert.Equal(deviceA.Id, log.DesktopDeviceId);
        Assert.Equal(InvoicePrintStatus.Requested, log.Status);
    }

    [Fact]
    public async Task DuplicateIdempotencyKey_ReturnsSameJob_AndCrossInvoiceKeyIsRejected()
    {
        await using var scenario = await CreateScenarioAsync();
        var device = await ApproveNewDeviceAsync(scenario, "device-key-1");

        var first = await scenario.JobService.RequestDevicePrintAsync(
            scenario.Invoice.Id,
            new RequestDevicePrintCommand(
                scenario.Manager.Id,
                device.Id,
                null,
                null,
                1,
                CanReprint: false,
                null,
                "dispatch-key-1"),
            CancellationToken.None);
        var duplicate = await scenario.JobService.RequestDevicePrintAsync(
            scenario.Invoice.Id,
            new RequestDevicePrintCommand(
                scenario.Manager.Id,
                device.Id,
                null,
                null,
                1,
                CanReprint: false,
                null,
                "dispatch-key-1"),
            CancellationToken.None);
        Assert.Equal(first.Id, duplicate.Id);

        await Assert.ThrowsAsync<ApplicationConflictException>(() =>
            scenario.JobService.RequestDevicePrintAsync(
                scenario.SecondInvoice.Id,
                new RequestDevicePrintCommand(
                    scenario.Manager.Id,
                    device.Id,
                    null,
                    null,
                    1,
                    CanReprint: false,
                    null,
                    "dispatch-key-1"),
                CancellationToken.None));

        var conflict = await scenario.JobService.RequestDevicePrintAsync(
            scenario.SecondInvoice.Id,
            new RequestDevicePrintCommand(
                scenario.Manager.Id,
                device.Id,
                null,
                null,
                1,
                CanReprint: false,
                null,
                $"key-{Guid.NewGuid():N}"),
            CancellationToken.None);
        await Assert.ThrowsAsync<ApplicationConflictException>(() =>
            scenario.JobService.RequestDevicePrintAsync(
                scenario.SecondInvoice.Id,
                new RequestDevicePrintCommand(
                    scenario.Manager.Id,
                    device.Id,
                    null,
                    null,
                    1,
                    CanReprint: false,
                    null,
                    null),
                CancellationToken.None));
        Assert.Equal(InvoicePrintStatus.Requested, conflict.Status);
    }

    [Fact]
    public async Task ResultReporting_RequiresValidOneWayDeviceSignature()
    {
        await using var scenario = await CreateScenarioAsync();
        var device = await ApproveNewDeviceAsync(scenario, "device-sig-1");
        var impostor = await ApproveNewDeviceAsync(scenario, "device-sig-2");
        var job = await scenario.JobService.RequestDevicePrintAsync(
            scenario.Invoice.Id,
            new RequestDevicePrintCommand(
                scenario.Manager.Id,
                device.Id,
                null,
                null,
                1,
                CanReprint: false,
                null,
                "sig-job-key-1"),
            CancellationToken.None);

        await Assert.ThrowsAsync<SecurityAccessDeniedException>(() =>
            scenario.JobService.CompleteDevicePrintAsync(
                job.Id,
                new CompleteDevicePrintCommand(
                    FixedNow,
                    true,
                    "EPSON TM-T20",
                    null,
                    "forged-signature"),
                CancellationToken.None));

        var impostorSignature = scenario.Sign(
            $"complete|{job.Id:N}|{impostor.Id:N}|{FixedNow:o}|True|EPSON TM-T20|");
        await Assert.ThrowsAsync<SecurityAccessDeniedException>(() =>
            scenario.JobService.CompleteDevicePrintAsync(
                job.Id,
                new CompleteDevicePrintCommand(
                    FixedNow,
                    true,
                    "EPSON TM-T20",
                    null,
                    impostorSignature),
                CancellationToken.None));

        var staleSignature = scenario.Sign(
            $"complete|{job.Id:N}|{device.Id:N}|{FixedNow.AddHours(-1):o}|True|EPSON TM-T20|");
        await Assert.ThrowsAsync<SecurityAccessDeniedException>(() =>
            scenario.JobService.CompleteDevicePrintAsync(
                job.Id,
                new CompleteDevicePrintCommand(
                    FixedNow.AddHours(-1),
                    true,
                    "EPSON TM-T20",
                    null,
                    staleSignature),
                CancellationToken.None));

        var poll = await scenario.JobService.GetPendingJobsAsync(
            device.Id,
            new DeviceHeartbeatCommand(
                FixedNow,
                scenario.Sign("poll", device.Id, FixedNow)),
            CancellationToken.None);
        Assert.Single(poll);
        Assert.Equal(job.Id, poll[0].JobId);

        var goodSignature = scenario.Sign(
            $"complete|{job.Id:N}|{device.Id:N}|{FixedNow:o}|True|EPSON TM-T20|");
        var completed = await scenario.JobService.CompleteDevicePrintAsync(
            job.Id,
            new CompleteDevicePrintCommand(
                FixedNow,
                true,
                "EPSON TM-T20",
                null,
                goodSignature),
            CancellationToken.None);
        Assert.Equal(InvoicePrintStatus.Succeeded, completed.Status);
        Assert.Equal("EPSON TM-T20", completed.PrinterName);

        var replay = await scenario.JobService.CompleteDevicePrintAsync(
            job.Id,
            new CompleteDevicePrintCommand(
                FixedNow,
                true,
                "EPSON TM-T20",
                null,
                goodSignature),
            CancellationToken.None);
        Assert.Equal(InvoicePrintStatus.Succeeded, replay.Status);

        Assert.Empty(await scenario.JobService.GetPendingJobsAsync(
            device.Id,
            new DeviceHeartbeatCommand(
                FixedNow,
                scenario.Sign("poll", device.Id, FixedNow)),
            CancellationToken.None));
        var log = await scenario.Context.InvoicePrintLogs.SingleAsync();
        Assert.Equal(InvoicePrintStatus.Succeeded, log.Status);
        Assert.Equal("EPSON TM-T20", log.PrinterName);
    }

    [Fact]
    public async Task FailureCodes_AreSanitized_BeforeStored()
    {
        await using var scenario = await CreateScenarioAsync();
        var device = await ApproveNewDeviceAsync(scenario, "device-fail-1");
        var job = await scenario.JobService.RequestDevicePrintAsync(
            scenario.Invoice.Id,
            new RequestDevicePrintCommand(
                scenario.Manager.Id,
                device.Id,
                null,
                null,
                1,
                CanReprint: false,
                null,
                "fail-job-key-1"),
            CancellationToken.None);

        var rawFailurePayload = $"complete|{job.Id:N}|{device.Id:N}|{FixedNow:o}|False||Desktop says: printer exploded";
        await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.JobService.CompleteDevicePrintAsync(
                job.Id,
                new CompleteDevicePrintCommand(
                    FixedNow,
                    false,
                    null,
                    "Desktop says: printer exploded",
                    scenario.Sign(rawFailurePayload)),
                CancellationToken.None));

        var failed = await scenario.JobService.CompleteDevicePrintAsync(
            job.Id,
            new CompleteDevicePrintCommand(
                FixedNow,
                false,
                null,
                "OUT_OF_PAPER",
                scenario.Sign($"complete|{job.Id:N}|{device.Id:N}|{FixedNow:o}|False||OUT_OF_PAPER")),
            CancellationToken.None);
        Assert.Equal(InvoicePrintStatus.Failed, failed.Status);
        Assert.Equal("OUT_OF_PAPER", failed.FailureCode);
        Assert.Null(failed.PrinterName);
    }

    [Fact]
    public async Task Retry_And_Reprint_Rules_PreserveImmutableLogHistory()
    {
        await using var scenario = await CreateScenarioAsync();
        var device = await ApproveNewDeviceAsync(scenario, "device-retry-1");
        var job = await scenario.JobService.RequestDevicePrintAsync(
            scenario.Invoice.Id,
            new RequestDevicePrintCommand(
                scenario.Manager.Id,
                device.Id,
                null,
                null,
                1,
                CanReprint: false,
                null,
                "retry-job-key-1"),
            CancellationToken.None);
        var failed = await scenario.JobService.CompleteDevicePrintAsync(
            job.Id,
            new CompleteDevicePrintCommand(
                FixedNow,
                false,
                null,
                "PRINTER_UNAVAILABLE",
                scenario.Sign($"complete|{job.Id:N}|{device.Id:N}|{FixedNow:o}|False||PRINTER_UNAVAILABLE")),
            CancellationToken.None);
        Assert.Equal(InvoicePrintStatus.Failed, failed.Status);

        var retried = await scenario.JobService.RetryDevicePrintAsync(
            job.Id,
            scenario.Manager.Id,
            failed.RowVersion,
            CancellationToken.None);
        Assert.Equal(InvoicePrintStatus.Requested, retried.Status);
        Assert.Equal(1, retried.RetryCount);

        var completed = await scenario.JobService.CompleteDevicePrintAsync(
            job.Id,
            new CompleteDevicePrintCommand(
                FixedNow,
                true,
                "EPSON TM-T20",
                null,
                scenario.Sign($"complete|{job.Id:N}|{device.Id:N}|{FixedNow:o}|True|EPSON TM-T20|")),
            CancellationToken.None);
        Assert.Equal(InvoicePrintStatus.Succeeded, completed.Status);

        var logs = await scenario.Context.InvoicePrintLogs
            .OrderBy(log => log.CreatedAt)
            .ToListAsync();
        Assert.Equal(2, logs.Count);
        Assert.Equal(InvoicePrintStatus.Failed, logs[0].Status);
        Assert.Equal("PRINTER_UNAVAILABLE", logs[0].FailureCode);
        Assert.Equal(InvoicePrintStatus.Succeeded, logs[1].Status);
        Assert.Equal(job.Id, logs[0].PrintJobId);
        Assert.Equal(job.Id, logs[1].PrintJobId);

        await Assert.ThrowsAsync<SecurityAccessDeniedException>(() =>
            scenario.JobService.RequestDevicePrintAsync(
                scenario.Invoice.Id,
                new RequestDevicePrintCommand(
                    scenario.Manager.Id,
                    device.Id,
                    null,
                    null,
                    1,
                    CanReprint: false,
                    null,
                    "reprint-without-permission-1"),
                CancellationToken.None));

        var reprint = await scenario.JobService.RequestDevicePrintAsync(
            scenario.Invoice.Id,
            new RequestDevicePrintCommand(
                scenario.Manager.Id,
                device.Id,
                null,
                null,
                2,
                CanReprint: true,
                "Customer requested another copy",
                "reprint-with-permission-1"),
            CancellationToken.None);
        Assert.True(reprint.IsReprint);
        Assert.Equal("Customer requested another copy", reprint.ReprintReason);
        Assert.Equal(2, reprint.Copies);

        Assert.Equal(3, await scenario.Context.InvoicePrintLogs.CountAsync());
        var attempts = await scenario.Context.InvoicePrintLogs
            .OrderBy(log => log.CreatedAt)
            .ToListAsync();
        Assert.Equal(InvoicePrintStatus.Failed, attempts[0].Status);
        Assert.Equal(InvoicePrintStatus.Succeeded, attempts[1].Status);
        Assert.Equal(InvoicePrintStatus.Requested, attempts[2].Status);
        Assert.True(attempts[2].IsReprint);
    }

    [Fact]
    public async Task AtMostOneActiveDefaultPrinter_AndProfile_PerDevice()
    {
        await using var scenario = await CreateScenarioAsync();
        var device = await ApproveNewDeviceAsync(scenario, "device-default-1");
        var printer1 = await scenario.DeviceService.RegisterPrinterAsync(
            new RegisterDevicePrinterCommand(
                scenario.Manager.Id,
                device.Id,
                "PRINTER-1",
                "Printer one",
                PrinterType.Receipt),
            CancellationToken.None);
        var printer2 = await scenario.DeviceService.RegisterPrinterAsync(
            new RegisterDevicePrinterCommand(
                scenario.Manager.Id,
                device.Id,
                "PRINTER-2",
                "Printer two",
                PrinterType.A4),
            CancellationToken.None);

        var secondDefault = await scenario.DeviceService.SetPrinterDefaultAsync(
            new SetDevicePrinterDefaultCommand(
                scenario.Manager.Id,
                device.Id,
                printer2.Id,
                true,
                printer2.RowVersion),
            CancellationToken.None);
        Assert.True(secondDefault.IsDefault);
        var reloaded = await scenario.DeviceService.GetDeviceAsync(device.Id, scenario.Manager.Id, CancellationToken.None);
        var defaultPrinters = reloaded.Printers.Where(printer => printer.IsDefault).ToArray();
        Assert.Single(defaultPrinters);
        Assert.Equal(printer2.Id, defaultPrinters[0].Id);

        var disposition = await scenario.DeviceService.SetPrinterEnabledAsync(
            new SetDevicePrinterEnabledCommand(
                scenario.Manager.Id,
                device.Id,
                printer2.Id,
                false,
                secondDefault.RowVersion),
            CancellationToken.None);
        Assert.False(disposition.IsDefault);
        Assert.False(disposition.IsEnabled);

        var profile1 = await scenario.DeviceService.CreatePrintProfileAsync(
            new CreateDevicePrintProfileCommand(
                scenario.Manager.Id,
                device.Id,
                "PROFILE-1",
                PaperSize.A4,
                PrintOrientation.Portrait,
                1,
                ColorMode.Monochrome,
                0, 0, 0, 0),
            CancellationToken.None);
        var profile2 = await scenario.DeviceService.CreatePrintProfileAsync(
            new CreateDevicePrintProfileCommand(
                scenario.Manager.Id,
                device.Id,
                "PROFILE-2",
                PaperSize.Receipt80,
                PrintOrientation.Portrait,
                1,
                ColorMode.Monochrome,
                5, 5, 5, 5),
            CancellationToken.None);
        var profile2Default = await scenario.DeviceService.SetPrintProfileDefaultAsync(
            new SetDevicePrintProfileDefaultCommand(
                scenario.Manager.Id,
                device.Id,
                profile2.Id,
                true,
                profile2.RowVersion),
            CancellationToken.None);
        Assert.True(profile2Default.IsDefault);
        var reloadedProfiles = await scenario.DeviceService.GetDeviceAsync(
            device.Id,
            scenario.Manager.Id,
            CancellationToken.None);
        Assert.Single(reloadedProfiles.Profiles.Where(profile => profile.IsDefault));
    }

    [Fact]
    public async Task PrintDocument_RequiresSignature_AndContainsSanitizedInvoiceData()
    {
        await using var scenario = await CreateScenarioAsync();
        var device = await ApproveNewDeviceAsync(scenario, "device-doc-1");
        var job = await scenario.JobService.RequestDevicePrintAsync(
            scenario.Invoice.Id,
            new RequestDevicePrintCommand(
                scenario.Manager.Id,
                device.Id,
                null,
                null,
                1,
                CanReprint: false,
                null,
                "doc-job-key-1"),
            CancellationToken.None);

        await Assert.ThrowsAsync<SecurityAccessDeniedException>(() =>
            scenario.JobService.GetPrintDocumentAsync(
                job.Id,
                new DeviceHeartbeatCommand(FixedNow, "forged"),
                CancellationToken.None));

        var document = await scenario.JobService.GetPrintDocumentAsync(
            job.Id,
            new DeviceHeartbeatCommand(
                FixedNow,
                scenario.Sign($"document|{job.Id:N}|{device.Id:N}|{FixedNow:o}")),
            CancellationToken.None);
        Assert.Equal(job.Id, document.JobId);
        Assert.Equal(scenario.Invoice.Id, document.InvoiceId);
        Assert.Contains("<html", document.Html, StringComparison.Ordinal);
        Assert.Contains("INV-7000", document.Html, StringComparison.Ordinal);
        Assert.Contains("5,000,000", document.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", document.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dir=\"rtl\"", document.Html, StringComparison.Ordinal);
    }

    private static async Task<Scenario> CreateScenarioAsync()
    {
        var timeProvider = new FixedTimeProvider(FixedNow);
        var context = CreateContext(timeProvider);
        var manager = new ApplicationUser("Owner") { UserName = "owner@example.test" };
        context.Users.Add(manager);
        var customer = new ApplicationUser("Customer") { UserName = "customer@example.test" };
        context.Users.Add(customer);
        await context.SaveChangesAsync();

        var order = new Order(customer.Id, "ORD-7000", 5_000_000, 0, 0, "Customer", "0012345678");
        var payment = new Payment(order.Id, "MANUAL", order.GrandTotalRials, PaymentMethod.BankTransfer);
        payment.Verify("GW-7000", FixedNow);
        var invoice = new Invoice(
            order.Id,
            customer.Id,
            "INV-7000",
            FixedNow,
            order.ItemsSubtotalRials,
            order.DiscountRials,
            order.ShippingRials,
            payment.Id,
            "Customer",
            "0012345678");
        var secondOrder = new Order(customer.Id, "ORD-7001", 3_000_000, 0, 0, "Customer", "0012345678");
        var secondPayment = new Payment(
            secondOrder.Id,
            "MANUAL",
            secondOrder.GrandTotalRials,
            PaymentMethod.BankTransfer);
        secondPayment.Verify("GW-7001", FixedNow);
        var secondInvoice = new Invoice(
            secondOrder.Id,
            customer.Id,
            "INV-7001",
            FixedNow,
            secondOrder.ItemsSubtotalRials,
            secondOrder.DiscountRials,
            secondOrder.ShippingRials,
            secondPayment.Id,
            "Customer",
            "0012345678");
        context.Orders.AddRange(order, secondOrder);
        context.Payments.AddRange(payment, secondPayment);
        context.Invoices.AddRange(invoice, secondInvoice);
        await context.SaveChangesAsync();

        var deviceService = new DesktopDeviceService(context, timeProvider);
        var jobService = new InvoicePrintJobService(context, deviceService, timeProvider);
        return new Scenario(
            context,
            manager,
            customer,
            invoice,
            secondInvoice,
            deviceService,
            jobService,
            timeProvider);
    }

    private static GoldInvoiceDbContext CreateContext(TimeProvider timeProvider)
    {
        var options = new DbContextOptionsBuilder<GoldInvoiceDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .AddInterceptors(new AuditingSaveChangesInterceptor(timeProvider))
            .Options;
        return new GoldInvoiceDbContext(options);
    }

    private static async Task<DeviceInfo> ApproveNewDeviceAsync(Scenario scenario, string identifierSuffix)
    {
        var token = await scenario.DeviceService.IssueRegistrationTokenAsync(
            new IssueDeviceRegistrationTokenCommand(scenario.Manager.Id, 60),
            CancellationToken.None);
        var pending = await scenario.DeviceService.EnrollAsync(
            new EnrollDeviceCommand(
                token.RawToken,
                $"identifier-{identifierSuffix}",
                $"Station {identifierSuffix}",
                scenario.DeviceRsa.ExportSubjectPublicKeyInfoPem()),
            CancellationToken.None);
        return await scenario.DeviceService.ApproveAsync(
            pending.Id,
            new ApproveDeviceCommand(scenario.Manager.Id, pending.RowVersion),
            CancellationToken.None);
    }

    private sealed class Scenario(
        GoldInvoiceDbContext context,
        ApplicationUser manager,
        ApplicationUser customer,
        Invoice invoice,
        Invoice secondInvoice,
        DesktopDeviceService deviceService,
        InvoicePrintJobService jobService,
        TimeProvider timeProvider) : IAsyncDisposable
    {
        public GoldInvoiceDbContext Context { get; } = context;
        public ApplicationUser Manager { get; } = manager;
        public ApplicationUser Customer { get; } = customer;
        public Invoice Invoice { get; } = invoice;
        public Invoice SecondInvoice { get; } = secondInvoice;
        public DesktopDeviceService DeviceService { get; } = deviceService;
        public InvoicePrintJobService JobService { get; } = jobService;
        public TimeProvider TimeProvider { get; } = timeProvider;
        public RSA DeviceRsa { get; } = RSA.Create(2048);

        public string Sign(string operation, Guid deviceId, DateTimeOffset timestamp) =>
            Sign($"poll".Equals(operation, StringComparison.Ordinal)
                ? $"poll|{deviceId:N}|{timestamp:o}"
                : $"{operation}|{deviceId:N}|{timestamp:o}");

        public string Sign(string payload) => Convert.ToBase64String(
            DeviceRsa.SignData(
                Encoding.UTF8.GetBytes(payload),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1));

        public ValueTask DisposeAsync()
        {
            DeviceRsa.Dispose();
            return Context.DisposeAsync();
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}