using GoldInvoice.Application.Business;
using GoldInvoice.Application.Security;
using GoldInvoice.Contracts.Business;
using GoldInvoice.Contracts.Common;
using GoldInvoice.Domain.Business;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GoldInvoice.Api.Controllers;

[ApiController]
[Authorize]
[RequestSizeLimit(64 * 1024)]
[Route("api/v1/crm/interactions")]
public sealed class CrmController(ICustomerInteractionService interactionService) : ControllerBase
{
    [Authorize(Policy = SecurityPermissions.CrmRead)]
    [HttpGet]
    public async Task<ActionResult<PagedResponse<CustomerInteractionResponse>>> GetInteractions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] Guid? customerId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var result = await interactionService.GetInteractionsAsync(
            page,
            pageSize,
            customerId,
            ParseOptionalStatus(status),
            cancellationToken);
        return Ok(new PagedResponse<CustomerInteractionResponse>
        {
            Items = result.Items.Select(Map).ToArray(),
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount
        });
    }

    [Authorize(Policy = SecurityPermissions.CrmManage)]
    [HttpPost]
    public async Task<ActionResult<CustomerInteractionResponse>> CreateInteraction(
        CreateCustomerInteractionRequest request,
        CancellationToken cancellationToken)
    {
        var interaction = await interactionService.CreateInteractionAsync(
            new CreateCustomerInteractionCommand(
                request.CustomerId,
                ParseInteractionType(request.InteractionType),
                request.Subject,
                request.Notes,
                request.OccurredAt ?? DateTimeOffset.UtcNow,
                request.NextFollowUpAt),
            cancellationToken);
        return CreatedAtAction(nameof(GetInteractions), Map(interaction));
    }

    [Authorize(Policy = SecurityPermissions.CrmManage)]
    [HttpPost("{interactionId:guid}/status")]
    public async Task<ActionResult<CustomerInteractionResponse>> ChangeStatus(
        Guid interactionId,
        ChangeCustomerInteractionStatusRequest request,
        CancellationToken cancellationToken) =>
        Ok(Map(await interactionService.ChangeStatusAsync(
            interactionId,
            new ChangeCustomerInteractionStatusCommand(
                ParseStatus(request.Status),
                request.RowVersion),
            cancellationToken)));

    private static CustomerInteractionResponse Map(CustomerInteractionInfo interaction) => new()
    {
        Id = interaction.Id,
        CustomerId = interaction.CustomerId,
        CustomerName = interaction.CustomerName,
        InteractionType = interaction.InteractionType,
        Subject = interaction.Subject,
        Notes = interaction.Notes,
        OccurredAt = interaction.OccurredAt,
        NextFollowUpAt = interaction.NextFollowUpAt,
        Status = interaction.Status,
        CompletedAt = interaction.CompletedAt,
        RowVersion = interaction.RowVersion
    };

    private static CustomerInteractionType ParseInteractionType(string value) =>
        Enum.TryParse<CustomerInteractionType>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new ArgumentException("The interaction type is invalid.", nameof(value));

    private static CustomerInteractionStatus ParseStatus(string value) =>
        Enum.TryParse<CustomerInteractionStatus>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new ArgumentException("The interaction status is invalid.", nameof(value));

    private static CustomerInteractionStatus? ParseOptionalStatus(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : ParseStatus(value);
}
