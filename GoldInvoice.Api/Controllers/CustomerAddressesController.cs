using GoldInvoice.Api.Security;
using GoldInvoice.Application.Customers;
using GoldInvoice.Application.Security;
using GoldInvoice.Contracts.Customers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GoldInvoice.Api.Controllers;

[ApiController]
[Authorize]
[RequestSizeLimit(32 * 1024)]
[Route("api/v1/customers/{customerId:guid}/addresses")]
public sealed class CustomerAddressesController(ICustomerAddressService addressService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CustomerAddressResponse>>> GetAddresses(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        var actorUserId = User.GetRequiredUserId();
        var addresses = await addressService.GetAddressesAsync(
            actorUserId,
            customerId,
            CanManageCustomers(),
            cancellationToken);
        return Ok(addresses.Select(Map).ToArray());
    }

    [HttpGet("{addressId:guid}")]
    public async Task<ActionResult<CustomerAddressResponse>> GetAddress(
        Guid customerId,
        Guid addressId,
        CancellationToken cancellationToken)
    {
        var address = await addressService.GetAddressAsync(
            addressId,
            User.GetRequiredUserId(),
            CanManageCustomers(),
            cancellationToken);
        if (address.CustomerId != customerId)
        {
            return NotFound();
        }

        return Ok(Map(address));
    }

    [HttpPost]
    public async Task<ActionResult<CustomerAddressResponse>> CreateAddress(
        Guid customerId,
        CreateCustomerAddressRequest request,
        CancellationToken cancellationToken)
    {
        var address = await addressService.CreateAddressAsync(
            new CreateCustomerAddressCommand(
                User.GetRequiredUserId(),
                customerId,
                CanManageCustomers(),
                request.Title,
                request.RecipientName,
                request.PhoneNumber,
                request.Province,
                request.City,
                request.PostalCode,
                request.AddressLine,
                request.IsDefault),
            cancellationToken);
        return CreatedAtAction(
            nameof(GetAddress),
            new { customerId, addressId = address.Id },
            Map(address));
    }

    [HttpPut("{addressId:guid}")]
    public async Task<ActionResult<CustomerAddressResponse>> UpdateAddress(
        Guid customerId,
        Guid addressId,
        UpdateCustomerAddressRequest request,
        CancellationToken cancellationToken) =>
        Ok(Map(await addressService.UpdateAddressAsync(
            addressId,
            new UpdateCustomerAddressCommand(
                User.GetRequiredUserId(),
                customerId,
                CanManageCustomers(),
                request.Title,
                request.RecipientName,
                request.PhoneNumber,
                request.Province,
                request.City,
                request.PostalCode,
                request.AddressLine,
                request.IsDefault,
                request.RowVersion),
            cancellationToken)));

    [HttpDelete("{addressId:guid}")]
    public async Task<IActionResult> DeleteAddress(
        Guid customerId,
        Guid addressId,
        [FromQuery] string rowVersion,
        CancellationToken cancellationToken)
    {
        var actorUserId = User.GetRequiredUserId();
        var address = await addressService.GetAddressAsync(
            addressId,
            actorUserId,
            CanManageCustomers(),
            cancellationToken);
        if (address.CustomerId != customerId)
        {
            return NotFound();
        }

        await addressService.DeleteAddressAsync(
            addressId,
            actorUserId,
            CanManageCustomers(),
            rowVersion,
            cancellationToken);
        return NoContent();
    }

    private bool CanManageCustomers() =>
        User.HasPermission(SecurityPermissions.OrdersManage) ||
        User.HasPermission(SecurityPermissions.UsersManage);

    private static CustomerAddressResponse Map(CustomerAddressInfo address) => new()
    {
        Id = address.Id,
        CustomerId = address.CustomerId,
        Title = address.Title,
        RecipientName = address.RecipientName,
        PhoneNumber = address.PhoneNumber,
        Province = address.Province,
        City = address.City,
        PostalCode = address.PostalCode,
        AddressLine = address.AddressLine,
        IsDefault = address.IsDefault,
        RowVersion = address.RowVersion
    };
}
