using GoldInvoice.Application.Security;
using GoldInvoice.Application.Settings;
using GoldInvoice.Contracts.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GoldInvoice.Api.Controllers;

[ApiController]
[Authorize]
[RequestSizeLimit(32 * 1024)]
[Route("api/v1/settings/financial-workspace")]
public sealed class FinancialWorkspaceController(
    IFinancialWorkspaceService financialWorkspaceService) : ControllerBase
{
    [Authorize(Policy = SecurityPermissions.SettingsRead)]
    [HttpGet]
    public async Task<ActionResult<FinancialWorkspaceResponse>> Get(
        CancellationToken cancellationToken)
    {
        var entries = await financialWorkspaceService.ListAsync(cancellationToken);

        return Ok(new FinancialWorkspaceResponse
        {
            Entries = entries.Select(Map).ToArray(),
        });
    }

    [Authorize(Policy = SecurityPermissions.SettingsManage)]
    [HttpPost("entries")]
    public async Task<ActionResult<FinancialWorkspaceEntryResponse>> Create(
        CreateFinancialWorkspaceEntryRequest request,
        CancellationToken cancellationToken)
    {
        var entry = await financialWorkspaceService.CreateAsync(
            new CreateFinancialWorkspaceEntryCommand(
                request.Scope,
                request.EntryType,
                request.OccurredOn,
                request.AmountRials,
                request.Reason),
            cancellationToken);

        return Ok(Map(entry));
    }

    private static FinancialWorkspaceEntryResponse Map(FinancialWorkspaceEntryInfo entry) =>
        new()
        {
            Id = entry.Id,
            Scope = entry.Scope,
            EntryType = entry.EntryType,
            OccurredOn = entry.OccurredOn,
            AmountRials = entry.AmountRials,
            Reason = entry.Reason,
        };
}
