using System.Text.Json;

namespace GoldInvoice.Contracts.Integration;

public sealed class IntegrationEventResponse
{
    public Guid EventId { get; init; }

    public string EventType { get; init; } = string.Empty;

    public DateTimeOffset OccurredAt { get; init; }

    public string AggregateType { get; init; } = string.Empty;

    public Guid AggregateId { get; init; }

    public JsonElement Data { get; init; }
}

public sealed class IntegrationEventPageResponse
{
    public IReadOnlyList<IntegrationEventResponse> Items { get; init; } = [];

    public DateTimeOffset? NextOccurredAt { get; init; }

    public Guid? NextEventId { get; init; }
}

public sealed class DeadLetterResponse
{
    public Guid Id { get; init; }

    public string MessageType { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public DateTimeOffset OccurredAt { get; init; }

    public int RetryCount { get; init; }

    public DateTimeOffset? NextRetryAt { get; init; }

    public string? LastError { get; init; }

    public string RowVersion { get; init; } = string.Empty;
}

public sealed class ReprocessDeadLetterRequest
{
    public string Reason { get; init; } = string.Empty;

    public string RowVersion { get; init; } = string.Empty;
}
