using System.Data;
using GoldInvoice.Application.Security;
using GoldInvoice.Domain.Security;
using GoldInvoice.Infrastructure.Configuration;
using GoldInvoice.Infrastructure.Identity;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GoldInvoice.Infrastructure.Security;

internal sealed class SecurityBootstrapHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<BootstrapOwnerOptions> bootstrapOptions,
    ILogger<SecurityBootstrapHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<GoldInvoiceDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        await using var transaction = await BeginBootstrapTransactionAsync(dbContext, cancellationToken);
        if (transaction is not null && dbContext.Database.IsSqlServer())
        {
            await AcquireBootstrapLockAsync(dbContext, cancellationToken);
        }

        await SeedRolesAsync(roleManager);
        await SeedPermissionsAsync(dbContext, cancellationToken);
        await GrantOwnerPermissionsAsync(dbContext, roleManager, cancellationToken);
        await GrantStaffPermissionsAsync(dbContext, roleManager, cancellationToken);

        if (bootstrapOptions.Value.Enabled)
        {
            await BootstrapOwnerAsync(userManager, cancellationToken);
        }

        await NormalizeNonOwnerStaffSecurityAsync(
            userManager,
            cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        logger.LogInformation("Security roles and permissions are initialized");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task<IDbContextTransaction?> BeginBootstrapTransactionAsync(
        GoldInvoiceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            return null;
        }

        return await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
    }

    private static Task<int> AcquireBootstrapLockAsync(
        GoldInvoiceDbContext dbContext,
        CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlRawAsync(
            """
            DECLARE @LockResult int;
            EXEC @LockResult = sys.sp_getapplock
                @Resource = N'GoldInvoice.SecurityBootstrap.v1',
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 15000;
            IF @LockResult < 0
                THROW 51000, 'Security bootstrap lock could not be acquired.', 1;
            """,
            cancellationToken);

    private static async Task SeedRolesAsync(RoleManager<ApplicationRole> roleManager)
    {
        foreach (var roleName in SecurityRoles.All)
        {
            if (await roleManager.RoleExistsAsync(roleName))
            {
                continue;
            }

            var description = roleName switch
            {
                SecurityRoles.Owner => "System owner with all permissions.",
                SecurityRoles.Admin => "Administrator with explicitly granted permissions.",
                SecurityRoles.Employee => "Staff member with sales and operational permissions.",
                _ => "Customer limited to owned resources."
            };
            var result = await roleManager.CreateAsync(new ApplicationRole(roleName, description, isSystem: true));
            ThrowIfFailed(result, "A required security role could not be created.");
        }
    }

    private static async Task SeedPermissionsAsync(
        GoldInvoiceDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var existingNames = await dbContext.Permissions
            .Select(permission => permission.Name)
            .ToListAsync(cancellationToken);
        var existing = existingNames.ToHashSet(StringComparer.Ordinal);

        foreach (var definition in SecurityPermissions.All.Where(item => !existing.Contains(item.Name)))
        {
            dbContext.Permissions.Add(new Permission(
                definition.Name,
                definition.DisplayName,
                definition.Group));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task GrantOwnerPermissionsAsync(
        GoldInvoiceDbContext dbContext,
        RoleManager<ApplicationRole> roleManager,
        CancellationToken cancellationToken)
    {
        var ownerRole = await roleManager.FindByNameAsync(SecurityRoles.Owner) ??
            throw new InvalidOperationException("The owner role was not initialized.");
        var permissionIds = await dbContext.Permissions
            .Where(permission => permission.IsActive)
            .Select(permission => permission.Id)
            .ToListAsync(cancellationToken);
        var existingPermissionIds = await dbContext.RolePermissions
            .Where(rolePermission => rolePermission.RoleId == ownerRole.Id)
            .Select(rolePermission => rolePermission.PermissionId)
            .ToListAsync(cancellationToken);
        var existing = existingPermissionIds.ToHashSet();

        foreach (var permissionId in permissionIds.Where(id => !existing.Contains(id)))
        {
            dbContext.RolePermissions.Add(new RolePermission(ownerRole.Id, permissionId));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task GrantStaffPermissionsAsync(
        GoldInvoiceDbContext dbContext,
        RoleManager<ApplicationRole> roleManager,
        CancellationToken cancellationToken)
    {
        string[] adminPermissions =
        [
            SecurityPermissions.UsersRead,
            SecurityPermissions.UsersManage,
            SecurityPermissions.ProductsRead,
            SecurityPermissions.ProductsManage,
            SecurityPermissions.InventoryRead,
            SecurityPermissions.InventoryAdjust,
            SecurityPermissions.OrdersRead,
            SecurityPermissions.OrdersManage,
            SecurityPermissions.PaymentsRead,
            SecurityPermissions.PaymentsManage,
            SecurityPermissions.InvoicesRead,
            SecurityPermissions.InvoicesPrint,
            SecurityPermissions.InvoicesReprint,
            SecurityPermissions.ReportsRead,
            SecurityPermissions.SuppliersRead,
            SecurityPermissions.SuppliersManage,
            SecurityPermissions.CrmRead,
            SecurityPermissions.CrmManage,
            SecurityPermissions.SettingsRead
        ];

        string[] employeePermissions =
        [
            SecurityPermissions.UsersRead,
            SecurityPermissions.UsersManage,
            SecurityPermissions.ProductsRead,
            SecurityPermissions.InventoryRead,
            SecurityPermissions.OrdersRead,
            SecurityPermissions.OrdersManage,
            SecurityPermissions.PaymentsRead,
            SecurityPermissions.PaymentsManage,
            SecurityPermissions.InvoicesRead,
            SecurityPermissions.InvoicesPrint,
            SecurityPermissions.ReportsRead,
            SecurityPermissions.SuppliersRead,
            SecurityPermissions.CrmRead,
            SecurityPermissions.SettingsRead
        ];

        await GrantRolePermissionsAsync(
            dbContext,
            roleManager,
            SecurityRoles.Admin,
            adminPermissions,
            cancellationToken);

        await GrantRolePermissionsAsync(
            dbContext,
            roleManager,
            SecurityRoles.Employee,
            employeePermissions,
            cancellationToken);
    }

    private static async Task GrantRolePermissionsAsync(
        GoldInvoiceDbContext dbContext,
        RoleManager<ApplicationRole> roleManager,
        string roleName,
        IReadOnlyCollection<string> permissionNames,
        CancellationToken cancellationToken)
    {
        var role = await roleManager.FindByNameAsync(roleName) ??
            throw new InvalidOperationException(
                $"The {roleName} role was not initialized.");

        var permissionIds = await dbContext.Permissions
            .Where(permission =>
                permission.IsActive &&
                permissionNames.Contains(permission.Name))
            .Select(permission => permission.Id)
            .ToListAsync(cancellationToken);

        var existingIds = await dbContext.RolePermissions
            .Where(item => item.RoleId == role.Id)
            .Select(item => item.PermissionId)
            .ToListAsync(cancellationToken);

        var existing = existingIds.ToHashSet();

        foreach (var permissionId in permissionIds.Where(id =>
                     !existing.Contains(id)))
        {
            dbContext.RolePermissions.Add(
                new RolePermission(role.Id, permissionId));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task NormalizeNonOwnerStaffSecurityAsync(
        UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken)
    {
        var admins =
            await userManager.GetUsersInRoleAsync(SecurityRoles.Admin);
        var employees =
            await userManager.GetUsersInRoleAsync(SecurityRoles.Employee);

        var adminIds = admins
            .Select(user => user.Id)
            .ToHashSet();

        // All staff get their contact identifiers normalized. Admins are a
        // privileged role whose sessions require MFA (the access-token
        // validator rejects non-MFA tokens for them), so their MFA state must
        // survive every startup. Only employees are force-cleared; they sign
        // in from the sales floor without MFA.
        var staff = admins
            .Concat(employees)
            .GroupBy(user => user.Id)
            .Select(group => group.First())
            .ToArray();

        foreach (var user in staff)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var changed = false;

            if (!adminIds.Contains(user.Id))
            {
                if (user.MfaRequired)
                {
                    user.ClearMfaRequirement();
                    changed = true;
                }

                if (user.TwoFactorEnabled)
                {
                    ThrowIfFailed(
                        await userManager.SetTwoFactorEnabledAsync(
                            user,
                            false),
                        "Two-factor authentication could not be disabled for staff.");
                }
            }

            if (!string.IsNullOrWhiteSpace(user.PhoneNumber))
            {
                var normalizedPhone =
                    ContactIdentifierNormalizer.NormalizePhoneNumber(
                        user.PhoneNumber);

                if (!string.Equals(
                        normalizedPhone,
                        user.PhoneNumber,
                        StringComparison.Ordinal))
                {
                    user.PhoneNumber = normalizedPhone;
                    changed = true;
                }

                if (!user.PhoneNumberConfirmed)
                {
                    user.PhoneNumberConfirmed = true;
                    changed = true;
                }
            }

            if (changed)
            {
                ThrowIfFailed(
                    await userManager.UpdateAsync(user),
                    "A staff account could not be normalized.");
            }
        }
    }
    private async Task BootstrapOwnerAsync(
        UserManager<ApplicationUser> userManager,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = bootstrapOptions.Value;
        var owners = await userManager.GetUsersInRoleAsync(SecurityRoles.Owner);
        if (owners.Count > 0)
        {
            var configuredOwner = owners.FirstOrDefault(owner =>
                string.Equals(owner.NormalizedEmail, userManager.NormalizeEmail(options.Email), StringComparison.Ordinal));
            if (configuredOwner is not null && !configuredOwner.MfaRequired)
            {
                configuredOwner.RequireMfa();
                ThrowIfFailed(
                    await userManager.UpdateAsync(configuredOwner),
                    "The existing owner account could not be secured.");
            }

            return;
        }

        if (await userManager.FindByEmailAsync(options.Email) is not null)
        {
            throw new InvalidOperationException(
                "Owner bootstrap cannot elevate an existing non-owner account.");
        }

        var owner = new ApplicationUser(options.DisplayName)
        {
            Email = options.Email.Trim(),
            UserName = options.Email.Trim(),
            EmailConfirmed = true
        };
        owner.RequireMfa();

        ThrowIfFailed(
            await userManager.CreateAsync(owner, options.Password),
            "The initial owner account could not be created.");
        ThrowIfFailed(
            await userManager.AddToRoleAsync(owner, SecurityRoles.Owner),
            "The initial owner role assignment failed.");
    }

    private static void ThrowIfFailed(IdentityResult result, string message)
    {
        if (result.Succeeded)
        {
            return;
        }

        var errorCodes = string.Join(", ", result.Errors.Select(error => error.Code));
        throw new InvalidOperationException($"{message} Identity errors: {errorCodes}");
    }
}
