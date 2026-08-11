using GoldInvoice.Application.Integration;
using GoldInvoice.Domain.Platform;
using GoldInvoice.Infrastructure.Configuration;
using GoldInvoice.Infrastructure.Integration;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GoldInvoice.IntegrationTests;

public sealed class PhaseSixWorkflowTests
{
    private static readonly DateTimeOffset FixedNow =
        DateTimeOffset.Parse("2026-08-10T20:00:00+00:00");

    [Fact]
    public async Task Dispatcher_ProcessesVersionedMessageOnceAndPreservesCorrelation()
    {
        var handler = new RecordingHandler();
        await using var provider = CreateProvider(handler);
        await AddEventAsync(
            provider,
            new IntegrationEventAudience([Guid.NewGuid()], [], []),
            correlationId: "phase-6-correlation");
        var dispatcher = provider.GetRequiredService<IOutboxDispatcher>();

        var first = await dispatcher.DispatchBatchAsync(CancellationToken.None);
        var second = await dispatcher.DispatchBatchAsync(CancellationToken.None);

        Assert.Equal(new OutboxDispatchResult(1, 1, 0), first);
        Assert.Equal(new OutboxDispatchResult(0, 0, 0), second);
        var delivered = Assert.Single(handler.Events);
        Assert.Equal("phase-6-correlation", delivered.Envelope.CorrelationId);
        await using var scope = provider.CreateAsyncScope();
        var message = await scope.ServiceProvider.GetRequiredService<GoldInvoiceDbContext>()
            .OutboxMessages.SingleAsync();
        Assert.Equal(OutboxMessageStatus.Processed, message.Status);
        Assert.Equal(0, message.RetryCount);
    }

