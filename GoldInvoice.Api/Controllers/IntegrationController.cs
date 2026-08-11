using GoldInvoice.Api.Security;
using GoldInvoice.Application.Integration;
using GoldInvoice.Application.Security;
using GoldInvoice.Contracts.Common;
using GoldInvoice.Contracts.Integration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GoldInvoice.Api.Controllers;

[ApiController]
[Authorize]
[RequestSizeLimit(32 * 1024)]
[Route("api/v1/integration")]
public sealed class IntegrationController(
    IIntegrationEventQueryService eventQueryService,
    IOutboxAdministrationService administrationService) : ControllerBase
{
    [HttpGet("events")]
    public async Task<ActionResult<IntegrationEventPageResponse>> GetEvents(
        [FromQuery] DateTimeOffset? afterOccurredAt = null,
        [FromQuery] Guid? afterEventId = null,
        [FromQuery] Guid? deviceId = null,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var page = await eventQueryService.GetEventsAsync(
            User.GetRequiredUserId(),
            User.FindAll(SecurityClaimNames.Role).Select(claim => claim.Value).ToArray(),
            deviceId,
            afterOccurredAt,
            afterEventId,
            pageSize,
            cancellationToken);
        return Ok(new IntegrationEventPageResponse
        {
            Items = page.Items.Select(MapEvent).ToArray(),
            NextOccurredAt = page.NextOccurredAt,
            NextEventId = page.NextEventId
        });
    }

    [Authorize(Policy = SecurityPermissions.OutboxReprocess)]
    [HttpGet("outbox/dead-letters")]
    public async Task<ActionResult<PagedResponse<DeadLetterResponse>>> GetDeadLetters(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await administrationService.GetDeadLettersAsync(page, pageSize, cancellationToken);
        return Ok(new PagedResponse<DeadLetterResponse>
        {
            Items = result.Items.Select(MapDeadLetter).ToArray(),
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount
        });
    }

    [Authorize(Policy = SecurityPermissions.OutboxReprocess)]
    [HttpPost("outbox/dead-letters/{messageId:guid}/reprocess")]
    public async Task<ActionResult<DeadLetterResponse>> Reprocess(
        Guid messageId,
        ReprocessDeadLetterRequest request,
        CancellationToken cancellationToken) =>
        Ok(MapDeadLetter(await administrationService.ReprocessAsync(
            messageId,
            new ReprocessDeadLetterCommand(
                User.GetRequiredUserId(),
                request.Reason,
                request.RowVersion,
                HttpContext.TraceIdentifier),
            cancellationToken)));

    private static IntegrationEventResponse MapEvent(RecoverableIntegrationEvent integrationEvent) => new()
    {
        EventId = integrationEvent.EventId,
        EventType = integrationEvent.EventType,
        OccurredAt = integrationEvent.OccurredAt,
        AggregateType = integrationEvent.AggregateType,
        AggregateId = integrationEvent.AggregateId,
        Data = integrationEvent.Data
    };

    private static DeadLetterResponse MapDeadLetter(DeadLetterInfo message) => new()
    {
        Id = message.Id,
        MessageType = message.MessageType,
        Status = message.Status,
        OccurredAt = message.OccurredAt,
        RetryCount = message.RetryCount,
        NextRetryAt = message.NextRetryAt,
        LastError = message.LastError,
        RowVersion = message.RowVersion
    };
}
