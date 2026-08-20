using GoldInvoice.Application.Common;
using GoldInvoice.Application.People;
using GoldInvoice.Application.Security;
using GoldInvoice.Infrastructure.Identity;
using GoldInvoice.Infrastructure.Persistence;
using GoldInvoice.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GoldInvoice.Infrastructure.People;

internal sealed class PeopleDirectoryService(
    GoldInvoiceDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    AccessResolutionCache accessCache) : IPeopleDirectoryService
{
    public Task<IReadOnlyList<PersonInfo>> GetCustomersAsync(
        string? query,
        CancellationToken cancellationToken) =>
        GetPeopleAsync([SecurityRoles.Customer], query, cancellationToken);

    public Task<IReadOnlyList<PersonInfo>> GetEmployeesAsync(
        string? query,
        CancellationToken cancellationToken) =>
        GetPeopleAsync([SecurityRoles.Owner, SecurityRoles.Admin, SecurityRoles.Employee], query, cancellationToken);

    public Task<PersonInfo> CreateCustomerAsync(
        CreateCustomerCommand command,
        CancellationToken cancellationToken) =>
        CreateCustomerAccountAsync(command, cancellationToken);

    public Task<PersonInfo> CreateEmployeeAsync(
        CreateEmployeeCommand command,
        CancellationToken cancellationToken) =>
        CreateEmployeeAccountAsync(
            command.DisplayName,
            command.Email,
            command.PhoneNumber,
            command.TemporaryPassword,
            command.RoleName,
            cancellationToken);

    private async Task<IReadOnlyList<PersonInfo>> GetPeopleAsync(
        IReadOnlyCollection<string> roleNames,
        string? query,
        CancellationToken cancellationToken)
    {
        var peopleQuery =
            from user in dbContext.Users.AsNoTracking()
            join userRole in dbContext.UserRoles.AsNoTracking()
                on user.Id equals userRole.UserId
            join role in dbContext.Roles.AsNoTracking()
                on userRole.RoleId equals role.Id
            where role.Name != null && roleNames.Contains(role.Name)
            select new { User = user, RoleName = role.Name };

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            peopleQuery = roleNames.Contains(SecurityRoles.Customer)
                ? peopleQuery.Where(item =>
                    item.User.DisplayName.Contains(term) ||
                    (item.User.PhoneNumber != null && item.User.PhoneNumber.Contains(term)))
                : peopleQuery.Where(item =>
                    item.User.DisplayName.Contains(term) ||
                    (item.User.Email != null && item.User.Email.Contains(term)) ||
                    (item.User.PhoneNumber != null && item.User.PhoneNumber.Contains(term)));
        }

        var rows = await peopleQuery
            .OrderBy(item => item.User.DisplayName)
            .ThenBy(item => item.User.Id)
            .ToListAsync(cancellationToken);
        var users = rows
            .GroupBy(item => item.User.Id)
            .Select(group => new
            {
                User = group.First().User,
                Roles = group.Select(item => item.RoleName!).Distinct().OrderBy(name => name).ToArray()
            })
            .ToArray();
        return await EnrichAsync(users.Select(item => item.User).ToArray(),
            users.ToDictionary(item => item.User.Id, item => (IReadOnlyList<string>)item.Roles),
            cancellationToken);
    }

    private async Task<PersonInfo> CreateCustomerAccountAsync(
        CreateCustomerCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var phoneNumber = ContactIdentifierNormalizer.NormalizePhoneNumber(command.PhoneNumber);
        if (await userManager.FindByNameAsync(phoneNumber) is not null ||
            await dbContext.Users.AnyAsync(
                user => user.PhoneNumber == phoneNumber,
                cancellationToken))
        {
            throw new ApplicationConflictException();
        }

        var user = new ApplicationUser(command.DisplayName)
        {
            UserName = phoneNumber,
            PhoneNumber = phoneNumber,
            PhoneNumberConfirmed = true
        };
        return await CreatePersonAsync(
            user,
            command.TemporaryPassword,
            SecurityRoles.Customer,
            cancellationToken);
    }

    private async Task<PersonInfo> CreateEmployeeAccountAsync(
        string displayName,
        string email,
        string phoneNumber,
        string temporaryPassword,
        string roleName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedRole = roleName?.Trim() switch
        {
            SecurityRoles.Admin => SecurityRoles.Admin,
            SecurityRoles.Employee => SecurityRoles.Employee,
            _ => throw new ArgumentException(
                "Employee role must be Admin or Employee.",
                nameof(roleName))
        };

        var normalizedEmail = email.Trim();
        var normalizedPhoneNumber =
            ContactIdentifierNormalizer.NormalizePhoneNumber(phoneNumber);

        if (await userManager.FindByEmailAsync(normalizedEmail) is not null ||
            await userManager.FindByNameAsync(normalizedPhoneNumber) is not null ||
            await dbContext.Users.AnyAsync(
                user => user.PhoneNumber == normalizedPhoneNumber,
                cancellationToken))
        {
            throw new ApplicationConflictException();
        }

        var user = new ApplicationUser(displayName)
        {
            Email = normalizedEmail,
            EmailConfirmed = true,

            // All non-owner staff sign in with mobile number.
            UserName = normalizedPhoneNumber,
            PhoneNumber = normalizedPhoneNumber,
            PhoneNumberConfirmed = true
        };

        // Non-owner staff are intentionally created without an MFA requirement.
        return await CreatePersonAsync(
            user,
            temporaryPassword,
            normalizedRole,
            cancellationToken);
    }
    private async Task<PersonInfo> CreatePersonAsync(
        ApplicationUser user,
        string temporaryPassword,
        string roleName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var created = await userManager.CreateAsync(user, temporaryPassword);
        if (!created.Succeeded)
        {
            throw new ArgumentException("The account details or temporary password are invalid.");
        }

        var roleResult = await userManager.AddToRoleAsync(user, roleName);
        if (!roleResult.Succeeded)
        {
            await userManager.DeleteAsync(user);
            throw new ApplicationConflictException();
        }

        accessCache.Invalidate(user.Id);

        return (await EnrichAsync(
            [user],
            new Dictionary<Guid, IReadOnlyList<string>> { [user.Id] = [roleName] },
            cancellationToken))[0];
    }

    private async Task<IReadOnlyList<PersonInfo>> EnrichAsync(
        IReadOnlyList<ApplicationUser> users,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> roles,
        CancellationToken cancellationToken)
    {
        if (users.Count == 0)
        {
            return [];
        }

        var ids = users.Select(user => user.Id).ToArray();
        var orderStats = await dbContext.Orders
            .AsNoTracking()
            .Where(order => ids.Contains(order.CustomerId))
            .GroupBy(order => order.CustomerId)
            .Select(group => new
            {
                CustomerId = group.Key,
                Count = group.Count(),
                LastAt = group.Max(order => order.UpdatedAt)
            })
            .ToDictionaryAsync(item => item.CustomerId, cancellationToken);
        var invoiceStats = await dbContext.Invoices
            .AsNoTracking()
            .Where(invoice => ids.Contains(invoice.CustomerId))
            .GroupBy(invoice => invoice.CustomerId)
            .Select(group => new
            {
                CustomerId = group.Key,
                Count = group.Count(),
                LastAt = group.Max(invoice => invoice.IssuedAt)
            })
            .ToDictionaryAsync(item => item.CustomerId, cancellationToken);
        var addressCounts = await dbContext.CustomerAddresses
            .AsNoTracking()
            .Where(address => ids.Contains(address.CustomerId))
            .GroupBy(address => address.CustomerId)
            .Select(group => new { CustomerId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.CustomerId, item => item.Count, cancellationToken);

        return users.Select(user =>
        {
            var userRoles = roles[user.Id];
            orderStats.TryGetValue(user.Id, out var orders);
            invoiceStats.TryGetValue(user.Id, out var invoices);
            var lastActivity = orders?.LastAt;
            if (invoices is not null && (lastActivity is null || invoices.LastAt > lastActivity))
            {
                lastActivity = invoices.LastAt;
            }

            return new PersonInfo(
                user.Id,
                user.DisplayName,
                userRoles.Contains(SecurityRoles.Customer, StringComparer.Ordinal)
                    ? null
                    : user.Email,
                user.PhoneNumber,
                user.IsActive,
                user.TwoFactorEnabled,
                userRoles,
                orders?.Count ?? 0,
                invoices?.Count ?? 0,
                addressCounts.GetValueOrDefault(user.Id),
                user.CreatedAt,
                lastActivity);
        }).ToArray();
    }
}
