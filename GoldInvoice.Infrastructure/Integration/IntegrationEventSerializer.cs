using System.Diagnostics;
using System.Text.Json;
using GoldInvoice.Application.Integration;
using GoldInvoice.Domain.Platform;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;

namespace GoldInvoice.Infrastructure.Integration;

internal static class IntegrationEventSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(
        IntegrationEventDefinition definition,
        IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!IntegrationEventTypes.Supported.Contains(definition.EventType) ||
            string.IsNullOrWhiteSpace(definition.AggregateType) ||
            definition.AggregateType.Trim().Length > 200 ||
            definition.AggregateId == Guid.Empty ||
            definition.OccurredAt == default || definition.Data is null)
        {
            throw new ArgumentException("The integration event definition is invalid.", nameof(definition));
        }

        var audience = NormalizeAudience(definition.Audience);
        if (audience.UserIds.Count == 0 && audience.Roles.Count == 0 && audience.DeviceIds.Count == 0)
        {
            throw new ArgumentException("An integration event requires a bounded audience.", nameof(definition));
        }

        var envelope = new IntegrationEventEnvelope(
            definition.AggregateType.Trim(),
            definition.AggregateId,
            ResolveCorrelationId(httpContextAccessor),
            definition.CausationId,
            audience,
            JsonSerializer.SerializeToElement(definition.Data, definition.Data.GetType(), SerializerOptions));
        return JsonSerializer.Serialize(envelope, SerializerOptions);
    }

    public static ClaimedIntegrationEvent Deserialize(
        Guid eventId,
        string eventType,
        DateTimeOffset occurredAt,
        string payload)
    {
        if (eventId == Guid.Empty || !IntegrationEventTypes.Supported.Contains(eventType) ||
            occurredAt == default || string.IsNullOrWhiteSpace(payload))
        {
            throw new PermanentIntegrationEventException("The outbox message metadata is invalid.");
        }

        IntegrationEventEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<IntegrationEventEnvelope>(payload, SerializerOptions);
        }
        catch (JsonException)
        {
            throw new PermanentIntegrationEventException("The outbox payload is not valid JSON.");
        }

        if (envelope is null || string.IsNullOrWhiteSpace(envelope.AggregateType) ||
            envelope.AggregateId == Guid.Empty || envelope.Audience is null ||
            envelope.Data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new PermanentIntegrationEventException("The outbox event envelope is invalid.");
        }

        var audience = NormalizeAudience(envelope.Audience);
        if (audience.UserIds.Count == 0 && audience.Roles.Count == 0 && audience.DeviceIds.Count == 0)
        {
            throw new PermanentIntegrationEventException("The outbox event audience is empty.");
        }

        return new ClaimedIntegrationEvent(
            eventId,
            eventType,
            occurredAt,
            envelope with { Audience = audience });
    }

    private static IntegrationEventAudience NormalizeAudience(IntegrationEventAudience audience)
    {
        ArgumentNullException.ThrowIfNull(audience);
        return new IntegrationEventAudience(
            (audience.UserIds ?? [])
                .Where(id => id != Guid.Empty)
                .Distinct()
                .OrderBy(id => id)
                .ToArray(),
            (audience.Roles ?? [])
                .Where(role => !string.IsNullOrWhiteSpace(role))
                .Select(role => role.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            (audience.DeviceIds ?? [])
                .Where(id => id != Guid.Empty)
                .Distinct()
                .OrderBy(id => id)
                .ToArray());
    }

    private static string? ResolveCorrelationId(IHttpContextAccessor accessor)
    {
        var value = accessor.HttpContext?.TraceIdentifier;
        if (string.IsNullOrWhiteSpace(value))
        {
            value = Activity.Current?.TraceId.ToHexString();
        }

        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim()[..Math.Min(value.Trim().Length, 128)];
    }
}

internal sealed class OutboxWriter(
    GoldInvoiceDbContext dbContext,
    IHttpContextAccessor httpContextAccessor) : IOutboxWriter
{
    public Guid Add(IntegrationEventDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var message = new OutboxMessage(
            definition.EventType,
            IntegrationEventSerializer.Serialize(definition, httpContextAccessor),
            definition.OccurredAt);
        dbContext.OutboxMessages.Add(message);
        return message.Id;
    }
}
