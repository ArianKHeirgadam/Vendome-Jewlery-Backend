using GoldInvoice.Application.Common;
using GoldInvoice.Application.Invoicing;
using GoldInvoice.Application.Integration;
using GoldInvoice.Application.Payments;
using GoldInvoice.Domain.Orders;
using GoldInvoice.Domain.Payments;
using GoldInvoice.Infrastructure.Configuration;
using GoldInvoice.Infrastructure.Inventory;
using GoldInvoice.Infrastructure.Integration;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GoldInvoice.Infrastructure.Payments;

internal sealed class PaymentService(
    GoldInvoiceDbContext dbContext,
    IEnumerable<IPaymentGatewayProvider> providers,
    InventoryReservationCoordinator reservationCoordinator,
    IInvoiceIssuanceService invoiceIssuanceService,
    IOutboxWriter outboxWriter,
    IOptions<PaymentProcessingOptions> options,
    TimeProvider timeProvider,
    ILogger<PaymentService> logger) : IPaymentService
{
    private readonly IReadOnlyList<IPaymentGatewayProvider> registeredProviders = providers.ToArray();

    public async Task<IReadOnlyList<PaymentGatewayInfo>> GetGatewaysAsync(
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var query = dbContext.PaymentGateways.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(gateway => gateway.IsActive);
        }

        return (await query.OrderBy(gateway => gateway.DisplayName).ToListAsync(cancellationToken))
            .Select(MapGateway)
            .ToArray();
    }

    public async Task<PaymentGatewayInfo> CreateGatewayAsync(
        CreatePaymentGatewayCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var gateway = new PaymentGateway(
            command.Code,
            command.DisplayName,
            command.ProviderCode,
            command.ConfigurationReference);
        await using var transaction = await PersistenceUtilities.BeginSerializableTransactionAsync(
            dbContext,
            cancellationToken);
        var activeConfigurationCount = await dbContext.PaymentGateways.CountAsync(
            candidate => candidate.ProviderCode == gateway.ProviderCode && candidate.IsActive,
            cancellationToken);
        if (activeConfigurationCount >= options.Value.MaximumGatewayConfigurationsPerProvider)
        {
            throw new ApplicationConflictException();
        }

        dbContext.PaymentGateways.Add(gateway);
        await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken);
        await PersistenceUtilities.CommitAsync(transaction, cancellationToken);
        return MapGateway(gateway);
    }

    public async Task<PaymentGatewayInfo> UpdateGatewayAsync(
        Guid gatewayId,
        UpdatePaymentGatewayCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.ProviderCode) ||
            string.IsNullOrWhiteSpace(command.ConfigurationReference))
        {
            throw new ArgumentException("Provider and configuration references are required.", nameof(command));
        }

        await using var transaction = await PersistenceUtilities.BeginSerializableTransactionAsync(
            dbContext,
            cancellationToken);
        var gateway = await dbContext.PaymentGateways.FindAsync([gatewayId], cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        PersistenceUtilities.SetOriginalRowVersion(dbContext, gateway, command.RowVersion);
        var hasInFlightPayments = await dbContext.Payments.AnyAsync(
            payment => payment.PaymentGatewayId == gateway.Id &&
                (payment.Status == PaymentStatus.Pending ||
                 payment.Status == PaymentStatus.Processing ||
                 payment.Status == PaymentStatus.RequiresReview),
            cancellationToken);
        if (hasInFlightPayments &&
            (!command.IsActive ||
             !string.Equals(gateway.ProviderCode, command.ProviderCode.Trim(), StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(
                 gateway.ConfigurationReference,
                 command.ConfigurationReference.Trim(),
                 StringComparison.Ordinal)))
        {
            throw new ApplicationConflictException();
        }

        var targetProviderCode = command.ProviderCode.Trim().ToUpperInvariant();
        if (command.IsActive)
        {
            var activeConfigurationCount = await dbContext.PaymentGateways.CountAsync(
                candidate => candidate.Id != gateway.Id &&
                    candidate.ProviderCode == targetProviderCode &&
                    candidate.IsActive,
                cancellationToken);
            if (activeConfigurationCount >= options.Value.MaximumGatewayConfigurationsPerProvider)
            {
                throw new ApplicationConflictException();
            }
        }

        gateway.Update(
            command.DisplayName,
            command.ProviderCode,
            command.ConfigurationReference,
            command.IsActive);
        await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken);
        await PersistenceUtilities.CommitAsync(transaction, cancellationToken);
        return MapGateway(gateway);
    }

    public async Task<PaymentInfo> GetPaymentAsync(
        Guid paymentId,
        Guid actorUserId,
        bool canReadAll,
        CancellationToken cancellationToken)
    {
        ValidateActor(actorUserId);
        var payment = await dbContext.Payments
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == paymentId, cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        var customerId = await dbContext.Orders
            .Where(order => order.Id == payment.OrderId)
            .Select(order => (Guid?)order.CustomerId)
            .SingleOrDefaultAsync(cancellationToken) ?? throw new ApplicationResourceNotFoundException();
        if (!canReadAll && customerId != actorUserId)
        {
            throw new ApplicationResourceNotFoundException();
        }

        return await MapPaymentAsync(payment, cancellationToken);
    }

    public async Task<PaymentInitiationInfo> InitiateAsync(
        InitiatePaymentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateActor(command.ActorUserId);
        if (command.OrderId == Guid.Empty)
        {
            throw new ArgumentException("A valid order is required.", nameof(command));
        }

        var normalizedKey = PersistenceUtilities.NormalizeIdempotencyKey(command.IdempotencyKey);
        var normalizedGatewayCode = NormalizeGatewayCode(command.GatewayCode);
        var keyHash = PersistenceUtilities.Hash(
            $"Payments.Online:{command.ActorUserId:N}:{normalizedKey}");
        Payment payment;
        PaymentAttempt attempt;
        PaymentGateway gateway;
        IPaymentGatewayProvider provider;
        Order order;

        await using (var transaction = await PersistenceUtilities.BeginSerializableTransactionAsync(
                         dbContext,
                         cancellationToken))
        {
            var existing = await dbContext.Payments
                .SingleOrDefaultAsync(candidate => candidate.IdempotencyKeyHash == keyHash, cancellationToken);
            if (existing is not null)
            {
                if (existing.OrderId != command.OrderId ||
                    existing.Method != PaymentMethod.OnlineGateway)
                {
                    throw new ApplicationConflictException();
                }

                gateway = existing.PaymentGatewayId is null
                    ? throw new ApplicationConflictException()
                    : await dbContext.PaymentGateways.SingleOrDefaultAsync(
                        candidateGateway => candidateGateway.Id == existing.PaymentGatewayId &&
                            candidateGateway.Code == normalizedGatewayCode,
                        cancellationToken) ?? throw new ApplicationConflictException();
                attempt = await dbContext.PaymentAttempts
                    .Where(candidate => candidate.PaymentId == existing.Id)
                    .OrderByDescending(candidate => candidate.AttemptNumber)
                    .FirstOrDefaultAsync(cancellationToken) ?? throw new ApplicationConflictException();
                if (attempt.RedirectUrl is not null)
                {
                    await PersistenceUtilities.CommitAsync(transaction, cancellationToken);
                    return new PaymentInitiationInfo(
                        await MapPaymentAsync(existing, cancellationToken),
                        attempt.RedirectUrl);
                }

                if (!gateway.IsActive ||
                    !string.Equals(existing.Provider, gateway.ProviderCode, StringComparison.OrdinalIgnoreCase) ||
                    existing.Status != PaymentStatus.Pending ||
                    attempt.Status != PaymentAttemptStatus.Started)
                {
                    throw new ApplicationConflictException();
                }

                provider = GetProvider(gateway.ProviderCode);
                order = await dbContext.Orders.FindAsync([existing.OrderId], cancellationToken) ??
                    throw new ApplicationResourceNotFoundException();
                EnsureOrderAccess(order, command.ActorUserId, command.CanManagePayments);
                if (order.Status != OrderStatus.AwaitingPayment)
                {
                    throw new ApplicationConflictException();
                }

                await reservationCoordinator.EnsurePayableAsync(order.Id, cancellationToken);
                payment = existing;
                await PersistenceUtilities.CommitAsync(transaction, cancellationToken);
            }
            else
            {
                order = await dbContext.Orders.FindAsync([command.OrderId], cancellationToken) ??
                    throw new ApplicationResourceNotFoundException();
                EnsureOrderAccess(order, command.ActorUserId, command.CanManagePayments);
                if (order.Status != OrderStatus.AwaitingPayment)
                {
                    throw new ApplicationConflictException();
                }

                await reservationCoordinator.EnsurePayableAsync(order.Id, cancellationToken);
                var hasActivePayment = await dbContext.Payments.AnyAsync(
                    candidate => candidate.OrderId == order.Id &&
                        (candidate.Status == PaymentStatus.Pending ||
                         candidate.Status == PaymentStatus.Processing ||
                         candidate.Status == PaymentStatus.RequiresReview ||
                         candidate.Status == PaymentStatus.Verified),
                    cancellationToken);
                if (hasActivePayment)
                {
                    throw new ApplicationConflictException();
                }

                gateway = await dbContext.PaymentGateways.SingleOrDefaultAsync(
                    candidate => candidate.Code == normalizedGatewayCode && candidate.IsActive,
                    cancellationToken) ?? throw new ApplicationResourceNotFoundException();
                provider = GetProvider(gateway.ProviderCode);
                payment = new Payment(
                    order.Id,
                    gateway.ProviderCode,
                    order.GrandTotalRials,
                    PaymentMethod.OnlineGateway,
                    gateway.Id,
                    keyHash);
                attempt = new PaymentAttempt(payment.Id, 1, payment.AmountRials, timeProvider.GetUtcNow());
                dbContext.AddRange(payment, attempt);
                await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken);
                await PersistenceUtilities.CommitAsync(transaction, cancellationToken);
            }
        }

        PaymentGatewayInitiationResult initiation;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.ProviderTimeoutSeconds));
            initiation = await provider.InitiateAsync(
                new PaymentGatewayInitiationRequest(
                    payment.Id,
                    order.Id,
                    order.OrderNumber,
                    payment.AmountRials,
                    order.CustomerNameSnapshot ?? string.Empty,
                    gateway.ConfigurationReference),
                timeout.Token);
            ValidateInitiationResult(initiation);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await MarkInitiationFailedAsync(payment, attempt, "PROVIDER_TIMEOUT", CancellationToken.None);
            throw new ApplicationConflictException();
        }
        catch (OperationCanceledException)
        {
            await MarkInitiationFailedAsync(payment, attempt, "REQUEST_CANCELLED", CancellationToken.None);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                "Payment provider {ProviderCode} failed to initiate payment {PaymentId} with {ExceptionType}",
                gateway.ProviderCode,
                payment.Id,
                exception.GetType().Name);
            await MarkInitiationFailedAsync(
                payment,
                attempt,
                "PROVIDER_INITIATION_FAILED",
                CancellationToken.None);
            throw new ApplicationConflictException();
        }

        await using (var transaction = await PersistenceUtilities.BeginSerializableTransactionAsync(
                         dbContext,
                         CancellationToken.None))
        {
            payment.BeginProcessing(initiation.Authority);
            attempt.MarkRedirected(
                initiation.ProviderRequestId,
                initiation.RedirectUrl,
                initiation.MaskedMetadataJson);
            await PersistenceUtilities.SaveChangesAsync(dbContext, CancellationToken.None);
            await PersistenceUtilities.CommitAsync(transaction, CancellationToken.None);
        }

        return new PaymentInitiationInfo(
            await MapPaymentAsync(payment, cancellationToken),
            initiation.RedirectUrl);
    }

    public async Task<PaymentInfo> RecordManualPaymentAsync(
        RecordManualPaymentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateActor(command.ActorUserId);
        if (command.OrderId == Guid.Empty || command.Method == PaymentMethod.OnlineGateway)
        {
            throw new ArgumentException("A manual payment method and valid order are required.", nameof(command));
        }

        var normalizedKey = PersistenceUtilities.NormalizeIdempotencyKey(command.IdempotencyKey);
        var keyHash = PersistenceUtilities.Hash(
            $"Payments.Manual:{command.ActorUserId:N}:{normalizedKey}");
        await using var transaction = await PersistenceUtilities.BeginSerializableTransactionAsync(
            dbContext,
            cancellationToken);
        var existing = await dbContext.Payments
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.IdempotencyKeyHash == keyHash, cancellationToken);
        if (existing is not null)
        {
            if (existing.OrderId != command.OrderId || existing.Method != command.Method)
            {
                throw new ApplicationConflictException();
            }

            await PersistenceUtilities.CommitAsync(transaction, cancellationToken);
            return await MapPaymentAsync(existing, cancellationToken);
        }

        var order = await dbContext.Orders.FindAsync([command.OrderId], cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new ApplicationConflictException();
        }

        await reservationCoordinator.EnsurePayableAsync(order.Id, cancellationToken);
        var otherPayments = await dbContext.Payments
            .Where(candidate => candidate.OrderId == order.Id &&
                (candidate.Status == PaymentStatus.Pending ||
                 candidate.Status == PaymentStatus.Processing ||
                 candidate.Status == PaymentStatus.RequiresReview ||
                 candidate.Status == PaymentStatus.Verified))
            .ToListAsync(cancellationToken);
        if (otherPayments.Count > 0)
        {
            throw new ApplicationConflictException();
        }

        var now = timeProvider.GetUtcNow();
        var payment = new Payment(
            order.Id,
            "MANUAL",
            order.GrandTotalRials,
            command.Method,
            paymentGatewayId: null,
            idempotencyKeyHash: keyHash);
        payment.Verify(
            string.IsNullOrWhiteSpace(command.Reference)
                ? $"MANUAL-{payment.Id:N}"
                : command.Reference,
            now);
        dbContext.Payments.Add(payment);
        var fromStatus = order.Status;
        order.MarkPaid(now);
        dbContext.OrderStatusHistory.Add(new OrderStatusHistory(
            order.Id,
            fromStatus,
            OrderStatus.Paid,
            now,
            command.ActorUserId,
            "Manual payment verified"));
        outboxWriter.AddOrderStatusChanged(order, fromStatus, now);
        await reservationCoordinator.ConfirmForPaymentAsync(
            order.Id,
            payment.Id,
            now,
            cancellationToken);
        await invoiceIssuanceService.IssueForPaidOrderAsync(
            order.Id,
            payment.Id,
            now,
            cancellationToken);
        await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken);
        await PersistenceUtilities.CommitAsync(transaction, cancellationToken);
        return await MapPaymentAsync(payment, cancellationToken);
    }

    public async Task<PaymentCallbackProcessingInfo> ProcessCallbackAsync(
        string providerCode,
        string rawPayload,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        var normalizedProvider = NormalizeProviderCode(providerCode);
        if (string.IsNullOrWhiteSpace(rawPayload) || rawPayload.Length > 64 * 1024)
        {
            throw new ArgumentException("A bounded callback payload is required.", nameof(rawPayload));
        }

        ArgumentNullException.ThrowIfNull(headers);
        var provider = GetProvider(normalizedProvider);
        var gateways = await dbContext.PaymentGateways
            .AsNoTracking()
            .Where(gateway => gateway.ProviderCode == normalizedProvider && gateway.IsActive)
            .OrderBy(gateway => gateway.Code)
            .Take(options.Value.MaximumGatewayConfigurationsPerProvider + 1)
            .ToListAsync(cancellationToken);
        if (gateways.Count == 0 ||
            gateways.Count > options.Value.MaximumGatewayConfigurationsPerProvider)
        {
            throw new ApplicationConflictException();
        }

        VerifiedCallback verified;
        try
        {
            verified = await VerifyCallbackAsync(
                provider,
                gateways,
                rawPayload,
                headers,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ApplicationConflictException();
        }
        catch (Exception exception) when (exception is not OperationCanceledException and
                                          not ApplicationConflictException)
        {
            logger.LogWarning(
                "Payment provider {ProviderCode} failed to verify a callback with {ExceptionType}",
                normalizedProvider,
                exception.GetType().Name);
            throw new ApplicationConflictException();
        }
        var payloadHash = PersistenceUtilities.Hash(rawPayload);
        var externalCallbackId = NormalizeExternalCallbackId(
            verified.Result.ExternalCallbackId,
            payloadHash);
        var now = timeProvider.GetUtcNow();

        await using var transaction = await PersistenceUtilities.BeginSerializableTransactionAsync(
            dbContext,
            cancellationToken);
        var duplicateMatches = await dbContext.PaymentCallbacks
            .AsNoTracking()
            .Where(callback => callback.Provider == normalizedProvider &&
                    (callback.ExternalCallbackId == externalCallbackId ||
                     callback.PayloadHash == payloadHash))
            .Take(2)
            .ToListAsync(cancellationToken);
        if (duplicateMatches.Count > 1)
        {
            throw new ApplicationConflictException();
        }

        var duplicate = duplicateMatches.SingleOrDefault();
        if (duplicate is not null)
        {
            var duplicateInvoiceId = duplicate.PaymentId is null
                ? null
                : await FindInvoiceIdAsync(duplicate.PaymentId.Value, cancellationToken);
            await PersistenceUtilities.CommitAsync(transaction, cancellationToken);
            return new PaymentCallbackProcessingInfo(
                duplicate.Id,
                duplicate.IsVerified,
                IsDuplicate: true,
                duplicate.ProcessingResult ?? "DUPLICATE",
                duplicate.PaymentId,
                duplicateInvoiceId);
        }

        if (verified.Gateway is null || !verified.Result.IsAuthentic)
        {
            var rejected = CreateCallback(
                normalizedProvider,
                externalCallbackId,
                payloadHash,
                now,
                paymentId: null,
                isVerified: false,
                processingResult: "REJECTED_AUTHENTICITY",
                maskedPayloadJson: verified.Result.MaskedPayloadJson);
            await SaveCallbackAsync(rejected, transaction, cancellationToken);
            return MapCallback(rejected, isDuplicate: false, invoiceId: null);
        }

        if (verified.Result.MerchantPaymentId is null)
        {
            var invalidReference = CreateCallback(
                normalizedProvider,
                externalCallbackId,
                payloadHash,
                now,
                paymentId: null,
                isVerified: true,
                processingResult: "REQUIRES_REVIEW_INVALID_REFERENCE",
                maskedPayloadJson: verified.Result.MaskedPayloadJson);
            await SaveCallbackAsync(invalidReference, transaction, cancellationToken);
            return MapCallback(invalidReference, isDuplicate: false, invoiceId: null);
        }

        var payment = await dbContext.Payments.FindAsync(
            [verified.Result.MerchantPaymentId.Value],
            cancellationToken);
        if (payment is null)
        {
            var unknown = CreateCallback(
                normalizedProvider,
                externalCallbackId,
                payloadHash,
                now,
                paymentId: null,
                isVerified: true,
                processingResult: "REQUIRES_REVIEW_UNKNOWN_PAYMENT",
                maskedPayloadJson: verified.Result.MaskedPayloadJson);
            await SaveCallbackAsync(unknown, transaction, cancellationToken);
            return MapCallback(unknown, isDuplicate: false, invoiceId: null);
        }

        var order = await dbContext.Orders.FindAsync([payment.OrderId], cancellationToken) ??
            throw new InvalidOperationException("The payment is missing its order.");
        var existingInvoiceId = await FindInvoiceIdAsync(payment.Id, cancellationToken);
        if (payment.Status == PaymentStatus.Verified && existingInvoiceId is not null)
        {
            var alreadyVerified = CreateCallback(
                normalizedProvider,
                externalCallbackId,
                payloadHash,
                now,
                payment.Id,
                isVerified: true,
                processingResult: "ALREADY_VERIFIED",
                maskedPayloadJson: verified.Result.MaskedPayloadJson);
            await SaveCallbackAsync(alreadyVerified, transaction, cancellationToken);
            return MapCallback(alreadyVerified, isDuplicate: false, existingInvoiceId);
        }

        var mismatchCode = ValidateCallbackMatches(
            payment,
            order,
            verified.Gateway,
            verified.Result);
        if (mismatchCode is not null)
        {
            if (payment.Status is not PaymentStatus.Verified and
                not PaymentStatus.Cancelled and
                not PaymentStatus.Refunded)
            {
                payment.RequireReview(mismatchCode);
            }

            MoveOrderToReview(order, now, mismatchCode);
            var mismatch = CreateCallback(
                normalizedProvider,
                externalCallbackId,
                payloadHash,
                now,
                payment.Id,
                isVerified: true,
                processingResult: mismatchCode,
                maskedPayloadJson: verified.Result.MaskedPayloadJson);
            await SaveCallbackAsync(mismatch, transaction, cancellationToken);
            return MapCallback(mismatch, isDuplicate: false, invoiceId: null);
        }

        var attempt = await dbContext.PaymentAttempts
            .Where(candidate => candidate.PaymentId == payment.Id)
            .OrderByDescending(candidate => candidate.AttemptNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (!verified.Result.IsSuccessful)
        {
            var failureCode = NormalizeFailureCode(verified.Result.FailureCode, "GATEWAY_DECLINED");
            payment.Fail(failureCode, now);
            if (attempt is not null &&
                attempt.Status is PaymentAttemptStatus.Started or PaymentAttemptStatus.Redirected)
            {
                attempt.Fail(failureCode, now);
            }

            var failed = CreateCallback(
                normalizedProvider,
                externalCallbackId,
                payloadHash,
                now,
                payment.Id,
                isVerified: true,
                processingResult: "PAYMENT_FAILED",
                maskedPayloadJson: verified.Result.MaskedPayloadJson);
            await SaveCallbackAsync(failed, transaction, cancellationToken);
            return MapCallback(failed, isDuplicate: false, invoiceId: null);
        }

        try
        {
            await reservationCoordinator.EnsurePayableAsync(order.Id, cancellationToken);
        }
        catch (ApplicationConflictException)
        {
            payment.RequireReview("INVENTORY_RESERVATION_EXPIRED");
            MoveOrderToReview(order, now, "INVENTORY_RESERVATION_EXPIRED");
            var review = CreateCallback(
                normalizedProvider,
                externalCallbackId,
                payloadHash,
                now,
                payment.Id,
                isVerified: true,
                processingResult: "REQUIRES_REVIEW_INVENTORY",
                maskedPayloadJson: verified.Result.MaskedPayloadJson);
            await SaveCallbackAsync(review, transaction, cancellationToken);
            return MapCallback(review, isDuplicate: false, invoiceId: null);
        }

        payment.Verify(verified.Result.GatewayPaymentId!, now);
        if (attempt is not null &&
            attempt.Status is PaymentAttemptStatus.Started or PaymentAttemptStatus.Redirected)
        {
            attempt.Complete(now, verified.Result.MaskedPayloadJson);
        }

        var previousStatus = order.Status;
        order.MarkPaid(now);
        dbContext.OrderStatusHistory.Add(new OrderStatusHistory(
            order.Id,
            previousStatus,
            OrderStatus.Paid,
            now,
            changedBy: null,
            reason: "Verified payment callback"));
        outboxWriter.AddOrderStatusChanged(order, previousStatus, now);
        await reservationCoordinator.ConfirmForPaymentAsync(
            order.Id,
            payment.Id,
            now,
            cancellationToken);
        var accepted = CreateCallback(
            normalizedProvider,
            externalCallbackId,
            payloadHash,
            now,
            payment.Id,
            isVerified: true,
            processingResult: "PAYMENT_VERIFIED",
            maskedPayloadJson: verified.Result.MaskedPayloadJson);
        dbContext.PaymentCallbacks.Add(accepted);
        var invoice = await invoiceIssuanceService.IssueForPaidOrderAsync(
            order.Id,
            payment.Id,
            now,
            cancellationToken);
        await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken);
        await PersistenceUtilities.CommitAsync(transaction, cancellationToken);
        return MapCallback(accepted, isDuplicate: false, invoice.Id);
    }

    private async Task MarkInitiationFailedAsync(
        Payment payment,
        PaymentAttempt attempt,
        string failureCode,
        CancellationToken cancellationToken)
    {
        await using var transaction = await PersistenceUtilities.BeginSerializableTransactionAsync(
            dbContext,
            cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (payment.Status == PaymentStatus.Pending)
        {
            payment.Fail(failureCode, now);
        }

        if (attempt.Status == PaymentAttemptStatus.Started)
        {
            attempt.Fail(failureCode, now);
        }

        await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken);
        await PersistenceUtilities.CommitAsync(transaction, cancellationToken);
    }

    private async Task<VerifiedCallback> VerifyCallbackAsync(
        IPaymentGatewayProvider provider,
        IReadOnlyList<PaymentGateway> gateways,
        string rawPayload,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.ProviderTimeoutSeconds));
        var results = new List<VerifiedCallback>(gateways.Count);
        foreach (var gateway in gateways)
        {
            try
            {
                var result = await provider.VerifyCallbackAsync(
                    new PaymentGatewayCallbackRequest(
                        rawPayload,
                        headers,
                        gateway.ConfigurationReference),
                    timeout.Token);
                results.Add(new VerifiedCallback(gateway, result));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    "Payment provider {ProviderCode} could not verify callback configuration {GatewayCode} with {ExceptionType}",
                    provider.ProviderCode,
                    gateway.Code,
                    exception.GetType().Name);
            }
        }

        var authentic = results.Where(result => result.Result.IsAuthentic).ToArray();
        if (authentic.Length > 1 || (authentic.Length == 0 && results.Count != gateways.Count))
        {
            throw new ApplicationConflictException();
        }

        return authentic.Length == 1
            ? authentic[0]
            : new VerifiedCallback(null, results[0].Result);
    }

    private async Task SaveCallbackAsync(
        PaymentCallback callback,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        dbContext.PaymentCallbacks.Add(callback);
        await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken);
        await PersistenceUtilities.CommitAsync(transaction, cancellationToken);
    }

    private void MoveOrderToReview(Order order, DateTimeOffset occurredAt, string reason)
    {
        if (order.Status is not OrderStatus.AwaitingPayment and not OrderStatus.PaymentReview)
        {
            return;
        }

        var fromStatus = order.Status;
        order.MarkPaymentReview();
        if (fromStatus != order.Status)
        {
            dbContext.OrderStatusHistory.Add(new OrderStatusHistory(
                order.Id,
                fromStatus,
                order.Status,
                occurredAt,
                changedBy: null,
                reason: reason));
            outboxWriter.AddOrderStatusChanged(order, fromStatus, occurredAt);
        }
    }

    private string? ValidateCallbackMatches(
        Payment payment,
        Order order,
        PaymentGateway gateway,
        PaymentGatewayCallbackResult result)
    {
        if (payment.PaymentGatewayId != gateway.Id ||
            payment.Method != PaymentMethod.OnlineGateway ||
            !string.Equals(payment.Provider, gateway.ProviderCode, StringComparison.OrdinalIgnoreCase))
        {
            return "CALLBACK_GATEWAY_MISMATCH";
        }

        if (payment.Status is PaymentStatus.Cancelled or PaymentStatus.Refunded ||
            order.Status is OrderStatus.Cancelled or OrderStatus.Refunded)
        {
            return "CALLBACK_FINAL_STATE_MISMATCH";
        }

        if (payment.Status is not PaymentStatus.Pending and not PaymentStatus.Processing)
        {
            return "CALLBACK_PAYMENT_STATE_MISMATCH";
        }

        if (payment.AmountRials != order.GrandTotalRials ||
            (result.IsSuccessful && result.AmountRials != payment.AmountRials) ||
            (!result.IsSuccessful && result.AmountRials is not null &&
             result.AmountRials != payment.AmountRials))
        {
            return "CALLBACK_AMOUNT_MISMATCH";
        }

        if ((result.IsSuccessful &&
             (string.IsNullOrWhiteSpace(payment.Authority) ||
              string.IsNullOrWhiteSpace(result.Authority) ||
              !string.Equals(result.Authority, payment.Authority, StringComparison.Ordinal))) ||
            (!result.IsSuccessful &&
             !string.IsNullOrWhiteSpace(result.Authority) &&
             !string.Equals(result.Authority, payment.Authority, StringComparison.Ordinal)))
        {
            return "CALLBACK_AUTHORITY_MISMATCH";
        }

        if (result.IsSuccessful && string.IsNullOrWhiteSpace(result.GatewayPaymentId))
        {
            return "CALLBACK_PAYMENT_REFERENCE_MISSING";
        }

        return null;
    }

    private async Task<PaymentInfo> MapPaymentAsync(
        Payment payment,
        CancellationToken cancellationToken)
    {
        var attempts = await dbContext.PaymentAttempts
            .AsNoTracking()
            .Where(attempt => attempt.PaymentId == payment.Id)
            .OrderBy(attempt => attempt.AttemptNumber)
            .ToListAsync(cancellationToken);
        var invoiceId = await FindInvoiceIdAsync(payment.Id, cancellationToken);
        return new PaymentInfo(
            payment.Id,
            payment.OrderId,
            payment.PaymentGatewayId,
            payment.Provider,
            payment.Method,
            payment.Status,
            payment.AmountRials,
            payment.Authority,
            payment.GatewayPaymentId,
            payment.VerifiedAt,
            payment.FailedAt,
            payment.CancelledAt,
            payment.FailureCode,
            invoiceId,
            attempts.Select(attempt => new PaymentAttemptInfo(
                attempt.Id,
                attempt.AttemptNumber,
                attempt.Status,
                attempt.ProviderRequestId,
                attempt.RedirectUrl,
                attempt.StartedAt,
                attempt.CompletedAt,
                attempt.FailureCode)).ToArray(),
            Convert.ToBase64String(payment.RowVersion));
    }

    private Task<Guid?> FindInvoiceIdAsync(Guid paymentId, CancellationToken cancellationToken) =>
        dbContext.Invoices
            .AsNoTracking()
            .Where(invoice => invoice.PaymentId == paymentId)
            .Select(invoice => (Guid?)invoice.Id)
            .SingleOrDefaultAsync(cancellationToken);

    private PaymentGatewayInfo MapGateway(PaymentGateway gateway) => new(
        gateway.Id,
        gateway.Code,
        gateway.DisplayName,
        gateway.ProviderCode,
        gateway.ConfigurationReference,
        gateway.IsActive,
        IsProviderRegistered(gateway.ProviderCode),
        Convert.ToBase64String(gateway.RowVersion));

    private IPaymentGatewayProvider GetProvider(string providerCode)
    {
        var matches = registeredProviders
            .Where(provider => string.Equals(
                provider.ProviderCode,
                providerCode,
                StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new ApplicationResourceNotFoundException();
    }

    private bool IsProviderRegistered(string providerCode) =>
        registeredProviders.Count(provider => string.Equals(
            provider.ProviderCode,
            providerCode,
            StringComparison.OrdinalIgnoreCase)) == 1;

    private static PaymentCallback CreateCallback(
        string provider,
        string externalCallbackId,
        string payloadHash,
        DateTimeOffset receivedAt,
        Guid? paymentId,
        bool isVerified,
        string processingResult,
        string? maskedPayloadJson) => new(
        provider,
        externalCallbackId,
        payloadHash,
        receivedAt,
        paymentId,
        isVerified,
        processingResult,
        maskedPayloadJson);

    private static PaymentCallbackProcessingInfo MapCallback(
        PaymentCallback callback,
        bool isDuplicate,
        Guid? invoiceId) => new(
        callback.Id,
        callback.IsVerified,
        isDuplicate,
        callback.ProcessingResult ?? "UNKNOWN",
        callback.PaymentId,
        invoiceId);

    private static void EnsureOrderAccess(Order order, Guid actorUserId, bool canManagePayments)
    {
        if (!canManagePayments && order.CustomerId != actorUserId)
        {
            throw new ApplicationResourceNotFoundException();
        }
    }

    private static void ValidateInitiationResult(PaymentGatewayInitiationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (string.IsNullOrWhiteSpace(result.Authority) || result.Authority.Length > 200 ||
            string.IsNullOrWhiteSpace(result.ProviderRequestId) || result.ProviderRequestId.Length > 200 ||
            !Uri.TryCreate(result.RedirectUrl, UriKind.Absolute, out var redirect) ||
            redirect.AbsoluteUri.Length > 2000 ||
            (redirect.Scheme != Uri.UriSchemeHttps &&
             !(redirect.Scheme == Uri.UriSchemeHttp && redirect.IsLoopback)))
        {
            throw new InvalidOperationException("The payment provider returned an invalid initiation result.");
        }
    }

    private static string NormalizeProviderCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 100)
        {
            throw new ArgumentException("A valid provider code is required.", nameof(value));
        }

        return value.Trim().ToUpperInvariant();
    }

    private static string NormalizeGatewayCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 50)
        {
            throw new ArgumentException("A valid gateway code is required.", nameof(value));
        }

        return value.Trim().ToUpperInvariant();
    }

    private static string NormalizeExternalCallbackId(string? value, string payloadHash)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? payloadHash : value.Trim();
        if (normalized.Length > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        return normalized;
    }

    private static string NormalizeFailureCode(string? value, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return normalized.Length <= 100 ? normalized : normalized[..100];
    }

    private static void ValidateActor(Guid actorUserId)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("A valid actor identifier is required.", nameof(actorUserId));
        }
    }

    private sealed record VerifiedCallback(
        PaymentGateway? Gateway,
        PaymentGatewayCallbackResult Result);
}
