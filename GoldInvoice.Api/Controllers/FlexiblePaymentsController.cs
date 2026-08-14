using GoldInvoice.Api.Security;
using GoldInvoice.Application.Payments;
using GoldInvoice.Application.Security;
using GoldInvoice.Contracts.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GoldInvoice.Api.Controllers;

[ApiController]
[Authorize]
[RequestSizeLimit(64 * 1024)]
[Route("api/v1/finance")]
public sealed class FlexiblePaymentsController(
    IFlexiblePaymentService flexiblePaymentService) : ControllerBase
{
    [Authorize(Policy = SecurityPermissions.PaymentsRead)]
    [HttpGet("installments")]
    public async Task<ActionResult<IReadOnlyList<InstallmentPlanInfo>>> GetInstallments(
        CancellationToken cancellationToken) =>
        Ok(await flexiblePaymentService.GetInstallmentPlansAsync(cancellationToken));

    [Authorize(Policy = SecurityPermissions.PaymentsRead)]
    [HttpGet("installments/{planId:guid}")]
    public async Task<ActionResult<InstallmentPlanInfo>> GetInstallment(
        Guid planId,
        CancellationToken cancellationToken) =>
        Ok(await flexiblePaymentService.GetInstallmentPlanAsync(planId, cancellationToken));

    [Authorize(Policy = SecurityPermissions.PaymentsManage)]
    [HttpPost("installments")]
    public async Task<ActionResult<InstallmentPlanInfo>> CreateInstallment(
        CreateInstallmentPlanRequest request,
        CancellationToken cancellationToken)
    {
        var plan = await flexiblePaymentService.CreateInstallmentPlanAsync(
            new CreateInstallmentPlanCommand(
                User.GetRequiredUserId(),
                request.OrderId,
                request.Installments
                    .Select(item => new InstallmentDraftInfo(item.DueOn, item.AmountRials))
                    .ToArray()),
            cancellationToken);

        return Ok(plan);
    }

    [Authorize(Policy = SecurityPermissions.PaymentsManage)]
    [HttpPost("installments/{planId:guid}/items/{installmentId:guid}/pay")]
    public async Task<ActionResult<InstallmentPlanInfo>> PayInstallment(
        Guid planId,
        Guid installmentId,
        PayInstallmentRequest request,
        CancellationToken cancellationToken) =>
        Ok(await flexiblePaymentService.PayInstallmentAsync(
            new PayInstallmentCommand(
                User.GetRequiredUserId(),
                planId,
                installmentId,
                request.Reference),
            cancellationToken));

    [Authorize(Policy = SecurityPermissions.PaymentsRead)]
    [HttpGet("trust-funds")]
    public async Task<ActionResult<TrustFundSnapshotInfo>> GetTrustFunds(
        CancellationToken cancellationToken) =>
        Ok(await flexiblePaymentService.GetTrustFundSnapshotAsync(cancellationToken));

    [Authorize(Policy = SecurityPermissions.PaymentsRead)]
    [HttpGet("trust-funds/customers/{customerId:guid}")]
    public async Task<ActionResult<TrustFundBalanceInfo>> GetTrustFundBalance(
        Guid customerId,
        CancellationToken cancellationToken) =>
        Ok(await flexiblePaymentService.GetTrustFundBalanceAsync(customerId, cancellationToken));

    [Authorize(Policy = SecurityPermissions.PaymentsManage)]
    [HttpPost("trust-funds/entries")]
    public async Task<ActionResult<TrustFundEntryInfo>> AddTrustFundEntry(
        AddTrustFundEntryRequest request,
        CancellationToken cancellationToken) =>
        Ok(await flexiblePaymentService.AddTrustFundEntryAsync(
            new AddTrustFundEntryCommand(
                User.GetRequiredUserId(),
                request.CustomerId,
                request.EntryType,
                request.AmountRials,
                request.OccurredAt,
                request.Reference),
            cancellationToken));

    [Authorize(Policy = SecurityPermissions.PaymentsManage)]
    [HttpPost("trust-funds/allocate")]
    public async Task<ActionResult<TrustFundAllocationInfo>> AllocateTrustFund(
        AllocateTrustFundRequest request,
        CancellationToken cancellationToken) =>
        Ok(await flexiblePaymentService.AllocateTrustFundAsync(
            new AllocateTrustFundCommand(
                User.GetRequiredUserId(),
                request.OrderId,
                request.Reference),
            cancellationToken));
}
