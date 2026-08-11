using GoldInvoice.Application.Integration;
using GoldInvoice.Contracts.Integration;
using Microsoft.AspNetCore.SignalR;

namespace GoldInvoice.Api.Integration;

internal sealed class SignalRIntegrationEventHandler(IHubContext<IntegrationHub> hubContext)
    : IIntegrationEventHandler
{
    public async Task HandleAsync(
        ClaimedIntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var audience = integrationEvent.Envelope.Audience;
        var groups = audience.UserIds.Select(IntegrationHubGroups.User)
            .Concat(audience.Roles.Select(IntegrationHubGroups.Role))
            .Concat(audience.DeviceIds.Select(IntegrationHubGroups.Device))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (groups.Length == 0)
        {
            throw new PermanentIntegrationEventException("The real-time event audience is empty.");
        }

        var notification = new IntegrationEventResponse
        {
            EventId = integrationEvent.EventId,
            EventType = integrationEvent.EventType,
            OccurredAt = integrationEvent.OccurredAt,
            AggregateType = integrationEvent.Envelope.AggregateType,
            AggregateId = integrationEvent.Envelope.AggregateId,
            Data = integrationEvent.Envelope.Data
        };
        await hubContext.Clients.Groups(groups).SendAsync(
            "integrationEvent",
            notification,
            cancellationToken);
    }
}
