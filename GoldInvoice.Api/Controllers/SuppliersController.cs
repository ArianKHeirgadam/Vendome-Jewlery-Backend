using GoldInvoice.Application.Business;
using GoldInvoice.Application.Security;
using GoldInvoice.Contracts.Business;
using GoldInvoice.Contracts.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GoldInvoice.Api.Controllers;

[ApiController]
[Authorize]
[RequestSizeLimit(64 * 1024)]
[Route("api/v1/suppliers")]
public sealed class SuppliersController(ISupplierService supplierService) : ControllerBase
{
    [Authorize(Policy = SecurityPermissions.SuppliersRead)]
    [HttpGet]
    public async Task<ActionResult<PagedResponse<SupplierResponse>>> GetSuppliers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? query = null,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var result = await supplierService.GetSuppliersAsync(
            page,
            pageSize,
            query,
            includeInactive,
            cancellationToken);
        return Ok(new PagedResponse<SupplierResponse>
        {
            Items = result.Items.Select(Map).ToArray(),
            Page = result.Page,
            PageSize = result.PageSize,
            TotalCount = result.TotalCount
        });
    }

    [Authorize(Policy = SecurityPermissions.SuppliersRead)]
    [HttpGet("{supplierId:guid}")]
    public async Task<ActionResult<SupplierResponse>> GetSupplier(
        Guid supplierId,
        CancellationToken cancellationToken) =>
        Ok(Map(await supplierService.GetSupplierAsync(supplierId, cancellationToken)));

    [Authorize(Policy = SecurityPermissions.SuppliersManage)]
    [HttpPost]
    public async Task<ActionResult<SupplierResponse>> CreateSupplier(
        CreateSupplierRequest request,
        CancellationToken cancellationToken)
    {
        var supplier = await supplierService.CreateSupplierAsync(
            new CreateSupplierCommand(
                request.Code,
                request.Name,
                request.ContactName,
                request.PhoneNumber,
                request.Email,
                request.NationalId,
                request.AddressLine,
                request.Notes),
            cancellationToken);
        return CreatedAtAction(nameof(GetSupplier), new { supplierId = supplier.Id }, Map(supplier));
    }

    [Authorize(Policy = SecurityPermissions.SuppliersManage)]
    [HttpPut("{supplierId:guid}")]
    public async Task<ActionResult<SupplierResponse>> UpdateSupplier(
        Guid supplierId,
        UpdateSupplierRequest request,
        CancellationToken cancellationToken) =>
        Ok(Map(await supplierService.UpdateSupplierAsync(
            supplierId,
            new UpdateSupplierCommand(
                request.Code,
                request.Name,
                request.ContactName,
                request.PhoneNumber,
                request.Email,
                request.NationalId,
                request.AddressLine,
                request.Notes,
                request.IsActive,
                request.RowVersion),
            cancellationToken)));

    [Authorize(Policy = SecurityPermissions.SuppliersManage)]
    [HttpDelete("{supplierId:guid}")]
    public async Task<IActionResult> DeleteSupplier(
        Guid supplierId,
        CancellationToken cancellationToken)
    {
        await supplierService.DeleteSupplierAsync(supplierId, cancellationToken);
        return NoContent();
    }

    private static SupplierResponse Map(SupplierInfo supplier) => new()
    {
        Id = supplier.Id,
        Code = supplier.Code,
        Name = supplier.Name,
        ContactName = supplier.ContactName,
        PhoneNumber = supplier.PhoneNumber,
        Email = supplier.Email,
        NationalId = supplier.NationalId,
        AddressLine = supplier.AddressLine,
        Notes = supplier.Notes,
        IsActive = supplier.IsActive,
        CreatedAt = supplier.CreatedAt,
        UpdatedAt = supplier.UpdatedAt,
        RowVersion = supplier.RowVersion
    };
}
