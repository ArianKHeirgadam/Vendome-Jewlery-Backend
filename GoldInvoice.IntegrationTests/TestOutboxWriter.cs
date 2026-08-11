using GoldInvoice.Application.Integration;
using System.Collections.Concurrent;

namespace GoldInvoice.IntegrationTests;

internal sealed class TestOutboxWriter : IOutboxWriter
{
    public static TestOutboxWriter Instance { get; } = new();

    public ConcurrentQueue<IntegrationEventDefinition> Events { get; } = new();

    public Guid Add(IntegrationEventDefinition definition)
    {
        Events.Enqueue(definition);
        return Guid.NewGuid();
    }
}
