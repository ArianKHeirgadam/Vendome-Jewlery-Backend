using GoldInvoice.Application.Business;
using GoldInvoice.Application.Common;
using GoldInvoice.Domain.Business;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoldInvoice.Infrastructure.Business;

internal sealed class SupplierService(GoldInvoiceDbContext dbContext) : ISupplierService
{
    private const int MaximumPageSize = 100;

    public async Task<PagedResult<SupplierInfo>> GetSuppliersAsync(
        int page,
        int pageSize,
        string? query,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        ValidatePagination(page, pageSize);
        var suppliers = dbContext.Suppliers.AsNoTracking();
        if (!includeInactive)
        {
            suppliers = suppliers.Where(supplier => supplier.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            suppliers = suppliers.Where(supplier =>
                supplier.Code.Contains(term) ||
                supplier.Name.Contains(term) ||
                (supplier.ContactName != null && supplier.ContactName.Contains(term)) ||
                (supplier.PhoneNumber != null && supplier.PhoneNumber.Contains(term)));
        }

        var totalCount = await suppliers.CountAsync(cancellationToken);
        var items = await suppliers
            .OrderBy(supplier => supplier.Name)
            .ThenBy(supplier => supplier.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<SupplierInfo>(
            items.Select(Map).ToArray(),
            page,
            pageSize,
            totalCount);
    }

    public async Task<SupplierInfo> GetSupplierAsync(
        Guid supplierId,
        CancellationToken cancellationToken)
    {
        var supplier = await dbContext.Suppliers
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == supplierId, cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        return Map(supplier);
    }

    public async Task<SupplierInfo> CreateSupplierAsync(
        CreateSupplierCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var supplier = new Supplier(
            command.Code,
            command.Name,
            command.ContactName,
            command.PhoneNumber,
            command.Email,
            command.NationalId,
            command.AddressLine,
            command.Notes);
        dbContext.Suppliers.Add(supplier);
        await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken);
        return Map(supplier);
    }

    public async Task<SupplierInfo> UpdateSupplierAsync(
        Guid supplierId,
        UpdateSupplierCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var supplier = await dbContext.Suppliers.FindAsync([supplierId], cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        PersistenceUtilities.SetOriginalRowVersion(dbContext, supplier, command.RowVersion);
        supplier.Update(
            command.Code,
            command.Name,
            command.ContactName,
            command.PhoneNumber,
            command.Email,
            command.NationalId,
            command.AddressLine,
            command.Notes,
            command.IsActive);
        await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken);
        return Map(supplier);
    }

    public async Task DeleteSupplierAsync(Guid supplierId, CancellationToken cancellationToken)
    {
        var supplier = await dbContext.Suppliers.FindAsync([supplierId], cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        dbContext.Suppliers.Remove(supplier);
        await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken);
    }

    private static SupplierInfo Map(Supplier supplier) => new(
        supplier.Id,
        supplier.Code,
        supplier.Name,
        supplier.ContactName,
        supplier.PhoneNumber,
        supplier.Email,
        supplier.NationalId,
        supplier.AddressLine,
        supplier.Notes,
        supplier.IsActive,
        supplier.CreatedAt,
        supplier.UpdatedAt,
        Convert.ToBase64String(supplier.RowVersion));

    private static void ValidatePagination(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }
    }
}
