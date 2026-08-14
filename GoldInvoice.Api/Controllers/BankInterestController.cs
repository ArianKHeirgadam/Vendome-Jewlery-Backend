using GoldInvoice.Application.Payments;
using GoldInvoice.Application.Security;
using GoldInvoice.Contracts.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GoldInvoice.Api.Controllers;

[ApiController]
[Authorize]
[RequestSizeLimit(64 * 1024)]
[Route("api/v1/finance/bank-interest")]
public sealed class BankInterestController(
    IBankInterestService bankInterestService) : ControllerBase
{
    [Authorize(Policy = SecurityPermissions.PaymentsRead)]
    [HttpGet]
    public async Task<ActionResult<BankInterestSnapshotInfo>> Get(
        CancellationToken cancellationToken) =>
        Ok(await bankInterestService.GetSnapshotAsync(cancellationToken));

    [Authorize(Policy = SecurityPermissions.PaymentsManage)]
    [HttpPost("deposits")]
    public async Task<ActionResult<BankDepositInfo>> CreateDeposit(
        CreateBankDepositRequest request,
        CancellationToken cancellationToken) =>
        Ok(await bankInterestService.CreateDepositAsync(
            new CreateBankDepositCommand(
                request.BankName,
                request.Title,
                request.AccountNumber,
                request.PrincipalRials,
                request.AnnualInterestRatePercent,
                request.OpenedOn,
                request.MaturityOn),
            cancellationToken));

    [Authorize(Policy = SecurityPermissions.PaymentsManage)]
    [HttpPost("entries")]
    public async Task<ActionResult<BankInterestEntryInfo>> AddEntry(
        AddBankInterestEntryRequest request,
        CancellationToken cancellationToken) =>
        Ok(await bankInterestService.AddEntryAsync(
            new AddBankInterestEntryCommand(
                request.DepositId,
                request.Direction,
                request.BankName,
                request.OccurredOn,
                request.AmountRials,
                request.Reference),
            cancellationToken));

    [Authorize(Policy = SecurityPermissions.PaymentsManage)]
    [HttpPost("deposits/{depositId:guid}/close")]
    public async Task<ActionResult<BankDepositInfo>> CloseDeposit(
        Guid depositId,
        CancellationToken cancellationToken) =>
        Ok(await bankInterestService.CloseDepositAsync(
            depositId,
            cancellationToken));
}
