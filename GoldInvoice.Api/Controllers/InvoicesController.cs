using GoldInvoice.Api.Security;
using GoldInvoice.Application.Invoicing;
using GoldInvoice.Application.Security;
using GoldInvoice.Contracts.Common;
using GoldInvoice.Contracts.Invoicing;
using GoldInvoice.Domain.Invoicing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GoldInvoice.Api.Controllers;

[ApiController]
[Authorize]
[RequestSizeLimit(32 * 1024)]
[Route("api/v1/invoices")]
public sealed class InvoicesController(IInvoiceService invoiceService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResponse<InvoiceResponse>>> GetInvoices(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var result = await invoiceService.GetInvoicesAsync(
            User.GetRequiredUserId(),
            CanReadAll(),
            page,
            pageSize,
            ParseOptionalStatus(status),
            cancellationToken);
        return Ok(new PagedResponse<InvoiceResponse>
        {
            Items = result.Items.Select(Map).ToArray(),
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount
        });
    }

    [HttpGet("{invoiceId:guid}")]
    public async Task<ActionResult<InvoiceResponse>> GetInvoice(
        Guid invoiceId,
        CancellationToken cancellationToken) =>
        Ok(Map(await invoiceService.GetInvoiceAsync(
            invoiceId,
            User.GetRequiredUserId(),
            CanReadAll(),
            cancellationToken)));

    [Authorize(Policy = SecurityPermissions.OrdersManage)]
    [HttpPost("{invoiceId:guid}/void")]
    public async Task<ActionResult<InvoiceResponse>> VoidInvoice(
        Guid invoiceId,
        VoidInvoiceRequest request,
        CancellationToken cancellationToken) =>
        Ok(Map(await invoiceService.VoidInvoiceAsync(
            invoiceId,
            new VoidInvoiceCommand(
                User.GetRequiredUserId(),
                request.Reason,
                request.RowVersion),
            cancellationToken)));

    private bool CanReadAll() =>
        User.HasPermission(SecurityPermissions.InvoicesRead) ||
        User.HasPermission(SecurityPermissions.OrdersManage);

    private static InvoiceResponse Map(InvoiceInfo invoice) => new()
    {
        Id = invoice.Id,
        OrderId = invoice.OrderId,
        CustomerId = invoice.CustomerId,
        PaymentId = invoice.PaymentId,
        InvoiceNumber = invoice.InvoiceNumber,
        Status = invoice.Status.ToString(),
        IssuedAt = invoice.IssuedAt,
        SubtotalRials = invoice.SubtotalRials,
        DiscountRials = invoice.DiscountRials,
        ShippingRials = invoice.ShippingRials,
        GrandTotalRials = invoice.GrandTotalRials,
        CustomerNameSnapshot = invoice.CustomerNameSnapshot,
        CustomerNationalIdSnapshot = invoice.CustomerNationalIdSnapshot,
        VoidedAt = invoice.VoidedAt,
        VoidReason = invoice.VoidReason,
        Address = invoice.Address is null ? null : new InvoiceAddressSnapshotResponse
        {
            Id = invoice.Address.Id,
            OrderAddressSnapshotId = invoice.Address.OrderAddressSnapshotId,
            RecipientName = invoice.Address.RecipientName,
            PhoneNumber = invoice.Address.PhoneNumber,
            Province = invoice.Address.Province,
            City = invoice.Address.City,
            PostalCode = invoice.Address.PostalCode,
            AddressLine = invoice.Address.AddressLine
        },
        Store = invoice.Store is null ? null : OrdersController.MapStore(invoice.Store),
        Items = invoice.Items.Select(item => new InvoiceItemResponse
        {
            Id = item.Id,
            OrderItemId = item.OrderItemId,
            PriceCalculationSnapshotId = item.PriceCalculationSnapshotId,
            InventoryUnitId = item.InventoryUnitId,
            LineNumber = item.LineNumber,
            Sku = item.Sku,
            ProductName = item.ProductName,
            VariantName = item.VariantName,
            GrossWeightGrams = item.GrossWeightGrams,
            NetGoldWeightGrams = item.NetGoldWeightGrams,
            Karat = item.Karat,
            Quantity = item.Quantity,
            MarketUnitPriceRials = item.MarketUnitPriceRials,
            GoldValueRials = item.GoldValueRials,
            WageRials = item.WageRials,
            ProfitRials = item.ProfitRials,
            TaxRials = item.TaxRials,
            UnitPriceRials = item.UnitPriceRials,
            LineTotalRials = item.LineTotalRials,
            RoundingPolicy = item.RoundingPolicy
        }).ToArray(),
        RowVersion = invoice.RowVersion
    };

    private static InvoiceStatus? ParseOptionalStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Enum.TryParse<InvoiceStatus>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new ArgumentException("The invoice status is invalid.", nameof(value));
    }
}
