using System.Text.Json;
using GoldInvoice.Application.Common;
using GoldInvoice.Application.Invoicing;
using GoldInvoice.Application.Integration;
using GoldInvoice.Application.Orders;
using GoldInvoice.Application.Security;
using GoldInvoice.Domain.Invoicing;
using GoldInvoice.Domain.Orders;
using GoldInvoice.Domain.Payments;
using GoldInvoice.Domain.Platform;
using GoldInvoice.Infrastructure.Configuration;
using GoldInvoice.Infrastructure.Integration;
using GoldInvoice.Infrastructure.Orders;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GoldInvoice.Infrastructure.Invoicing;

internal sealed class InvoiceService(
    GoldInvoiceDbContext dbContext,
    IOptions<InvoicingOptions> options,
    IOutboxWriter outboxWriter,
    TimeProvider timeProvider) : IInvoiceService, IInvoiceIssuanceService
{
    private const int MaximumPageSize = 100;

    public async Task<PagedResult<InvoiceInfo>> GetInvoicesAsync(
        Guid actorUserId,
        bool canReadAll,
        int page,
        int pageSize,
        InvoiceStatus? status,
        CancellationToken cancellationToken)
    {
        ValidateActor(actorUserId);
        ValidatePage(page, pageSize);
        var query = dbContext.Invoices.AsNoTracking();
        if (!canReadAll)
        {
            query = query.Where(invoice => invoice.CustomerId == actorUserId);
        }

        if (status is not null)
        {
            query = query.Where(invoice => invoice.Status == status);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var invoices = await query
            .OrderByDescending(invoice => invoice.IssuedAt)
            .ThenByDescending(invoice => invoice.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<InvoiceInfo>(
            await MapInvoicesAsync(invoices, cancellationToken),
            page,
            pageSize,
            totalCount);
    }

    public async Task<InvoiceInfo> GetInvoiceAsync(
        Guid invoiceId,
        Guid actorUserId,
        bool canReadAll,
        CancellationToken cancellationToken)
    {
        ValidateActor(actorUserId);
        var invoice = await dbContext.Invoices
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == invoiceId, cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        if (!canReadAll && invoice.CustomerId != actorUserId)
        {
            throw new ApplicationResourceNotFoundException();
        }

        return AssertSingle(await MapInvoicesAsync([invoice], cancellationToken));
    }

    public async Task<InvoiceInfo> VoidInvoiceAsync(
        Guid invoiceId,
        VoidInvoiceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateActor(command.ActorUserId);
        await using var transaction = await PersistenceUtilities.BeginSerializableTransactionAsync(
            dbContext,
            cancellationToken);
        var invoice = await dbContext.Invoices.FindAsync([invoiceId], cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        PersistenceUtilities.SetOriginalRowVersion(dbContext, invoice, command.RowVersion);
        invoice.Void(timeProvider.GetUtcNow(), command.Reason);
        await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken);
        await PersistenceUtilities.CommitAsync(transaction, cancellationToken);
        return await GetInvoiceAsync(invoice.Id, command.ActorUserId, canReadAll: true, cancellationToken);
    }

    public async Task<InvoiceInfo> CorrectDocumentAsync(
        Guid invoiceId,
        CorrectInvoiceDocumentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateActor(command.ActorUserId);
        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Trim().Length > 1000)
        {
            throw new ArgumentException("A bounded correction reason is required.", nameof(command));
        }

        await using var transaction = await PersistenceUtilities.BeginSerializableTransactionAsync(
            dbContext,
            cancellationToken);
        var invoice = await dbContext.Invoices.FindAsync([invoiceId], cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        var address = await dbContext.InvoiceAddressSnapshots.SingleOrDefaultAsync(
            candidate => candidate.InvoiceId == invoice.Id,
            cancellationToken) ?? throw new ApplicationConflictException();
        if (invoice.Status != InvoiceStatus.Issued || invoice.PaymentId is null)
        {
            throw new ApplicationConflictException();
        }

        var paymentIsVerified = await dbContext.Payments.AnyAsync(
            payment => payment.Id == invoice.PaymentId &&
                       payment.OrderId == invoice.OrderId &&
                       payment.Status == PaymentStatus.Verified,
            cancellationToken);
        var orderIsPaid = await dbContext.Orders.AnyAsync(
            order => order.Id == invoice.OrderId &&
                     order.CustomerId == invoice.CustomerId &&
                     (order.Status == OrderStatus.Paid ||
                      order.Status == OrderStatus.Processing ||
                      order.Status == OrderStatus.Completed),
            cancellationToken);
        if (!paymentIsVerified || !orderIsPaid)
        {
            throw new ApplicationConflictException();
        }

        PersistenceUtilities.SetOriginalRowVersion(dbContext, invoice, command.RowVersion);
        var oldValues = JsonSerializer.Serialize(new
        {
            invoice.CustomerNameSnapshot,
            invoice.CustomerNationalIdSnapshot,
            address.RecipientName,
            address.PhoneNumber,
            address.Province,
            address.City,
            address.PostalCode,
            address.AddressLine
        });
        invoice.CorrectCustomerSnapshot(command.CustomerName, command.CustomerNationalId);
        address.Correct(
            command.RecipientName,
            command.PhoneNumber,
            command.Province,
            command.City,
            command.PostalCode,
            command.AddressLine);
        var newValues = JsonSerializer.Serialize(new
        {
            invoice.CustomerNameSnapshot,
            invoice.CustomerNationalIdSnapshot,
            address.RecipientName,
            address.PhoneNumber,
            address.Province,
            address.City,
            address.PostalCode,
            address.AddressLine,
            CorrectionReason = command.Reason
        });
        var audit = new AuditLog(
            "InvoiceDocumentCorrected",
            nameof(Invoice),
            invoice.Id.ToString("N"),
            timeProvider.GetUtcNow());
        audit.SetContext(command.ActorUserId, correlationId: null);
        audit.SetValues(oldValues, newValues);
        dbContext.AuditLogs.Add(audit);

        await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken);
        await PersistenceUtilities.CommitAsync(transaction, cancellationToken);
        return await GetInvoiceAsync(invoice.Id, command.ActorUserId, canReadAll: true, cancellationToken);
    }

    public async Task<InvoicePrintInfo> RequestPrintAsync(
        Guid invoiceId,
        RequestInvoicePrintCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateActor(command.ActorUserId);
        await using var transaction = await PersistenceUtilities.BeginSerializableTransactionAsync(
            dbContext,
            cancellationToken);
        var invoice = await dbContext.Invoices
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == invoiceId, cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        if (invoice.Status != InvoiceStatus.Issued || invoice.PaymentId is null ||
            !await dbContext.Payments.AnyAsync(
                payment => payment.Id == invoice.PaymentId &&
                           payment.OrderId == invoice.OrderId &&
                           payment.Status == PaymentStatus.Verified,
                cancellationToken))
        {
            throw new ApplicationConflictException();
        }

        var requestedAt = timeProvider.GetUtcNow();
        var previousPrints = await dbContext.InvoicePrintLogs
            .Where(log => log.InvoiceId == invoice.Id)
            .ToListAsync(cancellationToken);
        var acknowledgementDeadline = requestedAt.AddMinutes(-5);
        if (previousPrints.Any(log =>
                log.Status == InvoicePrintStatus.Requested &&
                log.CreatedAt >= acknowledgementDeadline))
        {
            throw new ApplicationConflictException();
        }

        foreach (var stalePrint in previousPrints.Where(log =>
                     log.Status == InvoicePrintStatus.Requested))
        {
            stalePrint.MarkFailed(requestedAt, "PRINT_ACK_TIMEOUT");
        }

        var isReprint = previousPrints.Any(log => log.Status == InvoicePrintStatus.Succeeded);
        if (isReprint && !command.CanReprint)
        {
            throw new SecurityAccessDeniedException();
        }

        var printLog = new InvoicePrintLog(
            invoice.Id,
            command.ActorUserId,
            command.Copies,
            isReprint,
            isReprint ? command.ReprintReason : null);
        dbContext.InvoicePrintLogs.Add(printLog);
        await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken);
        await PersistenceUtilities.CommitAsync(transaction, cancellationToken);
        return MapPrint(printLog);
    }

    public async Task<InvoicePrintInfo> CompletePrintAsync(
        Guid invoiceId,
        Guid printJobId,
        CompleteInvoicePrintCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateActor(command.ActorUserId);
        await using var transaction = await PersistenceUtilities.BeginSerializableTransactionAsync(
            dbContext,
            cancellationToken);
        var printLog = await dbContext.InvoicePrintLogs.SingleOrDefaultAsync(
            candidate => candidate.Id == printJobId && candidate.InvoiceId == invoiceId,
            cancellationToken) ?? throw new ApplicationResourceNotFoundException();
        if (printLog.RequestedByUserId != command.ActorUserId)
        {
            throw new SecurityAccessDeniedException();
        }

        PersistenceUtilities.SetOriginalRowVersion(dbContext, printLog, command.RowVersion);
        if (command.Succeeded)
        {
            printLog.MarkSucceeded(timeProvider.GetUtcNow(), command.PrinterName);
        }
        else
        {
            printLog.MarkFailed(timeProvider.GetUtcNow(), command.FailureCode ?? string.Empty);
        }

        await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken);
        await PersistenceUtilities.CommitAsync(transaction, cancellationToken);
        return MapPrint(printLog);
    }

    public async Task<InvoiceInfo> IssueForPaidOrderAsync(
        Guid orderId,
        Guid paymentId,
        DateTimeOffset issuedAt,
        CancellationToken cancellationToken)
    {
        if (orderId == Guid.Empty || paymentId == Guid.Empty || issuedAt == default)
        {
            throw new ArgumentException("A valid paid order, payment, and issue time are required.");
        }

        await using var transaction = await PersistenceUtilities.BeginSerializableTransactionAsync(
            dbContext,
            cancellationToken);
        var existingMatches = await dbContext.Invoices
            .AsNoTracking()
            .Where(invoice => invoice.OrderId == orderId || invoice.PaymentId == paymentId)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (existingMatches.Count > 1)
        {
            throw new ApplicationConflictException();
        }

        var existing = existingMatches.SingleOrDefault();
        if (existing is not null)
        {
            if (existing.OrderId != orderId || existing.PaymentId != paymentId)
            {
                throw new ApplicationConflictException();
            }

            await PersistenceUtilities.CommitAsync(transaction, cancellationToken);
            return AssertSingle(await MapInvoicesAsync([existing], cancellationToken));
        }

        var order = await dbContext.Orders.FindAsync([orderId], cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        var payment = await dbContext.Payments.FindAsync([paymentId], cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        if (order.Status != OrderStatus.Paid ||
            payment.Status != PaymentStatus.Verified ||
            payment.OrderId != order.Id ||
            payment.AmountRials != order.GrandTotalRials)
        {
            throw new ApplicationConflictException();
        }

        var orderItems = await dbContext.OrderItems
            .AsNoTracking()
            .Where(item => item.OrderId == order.Id)
            .OrderBy(item => item.LineNumber)
            .ToListAsync(cancellationToken);
        var address = await dbContext.OrderAddressSnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(snapshot => snapshot.OrderId == order.Id, cancellationToken);
        var store = await dbContext.OrderStoreSnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(snapshot => snapshot.OrderId == order.Id, cancellationToken);
        long reconstructedSubtotal;
        try
        {
            reconstructedSubtotal = orderItems.Sum(item => item.LineTotalRials);
        }
        catch (OverflowException)
        {
            throw new ApplicationConflictException();
        }

        if (orderItems.Count == 0 ||
            reconstructedSubtotal != order.ItemsSubtotalRials ||
            address is null || store is null ||
            orderItems.Any(item =>
                item.PriceCalculationSnapshotId is null ||
                item.InventoryItemId is null ||
                item.NetGoldWeightGrams is null ||
                item.Karat is null ||
                item.MarketUnitPriceRials is null ||
                item.GoldValueRials is null ||
                item.WageRials is null ||
                item.ProfitRials is null ||
                item.TaxRials is null ||
                string.IsNullOrWhiteSpace(item.RoundingPolicy)))
        {
            throw new ApplicationConflictException();
        }

        var settings = options.Value;
        var series = settings.SequenceSeries.ToUpperInvariant();
        var prefix = settings.SequencePrefix.ToUpperInvariant();
        var sequence = await dbContext.InvoiceSequences.SingleOrDefaultAsync(
            candidate => candidate.Series == series,
            cancellationToken);
        if (sequence is null)
        {
            sequence = new InvoiceSequence(series, prefix);
            dbContext.InvoiceSequences.Add(sequence);
        }
        else if (!string.Equals(sequence.Prefix, prefix, StringComparison.Ordinal))
        {
            throw new ApplicationConflictException();
        }

        var invoice = new Invoice(
            order.Id,
            order.CustomerId,
            sequence.AllocateNext(issuedAt),
            issuedAt,
            order.ItemsSubtotalRials,
            order.DiscountRials,
            order.ShippingRials,
            payment.Id,
            order.CustomerNameSnapshot,
            order.CustomerNationalIdSnapshot);
        dbContext.Invoices.Add(invoice);
        foreach (var orderItem in orderItems)
        {
            dbContext.InvoiceItems.Add(new InvoiceItem(
                invoice.Id,
                orderItem.LineNumber,
                orderItem.Sku,
                orderItem.ProductName,
                orderItem.VariantName,
                orderItem.WeightGrams,
                orderItem.Purity,
                orderItem.UnitPriceRials,
                orderItem.Quantity,
                orderItem.Id,
                orderItem.PriceCalculationSnapshotId,
                orderItem.InventoryUnitId,
                orderItem.NetGoldWeightGrams,
                orderItem.Karat,
                orderItem.MarketUnitPriceRials,
                orderItem.GoldValueRials,
                orderItem.WageRials,
                orderItem.ProfitRials,
                orderItem.TaxRials,
                orderItem.RoundingPolicy,
                orderItem.AcquisitionUnitCostRials));
        }

        dbContext.InvoiceAddressSnapshots.Add(new InvoiceAddressSnapshot(
            invoice.Id,
            address.Id,
            address.RecipientName,
            address.PhoneNumber,
            address.Province,
            address.City,
            address.PostalCode,
            address.AddressLine));
        dbContext.InvoiceStoreSnapshots.Add(new InvoiceStoreSnapshot(
            invoice.Id,
            store.Id,
            store.TradeName,
            store.LegalName,
            store.NationalId,
            store.EconomicCode,
            store.RegistrationNumber,
            store.PhoneNumber,
            store.PostalCode,
            store.AddressLine));
        outboxWriter.AddInvoiceCreated(invoice, issuedAt);
        await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken);
        await PersistenceUtilities.CommitAsync(transaction, cancellationToken);
        return AssertSingle(await MapInvoicesAsync([invoice], cancellationToken));
    }

    private async Task<IReadOnlyList<InvoiceInfo>> MapInvoicesAsync(
        IReadOnlyList<Invoice> invoices,
        CancellationToken cancellationToken)
    {
        if (invoices.Count == 0)
        {
            return [];
        }

        var invoiceIds = invoices.Select(invoice => invoice.Id).ToArray();
        var items = await dbContext.InvoiceItems
            .AsNoTracking()
            .Where(item => invoiceIds.Contains(item.InvoiceId))
            .OrderBy(item => item.LineNumber)
            .ToListAsync(cancellationToken);
        var addresses = await dbContext.InvoiceAddressSnapshots
            .AsNoTracking()
            .Where(address => invoiceIds.Contains(address.InvoiceId))
            .ToDictionaryAsync(address => address.InvoiceId, cancellationToken);
        var stores = await dbContext.InvoiceStoreSnapshots
            .AsNoTracking()
            .Where(store => invoiceIds.Contains(store.InvoiceId))
            .ToDictionaryAsync(store => store.InvoiceId, cancellationToken);
        var itemGroups = items.ToLookup(item => item.InvoiceId);

        return invoices.Select(invoice => new InvoiceInfo(
            invoice.Id,
            invoice.OrderId,
            invoice.CustomerId,
            invoice.PaymentId,
            invoice.InvoiceNumber,
            invoice.Status,
            invoice.IssuedAt,
            invoice.SubtotalRials,
            invoice.DiscountRials,
            invoice.ShippingRials,
            invoice.GrandTotalRials,
            invoice.CustomerNameSnapshot,
            invoice.CustomerNationalIdSnapshot,
            invoice.VoidedAt,
            invoice.VoidReason,
            addresses.TryGetValue(invoice.Id, out var address) ? MapAddress(address) : null,
            stores.TryGetValue(invoice.Id, out var store) ? MapStore(store) : null,
            itemGroups[invoice.Id].Select(MapItem).ToArray(),
            Convert.ToBase64String(invoice.RowVersion))).ToArray();
    }

    private static InvoiceItemInfo MapItem(InvoiceItem item) => new(
        item.Id,
        item.OrderItemId,
        item.PriceCalculationSnapshotId,
        item.InventoryUnitId,
        item.LineNumber,
        item.Sku,
        item.ProductName,
        item.VariantName,
        item.WeightGrams,
        item.NetGoldWeightGrams,
        item.Karat,
        item.Quantity,
        item.MarketUnitPriceRials,
        item.GoldValueRials,
        item.WageRials,
        item.ProfitRials,
        item.TaxRials,
        item.UnitPriceRials,
        item.LineTotalRials,
        item.AcquisitionUnitCostRials,
        item.AcquisitionTotalCostRials,
        item.GrossProfitRials,
        item.RoundingPolicy);

    private static InvoiceAddressSnapshotInfo MapAddress(InvoiceAddressSnapshot address) => new(
        address.Id,
        address.OrderAddressSnapshotId,
        address.RecipientName,
        address.PhoneNumber,
        address.Province,
        address.City,
        address.PostalCode,
        address.AddressLine);

    private static StoreIdentitySnapshotInfo MapStore(InvoiceStoreSnapshot store) => new(
        store.Id,
        store.TradeName,
        store.LegalName,
        store.NationalId,
        store.EconomicCode,
        store.RegistrationNumber,
        store.PhoneNumber,
        store.PostalCode,
        store.AddressLine);

    private static InvoicePrintInfo MapPrint(InvoicePrintLog log) => new(
        log.Id,
        log.InvoiceId,
        log.RequestedByUserId,
        log.Status,
        log.Copies,
        log.IsReprint,
        log.ReprintReason,
        log.PrinterName,
        log.CompletedAt,
        log.FailureCode,
        log.CreatedAt,
        Convert.ToBase64String(log.RowVersion));

    private static void ValidateActor(Guid actorUserId)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("A valid actor identifier is required.", nameof(actorUserId));
        }
    }

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1 ||
            pageSize is < 1 or > MaximumPageSize ||
            ((long)page - 1) * pageSize > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(page));
        }
    }

    private static T AssertSingle<T>(IReadOnlyList<T> values) =>
        values.Count == 1 ? values[0] : throw new InvalidOperationException("Expected one mapped row.");
}
