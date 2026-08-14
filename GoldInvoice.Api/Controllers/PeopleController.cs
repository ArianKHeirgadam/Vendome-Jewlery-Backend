using GoldInvoice.Application.People;
using GoldInvoice.Application.Security;
using GoldInvoice.Contracts.People;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GoldInvoice.Api.Controllers;

[ApiController]
[Authorize]
[RequestSizeLimit(32 * 1024)]
[Route("api/v1/people")]
public sealed class PeopleController(IPeopleDirectoryService peopleService) : ControllerBase
{
    [Authorize(Policy = SecurityPermissions.UsersRead)]
    [HttpGet("customers")]
    public async Task<ActionResult<IReadOnlyList<PersonResponse>>> GetCustomers(
        [FromQuery] string? query,
        CancellationToken cancellationToken) =>
        Ok((await peopleService.GetCustomersAsync(query, cancellationToken)).Select(Map).ToArray());

    [Authorize(Policy = SecurityPermissions.UsersManage)]
    [HttpPost("customers")]
    public async Task<ActionResult<PersonResponse>> CreateCustomer(
        CreateCustomerRequest request,
        CancellationToken cancellationToken)
    {
        var person = await peopleService.CreateCustomerAsync(
            new CreateCustomerCommand(
                request.DisplayName,
                request.PhoneNumber,
                request.TemporaryPassword),
            cancellationToken);
        return CreatedAtAction(nameof(GetCustomers), Map(person));
    }

    [Authorize(Policy = SecurityPermissions.UsersRead)]
    [HttpGet("employees")]
    public async Task<ActionResult<IReadOnlyList<PersonResponse>>> GetEmployees(
        [FromQuery] string? query,
        CancellationToken cancellationToken) =>
        Ok((await peopleService.GetEmployeesAsync(query, cancellationToken)).Select(Map).ToArray());

    [Authorize(Policy = SecurityPermissions.AdminsManage)]
    [HttpPost("employees")]
    public async Task<ActionResult<PersonResponse>> CreateEmployee(
        CreateEmployeeRequest request,
        CancellationToken cancellationToken)
    {
        var person = await peopleService.CreateEmployeeAsync(
            new CreateEmployeeCommand(
                request.DisplayName,
                request.Email,
                request.PhoneNumber,
                request.TemporaryPassword,
                request.RoleName),
            cancellationToken);
        return CreatedAtAction(nameof(GetEmployees), Map(person));
    }

    private static PersonResponse Map(PersonInfo person) => new()
    {
        Id = person.Id,
        DisplayName = person.DisplayName,
        Email = person.Email,
        PhoneNumber = person.PhoneNumber,
        IsActive = person.IsActive,
        MfaEnabled = person.MfaEnabled,
        Roles = person.Roles,
        OrderCount = person.OrderCount,
        InvoiceCount = person.InvoiceCount,
        AddressCount = person.AddressCount,
        CreatedAt = person.CreatedAt,
        LastActivityAt = person.LastActivityAt
    };
}
