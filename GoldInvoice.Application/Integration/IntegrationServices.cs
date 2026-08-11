using System.Text.Json;
using GoldInvoice.Application.Common;

namespace GoldInvoice.Application.Integration;

public static class IntegrationEventTypes
{
    public const string InvoiceCreatedV1 = "invoice.created.v1";
    public const string InventoryChangedV1 = "inventory.changed.v1";
    public const string OrderStatusChangedV1 = "order.status-changed.v1";
    public const string MarketPriceUpdatedV1 = "market-price.updated.v1";

    public static readonly IReadOnlySet<string> Supported = new HashSet<string>(StringComparer.Ordinal)
    {
        InvoiceCreatedV1,
        InventoryChangedV1,
        OrderStatusChangedV1,
        MarketPriceUpdatedV1
    };
}

public sealed record IntegrationEventAudience(
    IReadOnlyList<Guid> UserIds,
    IReadOnlyList<string> Roles,
    IReadOnlyList<Guid> DeviceIds);

public sealed record IntegrationEventEnvelope(
    string AggregateType,
    Guid AggregateId,
    string? CorrelationId,
    Guid? CausationId,
    IntegrationEventAudience Audience,
    JsonElement Data);

public sealed record IntegrationEventDefinition(
    string EventType,
    string AggregateType,
    Guid AggregateId,
    DateTimeOffset OccurredAt,
    object Data,
    IntegrationEventAudience Audience,
    Guid? CausationId = null);

public sealed record InvoiceCreatedV1(
    Guid InvoiceId,
    Guid OrderId,
    Guid PaymentId,
    string InvoiceNumber,
    string Status);

public sealed record InventoryChangedV1(
    Guid InventoryItemId,
    Guid WarehouseId,
    Guid ProductVariantId,
    Guid? InventoryUnitId,
    Guid MovementId,
    string ChangeType,
    int QuantityOnHand,
    int QuantityReserved,
    int QuantityAvailable);

public sealed record OrderStatusChangedV1(
    Guid OrderId,
    Guid CustomerId,
    string? FromStatus,
    string ToStatus);

public sealed record MarketPriceUpdatedV1(
    Guid SnapshotId,
    Guid SourceId,
    string PriceType,
    long BuyPriceRials,
    long SellPriceRials,
    DateTimeOffset? ProviderTimestamp);

public interface IOutboxWriter
{
    Guid Add(IntegrationEventDefinition definition);
}

public sealed record ClaimedIntegrationEvent(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    IntegrationEventEnvelope Envelope);

public interface IIntegrationEventHandler
{
    Task HandleAsync(ClaimedIntegrationEvent integrationEvent, CancellationToken cancellationToken);
}

public interface IOutboxDispatcher
{
    Task<OutboxDispatchResult> DispatchBatchAsync(CancellationToken cancellationToken);
}

public sealed record OutboxDispatchResult(int ClaimedCount, int ProcessedCount, int FailedCount);

public sealed class PermanentIntegrationEventException(string message) : Exception(message);

public sealed record DeadLetterInfo(
    Guid Id,
    string MessageType,
    string Status,
    DateTimeOffset OccurredAt,
    int RetryCount,
    DateTimeOffset? NextRetryAt,
    string? LastError,
    string RowVersion);

public sealed record ReprocessDeadLetterCommand(
    Guid ActorUserId,
    string Reason,
    string RowVersion,
    string? CorrelationId);

public interface IOutboxAdministrationService
{
    Task<PagedResult<DeadLetterInfo>> GetDeadLettersAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<DeadLetterInfo> ReprocessAsync(
        Guid messageId,
        ReprocessDeadLetterCommand command,
        CancellationToken cancellationToken);
}

public sealed record RecoverableIntegrationEvent(
    Guid EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    string AggregateType,
    Guid AggregateId,
    JsonElement Data);

public sealed record IntegrationEventPage(
    IReadOnlyList<RecoverableIntegrationEvent> Items,
    DateTimeOffset? NextOccurredAt,
    Guid? NextEventId);

public interface IIntegrationEventQueryService
{
    Task<IntegrationEventPage> GetEventsAsync(
        Guid actorUserId,
        IReadOnlyCollection<string> actorRoles,
        Guid? deviceId,
        DateTimeOffset? afterOccurredAt,
        Guid? afterEventId,
        int pageSize,
        CancellationToken cancellationToken);
}
