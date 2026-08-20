using System.Text.Json;
using GoldInvoice.Application.Common;
using GoldInvoice.Application.Invoicing;
using GoldInvoice.Application.Integration;
using GoldInvoice.Application.Payments;
using GoldInvoice.Domain.Orders;
using GoldInvoice.Domain.Payments;
using GoldInvoice.Domain.Platform;
using GoldInvoice.Infrastructure.Inventory;
using GoldInvoice.Infrastructure.Integration;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoldInvoice.Infrastructure.Payments;

internal sealed class FlexiblePaymentService(
    GoldInvoiceDbContext dbContext,
    IPaymentService paymentService,
    InventoryReservationCoordinator reservationCoordinator,
    IInvoiceIssuanceService invoiceIssuanceService,
    IOutboxWriter outboxWriter,
    TimeProvider timeProvider) : IFlexiblePaymentService
{
    private const string PlanPrefix = "Finance.InstallmentPlan.";
    private const string LinePrefix = "Finance.InstallmentLine.";
    private const string TrustPrefix = "Finance.TrustFund.Entry.";

    private const string PlanDataType = "json:InstallmentPlan.v1";
    private const string LineDataType = "json:InstallmentLine.v1";
    private const string TrustDataType = "json:TrustFundEntry.v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<InstallmentPlanInfo>> GetInstallmentPlansAsync(
        CancellationToken cancellationToken)
    {
        var planDocuments = await LoadPlanDocumentsAsync(cancellationToken);
        var lineDocuments = await LoadLineDocumentsAsync(asTracking: false, cancellationToken);

        var result = new List<InstallmentPlanInfo>(planDocuments.Count);
        foreach (var plan in planDocuments.OrderByDescending(item => item.CreatedAt))
        {
            result.Add(await MapPlanAsync(
                plan,
                lineDocuments.Where(line => line.PlanId == plan.Id).ToArray(),
                cancellationToken));
        }

        return result;
    }

    public async Task<InstallmentPlanInfo> GetInstallmentPlanAsync(
        Guid planId,
        CancellationToken cancellationToken)
    {
        if (planId == Guid.Empty)
        {
            throw new ArgumentException("A valid installment plan is required.", nameof(planId));
        }

        var plans = await LoadPlanDocumentsAsync(cancellationToken);
        var plan = plans.SingleOrDefault(item => item.Id == planId)
            ?? throw new ApplicationResourceNotFoundException();

        var lines = (await LoadLineDocumentsAsync(asTracking: false, cancellationToken))
            .Where(item => item.PlanId == planId)
            .ToArray();

        return await MapPlanAsync(plan, lines, cancellationToken);
    }

    public async Task<InstallmentPlanInfo> CreateInstallmentPlanAsync(
        CreateInstallmentPlanCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateActor(command.ActorUserId);

        if (command.OrderId == Guid.Empty)
        {
            throw new ArgumentException("A valid order is required.", nameof(command));
        }

        if (command.Installments.Count is < 1 or > 24)
        {
            throw new ArgumentOutOfRangeException(nameof(command.Installments));
        }

        var orderedDrafts = command.Installments
            .Select((item, index) => new { Draft = item, Index = index })
            .OrderBy(item => item.Draft.DueOn)
            .ThenBy(item => item.Index)
            .ToArray();

        if (orderedDrafts.Any(item =>
                item.Draft.DueOn == default ||
                item.Draft.AmountRials <= 0))
        {
            throw new ArgumentException(
                "Every installment requires a valid date and positive amount.",
                nameof(command));
        }

        long total;
        try
        {
            total = orderedDrafts.Sum(item =>
                checked(item.Draft.AmountRials));
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(command.Installments));
        }

        await using var transaction =
            await PersistenceUtilities.BeginSerializableTransactionAsync(
                dbContext,
                cancellationToken);

        var order = await dbContext.Orders
            .SingleOrDefaultAsync(
                item => item.Id == command.OrderId,
                cancellationToken)
            ?? throw new ApplicationResourceNotFoundException();

        if (order.Status is not OrderStatus.AwaitingPayment and
            not OrderStatus.PaymentReview)
        {
            throw new ApplicationConflictException();
        }

        if (total != order.GrandTotalRials)
        {
            throw new ArgumentException(
                "The installment total must exactly equal the order grand total.",
                nameof(command.Installments));
        }

        var existingPlans = await LoadPlanDocumentsAsync(cancellationToken);
        if (existingPlans.Any(item => item.OrderId == order.Id))
        {
            throw new ApplicationConflictException();
        }

        var previousPayments = await dbContext.Payments
            .Where(item => item.OrderId == order.Id)
            .ToListAsync(cancellationToken);

        if (previousPayments.Any(item =>
                item.Status is PaymentStatus.Verified or
                    PaymentStatus.Refunded))
        {
            throw new ApplicationConflictException();
        }

        var now = timeProvider.GetUtcNow();
        foreach (var pendingPayment in previousPayments.Where(item =>
                     item.Status is PaymentStatus.Pending or
                         PaymentStatus.Processing or
                         PaymentStatus.RequiresReview))
        {
            pendingPayment.Cancel(now);
        }

        var finalDueDate = orderedDrafts[^1].Draft.DueOn;
        var reservationUntil = new DateTimeOffset(
            finalDueDate.ToDateTime(TimeOnly.MaxValue),
            TimeSpan.Zero).AddDays(30);

        await reservationCoordinator.EnsureInstallmentReservationAsync(
            order.Id,
            reservationUntil,
            cancellationToken);

        var plan = new InstallmentPlanDocument(
            Guid.NewGuid(),
            order.Id,
            order.CustomerId,
            order.OrderNumber,
            order.CustomerNameSnapshot ?? "مشتری",
            order.GrandTotalRials,
            now);

        dbContext.SystemSettings.Add(NewSetting(
            $"{PlanPrefix}{plan.Id:N}",
            PlanDataType,
            plan));

        var lines = new List<InstallmentLineDocument>(
            orderedDrafts.Length);

        for (var index = 0; index < orderedDrafts.Length; index++)
        {
            var draft = orderedDrafts[index].Draft;
            var line = new InstallmentLineDocument(
                Guid.NewGuid(),
                plan.Id,
                order.Id,
                index + 1,
                draft.DueOn,
                draft.AmountRials,
                PaidAt: null,
                Reference: null);

            lines.Add(line);
            dbContext.SystemSettings.Add(NewSetting(
                $"{LinePrefix}{line.Id:N}",
                LineDataType,
                line));
        }

        await PersistenceUtilities.SaveChangesAsync(
            dbContext,
            cancellationToken);
        await PersistenceUtilities.CommitAsync(
            transaction,
            cancellationToken);

        return await MapPlanAsync(
            plan,
            lines,
            cancellationToken);
    }

    public async Task<InstallmentPlanInfo> PayInstallmentAsync(
        PayInstallmentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateActor(command.ActorUserId);

        if (command.PlanId == Guid.Empty ||
            command.InstallmentId == Guid.Empty)
        {
            throw new ArgumentException(
                "A valid plan and installment are required.",
                nameof(command));
        }

        await using var transaction =
            await PersistenceUtilities.BeginSerializableTransactionAsync(
                dbContext,
                cancellationToken);

        var plans = await LoadPlanDocumentsAsync(cancellationToken);
        var plan = plans.SingleOrDefault(item =>
            item.Id == command.PlanId)
            ?? throw new ApplicationResourceNotFoundException();

        var trackedSettings = await dbContext.SystemSettings
            .Where(setting =>
                setting.Key.StartsWith(LinePrefix))
            .ToListAsync(cancellationToken);

        var parsed = trackedSettings
            .Select(setting => new
            {
                Setting = setting,
                Document = Deserialize<InstallmentLineDocument>(
                    setting,
                    LineDataType),
            })
            .Where(item =>
                item.Document is not null &&
                item.Document.PlanId == plan.Id)
            .Select(item =>
                new TrackedInstallment(
                    item.Setting,
                    item.Document!))
            .OrderBy(item =>
                item.Document.Sequence)
            .ToArray();

        if (parsed.Length == 0)
        {
            throw new ApplicationResourceNotFoundException();
        }

        var target = parsed.SingleOrDefault(item =>
            item.Document.Id == command.InstallmentId)
            ?? throw new ApplicationResourceNotFoundException();

        var order = await dbContext.Orders
            .SingleOrDefaultAsync(
                item => item.Id == plan.OrderId,
                cancellationToken)
            ?? throw new ApplicationResourceNotFoundException();

        var existingSettlement =
            await FindInstallmentSettlementAsync(
                plan,
                cancellationToken);

        if (existingSettlement is not null)
        {
            var invoiceId = await FindInvoiceIdAsync(
                existingSettlement.Id,
                cancellationToken);

            if (invoiceId is null)
            {
                throw new ApplicationConflictException();
            }

            await PersistenceUtilities.CommitAsync(
                transaction,
                cancellationToken);

            return new InstallmentPlanInfo(
                plan.Id,
                plan.OrderId,
                plan.CustomerId,
                plan.OrderNumber,
                plan.CustomerName,
                plan.TotalAmountRials,
                plan.CreatedAt,
                parsed
                    .Select(item => item.Document)
                    .OrderBy(item => item.Sequence)
                    .Select(MapLine)
                    .ToArray(),
                existingSettlement.Id,
                invoiceId);
        }

        if (order.Status is not OrderStatus.AwaitingPayment and
            not OrderStatus.PaymentReview)
        {
            throw new ApplicationConflictException();
        }

        if (target.Document.PaidAt is not null)
        {
            await PersistenceUtilities.CommitAsync(
                transaction,
                cancellationToken);

            return await MapPlanAsync(
                plan,
                parsed.Select(item => item.Document).ToArray(),
                cancellationToken);
        }

        var firstUnpaid = parsed.First(item =>
            item.Document.PaidAt is null);

        if (firstUnpaid.Document.Id != target.Document.Id)
        {
            throw new ApplicationConflictException();
        }

        var now = timeProvider.GetUtcNow();

        var orderPayments = await dbContext.Payments
            .Where(item => item.OrderId == order.Id)
            .ToListAsync(cancellationToken);

        if (orderPayments.Any(item =>
                item.Status is PaymentStatus.Verified or
                    PaymentStatus.Refunded))
        {
            throw new ApplicationConflictException();
        }

        foreach (var pendingPayment in orderPayments.Where(item =>
                     item.Status is PaymentStatus.Pending or
                         PaymentStatus.Processing or
                         PaymentStatus.RequiresReview))
        {
            pendingPayment.Cancel(now);
        }

        var lastDueOn = parsed.Max(item =>
            item.Document.DueOn);

        var contractualReservationUntil =
            new DateTimeOffset(
                lastDueOn.ToDateTime(TimeOnly.MaxValue),
                TimeSpan.Zero).AddDays(30);

        var minimumReservationUntil = now.AddDays(30);

        var reservationUntil =
            contractualReservationUntil > minimumReservationUntil
                ? contractualReservationUntil
                : minimumReservationUntil;

        await reservationCoordinator.EnsureInstallmentReservationAsync(
            order.Id,
            reservationUntil,
            cancellationToken);

        var normalizedReference = NormalizeOptional(
            command.Reference,
            200);

        var updated = target.Document with
        {
            PaidAt = now,
            Reference = normalizedReference,
        };

        target.Setting.UpdateValue(
            LineDataType,
            JsonSerializer.Serialize(
                updated,
                JsonOptions));

        var allLinesAfterUpdate = parsed
            .Select(item =>
                item.Document.Id == updated.Id
                    ? updated
                    : item.Document)
            .OrderBy(item =>
                item.Sequence)
            .ToArray();

        var isFinalInstallment =
            allLinesAfterUpdate.All(item =>
                item.PaidAt is not null);

        if (!isFinalInstallment)
        {
            await PersistenceUtilities.SaveChangesAsync(
                dbContext,
                cancellationToken);
            await PersistenceUtilities.CommitAsync(
                transaction,
                cancellationToken);

            return new InstallmentPlanInfo(
                plan.Id,
                plan.OrderId,
                plan.CustomerId,
                plan.OrderNumber,
                plan.CustomerName,
                plan.TotalAmountRials,
                plan.CreatedAt,
                allLinesAfterUpdate
                    .Select(MapLine)
                    .ToArray(),
                null,
                null);
        }

        // Dedicated installment finalizer:
        // intentionally bypasses the generic one-shot manual-payment API.
        var paymentKeyHash = PersistenceUtilities.Hash(
            $"Installment.Final:{plan.Id:N}");

        var payment = new Payment(
            order.Id,
            "MANUAL",
            order.GrandTotalRials,
            PaymentMethod.BankTransfer,
            paymentGatewayId: null,
            idempotencyKeyHash: paymentKeyHash);

        payment.Verify(
            BuildSettlementReference(
                "INSTALLMENT",
                plan.Id,
                normalizedReference),
            now);

        dbContext.Payments.Add(payment);

        var fromStatus = order.Status;
        order.MarkPaid(now);

        dbContext.OrderStatusHistory.Add(
            new OrderStatusHistory(
                order.Id,
                fromStatus,
                OrderStatus.Paid,
                now,
                command.ActorUserId,
                "Installment contract fully collected"));

        outboxWriter.AddOrderStatusChanged(
            order,
            fromStatus,
            now);

        await reservationCoordinator.ConfirmForPaymentAsync(
            order.Id,
            payment.Id,
            now,
            cancellationToken);

        var invoice =
            await invoiceIssuanceService.IssueForPaidOrderAsync(
                order.Id,
                payment.Id,
                now,
                cancellationToken);

        await PersistenceUtilities.SaveChangesAsync(
            dbContext,
            cancellationToken);
        await PersistenceUtilities.CommitAsync(
            transaction,
            cancellationToken);

        return new InstallmentPlanInfo(
            plan.Id,
            plan.OrderId,
            plan.CustomerId,
            plan.OrderNumber,
            plan.CustomerName,
            plan.TotalAmountRials,
            plan.CreatedAt,
            allLinesAfterUpdate
                .Select(MapLine)
                .ToArray(),
            payment.Id,
            invoice.Id);
    }

    public async Task<TrustFundSnapshotInfo> GetTrustFundSnapshotAsync(
        CancellationToken cancellationToken)
    {
        var entries = await LoadTrustEntriesAsync(cancellationToken);
        var balances = entries
            .GroupBy(item => item.CustomerId)
            .Select(group => new TrustFundBalanceInfo(
                group.Key,
                CalculateBalance(group)))
            .OrderByDescending(item => item.BalanceRials)
            .ToArray();

        return new TrustFundSnapshotInfo(
            entries
                .OrderByDescending(item => item.OccurredAt)
                .Select(MapTrustEntry)
                .ToArray(),
            balances);
    }

    public async Task<TrustFundBalanceInfo> GetTrustFundBalanceAsync(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        if (customerId == Guid.Empty)
        {
            throw new ArgumentException("A valid customer is required.", nameof(customerId));
        }

        var entries = (await LoadTrustEntriesAsync(cancellationToken))
            .Where(item => item.CustomerId == customerId)
            .ToArray();

        return new TrustFundBalanceInfo(customerId, CalculateBalance(entries));
    }

    public async Task<TrustFundEntryInfo> AddTrustFundEntryAsync(
        AddTrustFundEntryCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateActor(command.ActorUserId);

        if (command.CustomerId == Guid.Empty || command.AmountRials <= 0)
        {
            throw new ArgumentException("A valid customer and positive amount are required.", nameof(command));
        }

        var entryType = command.EntryType?.Trim() switch
        {
            "Deposit" => "Deposit",
            "Release" => "Release",
            _ => throw new ArgumentException("Only Deposit or Release can be entered manually.", nameof(command)),
        };

        var customerExists = await dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == command.CustomerId, cancellationToken);

        if (!customerExists)
        {
            throw new ApplicationResourceNotFoundException();
        }

        // The balance check for a Release and the entry insert must be one
        // atomic unit so two concurrent releases cannot both pass the balance
        // check and overspend the account.
        await using var transaction =
            await PersistenceUtilities.BeginSerializableTransactionAsync(
                dbContext,
                cancellationToken);

        if (entryType == "Release")
        {
            var currentBalance = CalculateBalance(
                (await LoadTrustEntriesForUpdateAsync(cancellationToken))
                    .Where(item => item.CustomerId == command.CustomerId));

            if (currentBalance < command.AmountRials)
            {
                throw new ApplicationConflictException();
            }
        }

        var occurredAt = command.OccurredAt ?? timeProvider.GetUtcNow();
        var entry = new TrustFundEntryDocument(
            Guid.NewGuid(),
            command.CustomerId,
            OrderId: null,
            entryType,
            command.AmountRials,
            occurredAt,
            NormalizeOptional(command.Reference, 200));

        dbContext.SystemSettings.Add(NewSetting(
            $"{TrustPrefix}{entry.Id:N}",
            TrustDataType,
            entry));

        await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken);
        await PersistenceUtilities.CommitAsync(transaction, cancellationToken);
        return MapTrustEntry(entry);
    }

    public async Task<TrustFundAllocationInfo> AllocateTrustFundAsync(
        AllocateTrustFundCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateActor(command.ActorUserId);

        if (command.OrderId == Guid.Empty)
        {
            throw new ArgumentException("A valid order is required.", nameof(command));
        }

        // The balance check, the per-order allocation de-duplication, and the
        // payment that spends the balance must be one atomic unit. Without a
        // transaction two concurrent allocations could both read the same
        // balance and overspend it across different orders. The serializable
        // transaction plus the tracked reads serialize concurrent allocations
        // for the same customer; the second one re-reads after the first
        // commits and conflicts.
        await using var transaction =
            await PersistenceUtilities.BeginSerializableTransactionAsync(
                dbContext,
                cancellationToken);

        var order = await dbContext.Orders.FindAsync([command.OrderId], cancellationToken)
            ?? throw new ApplicationResourceNotFoundException();

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new ApplicationConflictException();
        }

        var entries = await LoadTrustEntriesForUpdateAsync(cancellationToken);

        var existingAllocation = entries.FirstOrDefault(item =>
            item.EntryType == "Allocation" &&
            item.OrderId == order.Id);

        if (existingAllocation is not null)
        {
            throw new ApplicationConflictException();
        }

        var customerEntries = entries
            .Where(item => item.CustomerId == order.CustomerId)
            .ToArray();

        var balance = CalculateBalance(customerEntries);
        if (balance < order.GrandTotalRials)
        {
            throw new ApplicationConflictException();
        }

        var now = timeProvider.GetUtcNow();
        var reference = NormalizeOptional(command.Reference, 200);
        var allocation = new TrustFundEntryDocument(
            Guid.NewGuid(),
            order.CustomerId,
            order.Id,
            "Allocation",
            order.GrandTotalRials,
            now,
            reference);

        dbContext.SystemSettings.Add(NewSetting(
            $"{TrustPrefix}{allocation.Id:N}",
            TrustDataType,
            allocation));

        // This official full payment creates the verified Payment, moves the
        // order to Paid, confirms inventory and issues the invoice.
        var payment = await paymentService.RecordManualPaymentAsync(
            new RecordManualPaymentCommand(
                command.ActorUserId,
                order.Id,
                PaymentMethod.Cash,
                BuildSettlementReference(
                    "TRUSTFUND",
                    allocation.Id,
                    reference),
                $"trust-fund-order-{order.Id:N}"),
            cancellationToken);

        return new TrustFundAllocationInfo(
            allocation.Id,
            order.CustomerId,
            order.Id,
            order.GrandTotalRials,
            checked(balance - order.GrandTotalRials),
            payment.Id,
            payment.InvoiceId);
    }

    private async Task<InstallmentPlanInfo> MapPlanAsync(
        InstallmentPlanDocument plan,
        IReadOnlyCollection<InstallmentLineDocument> lines,
        CancellationToken cancellationToken)
    {
        var payment =
            await FindInstallmentSettlementAsync(
                plan,
                cancellationToken);

        Guid? invoiceId = null;
        if (payment is not null)
        {
            invoiceId = await FindInvoiceIdAsync(
                payment.Id,
                cancellationToken);
        }

        return new InstallmentPlanInfo(
            plan.Id,
            plan.OrderId,
            plan.CustomerId,
            plan.OrderNumber,
            plan.CustomerName,
            plan.TotalAmountRials,
            plan.CreatedAt,
            lines
                .OrderBy(item => item.Sequence)
                .Select(MapLine)
                .ToArray(),
            payment?.Id,
            invoiceId);
    }

    private Task<Payment?> FindInstallmentSettlementAsync(
        InstallmentPlanDocument plan,
        CancellationToken cancellationToken)
    {
        var prefix = $"INSTALLMENT-{plan.Id:N}";

        return dbContext.Payments
            .AsNoTracking()
            .Where(item =>
                item.OrderId == plan.OrderId &&
                item.Status == PaymentStatus.Verified &&
                item.Provider == "MANUAL" &&
                item.GatewayPaymentId != null &&
                item.GatewayPaymentId.StartsWith(prefix))
            .OrderByDescending(item => item.VerifiedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private Task<Guid?> FindInvoiceIdAsync(
        Guid paymentId,
        CancellationToken cancellationToken) =>
        dbContext.Invoices
            .AsNoTracking()
            .Where(invoice =>
                invoice.PaymentId == paymentId)
            .Select(invoice =>
                (Guid?)invoice.Id)
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<List<InstallmentPlanDocument>> LoadPlanDocumentsAsync(
        CancellationToken cancellationToken)
    {
        var settings = await dbContext.SystemSettings
            .AsNoTracking()
            .Where(setting => setting.Key.StartsWith(PlanPrefix))
            .ToListAsync(cancellationToken);

        return settings
            .Select(setting => Deserialize<InstallmentPlanDocument>(setting, PlanDataType))
            .Where(item => item is not null)
            .Cast<InstallmentPlanDocument>()
            .ToList();
    }

    private async Task<List<InstallmentLineDocument>> LoadLineDocumentsAsync(
        bool asTracking,
        CancellationToken cancellationToken)
    {
        var query = dbContext.SystemSettings
            .Where(setting => setting.Key.StartsWith(LinePrefix));

        var settings = asTracking
            ? await query.ToListAsync(cancellationToken)
            : await query.AsNoTracking().ToListAsync(cancellationToken);

        return settings
            .Select(setting => Deserialize<InstallmentLineDocument>(setting, LineDataType))
            .Where(item => item is not null)
            .Cast<InstallmentLineDocument>()
            .ToList();
    }

    private async Task<List<TrustFundEntryDocument>> LoadTrustEntriesForUpdateAsync(
        CancellationToken cancellationToken)
    {
        // Tracked read: when the caller has opened a serializable transaction
        // the range predicate below takes range locks, which serializes
        // concurrent allocations or releases for the same customer.
        var settings = await dbContext.SystemSettings
            .Where(setting => setting.Key.StartsWith(TrustPrefix))
            .ToListAsync(cancellationToken);

        return settings
            .Select(setting => Deserialize<TrustFundEntryDocument>(setting, TrustDataType))
            .Where(item => item is not null)
            .Cast<TrustFundEntryDocument>()
            .ToList();
    }

    private async Task<List<TrustFundEntryDocument>> LoadTrustEntriesAsync(
        CancellationToken cancellationToken)
    {
        var settings = await dbContext.SystemSettings
            .AsNoTracking()
            .Where(setting => setting.Key.StartsWith(TrustPrefix))
            .ToListAsync(cancellationToken);

        return settings
            .Select(setting => Deserialize<TrustFundEntryDocument>(setting, TrustDataType))
            .Where(item => item is not null)
            .Cast<TrustFundEntryDocument>()
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

    private static T? Deserialize<T>(SystemSetting setting, string expectedDataType)
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

    private static long CalculateBalance(IEnumerable<TrustFundEntryDocument> entries)
    {
        long balance = 0;
        foreach (var entry in entries.OrderBy(item => item.OccurredAt))
        {
            balance = entry.EntryType switch
            {
                "Deposit" => checked(balance + entry.AmountRials),
                "Release" or "Allocation" => checked(balance - entry.AmountRials),
                _ => balance,
            };
        }

        return balance;
    }

    private static InstallmentLineInfo MapLine(InstallmentLineDocument line) =>
        new(
            line.Id,
            line.Sequence,
            line.DueOn,
            line.AmountRials,
            line.PaidAt,
            line.Reference);

    private static TrustFundEntryInfo MapTrustEntry(TrustFundEntryDocument entry) =>
        new(
            entry.Id,
            entry.CustomerId,
            entry.OrderId,
            entry.EntryType,
            entry.AmountRials,
            entry.OccurredAt,
            entry.Reference);

    private static string BuildSettlementReference(
        string prefix,
        Guid id,
        string? userReference)
    {
        var head = $"{prefix}-{id:N}";

        if (string.IsNullOrWhiteSpace(userReference))
        {
            return head;
        }

        var suffix = userReference.Trim();
        var maximumSuffixLength = Math.Max(
            0,
            200 - head.Length - 1);

        if (suffix.Length > maximumSuffixLength)
        {
            suffix = suffix[..maximumSuffixLength];
        }

        return suffix.Length == 0
            ? head
            : $"{head}:{suffix}";
    }

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        return normalized;
    }

    private static void ValidateActor(Guid actorUserId)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("A valid actor is required.", nameof(actorUserId));
        }
    }

    private sealed record InstallmentPlanDocument(
        Guid Id,
        Guid OrderId,
        Guid CustomerId,
        string OrderNumber,
        string CustomerName,
        long TotalAmountRials,
        DateTimeOffset CreatedAt);

    private sealed record InstallmentLineDocument(
        Guid Id,
        Guid PlanId,
        Guid OrderId,
        int Sequence,
        DateOnly DueOn,
        long AmountRials,
        DateTimeOffset? PaidAt,
        string? Reference);

    private sealed record TrustFundEntryDocument(
        Guid Id,
        Guid CustomerId,
        Guid? OrderId,
        string EntryType,
        long AmountRials,
        DateTimeOffset OccurredAt,
        string? Reference);

    private sealed record TrackedInstallment(
        SystemSetting Setting,
        InstallmentLineDocument Document);
}
