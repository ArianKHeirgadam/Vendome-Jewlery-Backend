using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoldInvoice.Infrastructure.Security;

internal sealed record ResolvedAccess(
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

internal static class SecurityAccessQueries
{
    public static async Task<ResolvedAccess> ResolveAsync(
        GoldInvoiceDbContext dbContext,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var roles = await (
                from userRole in dbContext.UserRoles
                join role in dbContext.Roles on userRole.RoleId equals role.Id
                where userRole.UserId == userId && role.Name != null
                orderby role.Name
                select role.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var permissions = await (
                from userRole in dbContext.UserRoles
                join rolePermission in dbContext.RolePermissions
                    on userRole.RoleId equals rolePermission.RoleId
                join permission in dbContext.Permissions
                    on rolePermission.PermissionId equals permission.Id
                where userRole.UserId == userId && permission.IsActive
                orderby permission.Name
                select permission.Name)
            .AsNoTracking()
            .Distinct()
            .ToListAsync(cancellationToken);

        return new ResolvedAccess(roles, permissions);
    }
}