    [Fact]
    public async Task Dispatcher_UsesBoundedRetryAndDeadLettersPermanentContractFailure()
    {
        var handler = new RecordingHandler(_ => throw new TimeoutException());
        await using var provider = CreateProvider(handler);
        await AddEventAsync(provider, new IntegrationEventAudience([Guid.NewGuid()], [], []));
        var dispatcher = provider.GetRequiredService<IOutboxDispatcher>();

        var result = await dispatcher.DispatchBatchAsync(CancellationToken.None);

        Assert.Equal(1, result.FailedCount);
        await using (var scope = provider.CreateAsyncScope())
        {
            var message = await scope.ServiceProvider.GetRequiredService<GoldInvoiceDbContext>()
                .OutboxMessages.SingleAsync();
            Assert.Equal(OutboxMessageStatus.Failed, message.Status);
            Assert.Equal(1, message.RetryCount);
            Assert.Equal(FixedNow.AddSeconds(2), message.NextRetryAt);
            Assert.Equal("Transient delivery failure (TimeoutException).", message.LastError);
        }

        await using var permanentProvider = CreateProvider(new RecordingHandler());
        await using (var scope = permanentProvider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<GoldInvoiceDbContext>();
            context.OutboxMessages.Add(new OutboxMessage("unknown.event.v1", "{\"secret\":\"not logged\"}", FixedNow));
            await context.SaveChangesAsync();
        }

        await permanentProvider.GetRequiredService<IOutboxDispatcher>()
            .DispatchBatchAsync(CancellationToken.None);
        await using (var scope = permanentProvider.CreateAsyncScope())
        {
            var message = await scope.ServiceProvider.GetRequiredService<GoldInvoiceDbContext>()
                .OutboxMessages.SingleAsync();
            Assert.Equal(OutboxMessageStatus.DeadLetter, message.Status);
            Assert.Equal(1, message.RetryCount);
            Assert.DoesNotContain("secret", message.LastError, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task RecoveryQuery_IsAudienceScopedAndUsesBoundedCursor()
    {
        var firstUser = Guid.NewGuid();
        var secondUser = Guid.NewGuid();
        await using var provider = CreateProvider(new RecordingHandler());
        await AddEventAsync(provider, new IntegrationEventAudience([firstUser], [], []));
        await AddEventAsync(provider, new IntegrationEventAudience([secondUser], [], []));
        await provider.GetRequiredService<IOutboxDispatcher>()
            .DispatchBatchAsync(CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var query = new IntegrationEventQueryService(
            scope.ServiceProvider.GetRequiredService<GoldInvoiceDbContext>());
        var page = await query.GetEventsAsync(
            firstUser,
            [],
            deviceId: null,
            afterOccurredAt: null,
            afterEventId: null,
            pageSize: 10,
            CancellationToken.None);

        Assert.Single(page.Items);
        Assert.NotNull(page.NextOccurredAt);
        Assert.NotNull(page.NextEventId);
    }

    [Fact]
    public async Task Dispatcher_CancellationReleasesClaimWithoutConsumingAttempt()
    {
        var handler = new BlockingHandler();
        await using var provider = CreateProvider(handler);
        await AddEventAsync(provider, new IntegrationEventAudience([Guid.NewGuid()], [], []));
        using var cancellation = new CancellationTokenSource();

        var dispatch = provider.GetRequiredService<IOutboxDispatcher>()
            .DispatchBatchAsync(cancellation.Token);
        await handler.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await dispatch.WaitAsync(TimeSpan.FromSeconds(10)));

        await using var scope = provider.CreateAsyncScope();
        var message = await scope.ServiceProvider.GetRequiredService<GoldInvoiceDbContext>()
            .OutboxMessages.SingleAsync();
        Assert.Equal(OutboxMessageStatus.Failed, message.Status);
        Assert.Equal(0, message.RetryCount);
        Assert.Null(message.LockId);
    }

    [Fact]
    public async Task DeadLetterReprocess_PreservesAttemptsAndWritesAuditLog()
    {
        await using var provider = CreateProvider(new RecordingHandler());
        Guid messageId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<GoldInvoiceDbContext>();
            var message = new OutboxMessage("invalid.event.v1", "{}", FixedNow);
            messageId = message.Id;
            context.OutboxMessages.Add(message);
            await context.SaveChangesAsync();
        }

        await provider.GetRequiredService<IOutboxDispatcher>()
            .DispatchBatchAsync(CancellationToken.None);
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<GoldInvoiceDbContext>();
            var before = await context.OutboxMessages.AsNoTracking().SingleAsync();
            var service = new OutboxAdministrationService(context, provider.GetRequiredService<TimeProvider>());
            var reprocessed = await service.ReprocessAsync(
                messageId,
                new ReprocessDeadLetterCommand(
                    Guid.NewGuid(),
                    "Contract mapping corrected.",
                    Convert.ToBase64String(before.RowVersion),
                    "reprocess-correlation"),
                CancellationToken.None);

            Assert.Equal(1, reprocessed.RetryCount);
            Assert.Single(await context.AuditLogs.ToListAsync());
            Assert.Equal(
                OutboxMessageStatus.Pending,
                (await context.OutboxMessages.SingleAsync()).Status);
        }
    }

    private static ServiceProvider CreateProvider(IIntegrationEventHandler handler)
    {
        var databaseName = $"phase-six-{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddDbContext<GoldInvoiceDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(FixedNow));
        services.AddSingleton<IOptions<OutboxOptions>>(Options.Create(new OutboxOptions
        {
            BatchSize = 10,
            PollIntervalMilliseconds = 100,
            LockDurationSeconds = 30,
            HeartbeatIntervalSeconds = 10,
            MaximumAttempts = 3,
            RetryBaseDelaySeconds = 2,
            MaximumRetryDelaySeconds = 10
        }));
        services.AddScoped<IOutboxStore, OutboxStore>();
        services.AddSingleton<IIntegrationEventHandler>(handler);
        services.AddSingleton<IOutboxDispatcher, OutboxDispatcher>();
        services.AddSingleton<ILogger<OutboxDispatcher>>(NullLogger<OutboxDispatcher>.Instance);
        return services.BuildServiceProvider();
    }

    private static async Task AddEventAsync(
        IServiceProvider provider,
        IntegrationEventAudience audience,
        string? correlationId = null)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<GoldInvoiceDbContext>();
        var httpContext = correlationId is null
            ? null
            : new DefaultHttpContext { TraceIdentifier = correlationId };
        var writer = new OutboxWriter(
            context,
            new HttpContextAccessor { HttpContext = httpContext });
        writer.Add(new IntegrationEventDefinition(
            IntegrationEventTypes.OrderStatusChangedV1,
            "Order",
            Guid.NewGuid(),
            FixedNow,
            new OrderStatusChangedV1(Guid.NewGuid(), Guid.NewGuid(), "Pending", "AwaitingPayment"),
            audience));
        await context.SaveChangesAsync();
    }

    private sealed class RecordingHandler(Action<ClaimedIntegrationEvent>? action = null)
        : IIntegrationEventHandler
    {
        public List<ClaimedIntegrationEvent> Events { get; } = [];

        public Task HandleAsync(
            ClaimedIntegrationEvent integrationEvent,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action?.Invoke(integrationEvent);
            Events.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingHandler : IIntegrationEventHandler
    {
        private readonly TaskCompletionSource entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => entered.Task;

        public async Task HandleAsync(
            ClaimedIntegrationEvent integrationEvent,
            CancellationToken cancellationToken)
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
