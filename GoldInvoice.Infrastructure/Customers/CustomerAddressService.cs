using System.Data;
using GoldInvoice.Application.Common;
using GoldInvoice.Application.Customers;
using GoldInvoice.Domain.Customers;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoldInvoice.Infrastructure.Customers;

internal sealed class CustomerAddressService(GoldInvoiceDbContext dbContext) : ICustomerAddressService
{
    private const int MaximumAddressesPerCustomer = 100;

    public async Task<IReadOnlyList<CustomerAddressInfo>> GetAddressesAsync(
        Guid actorUserId,
        Guid customerId,
        bool canManageCustomer,
        CancellationToken cancellationToken)
    {
        EnsureAccess(actorUserId, customerId, canManageCustomer);
        return (await dbContext.CustomerAddresses
                .AsNoTracking()
                .Where(address => address.CustomerId == customerId)
                .OrderByDescending(address => address.IsDefault)
                .ThenBy(address => address.CreatedAt)
                .Take(MaximumAddressesPerCustomer)
                .ToListAsync(cancellationToken))
            .Select(Map)
            .ToArray();
    }

    public async Task<CustomerAddressInfo> GetAddressAsync(
        Guid addressId,
        Guid actorUserId,
        bool canManageCustomer,
        CancellationToken cancellationToken)
    {
        var address = await dbContext.CustomerAddresses
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == addressId, cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        EnsureAccess(actorUserId, address.CustomerId, canManageCustomer);
        return Map(address);
    }

    public async Task<CustomerAddressInfo> CreateAddressAsync(
        CreateCustomerAddressCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureAccess(command.ActorUserId, command.CustomerId, command.CanManageCustomer);
        await using var transaction = await BeginSerializableTransactionAsync(cancellationToken);
        if (!await dbContext.Users.AnyAsync(
                user => user.Id == command.CustomerId && user.IsActive,
                cancellationToken))
        {
            throw new ApplicationResourceNotFoundException();
        }

        var addressCount = await dbContext.CustomerAddresses.CountAsync(
            address => address.CustomerId == command.CustomerId,
            cancellationToken);
        if (addressCount >= MaximumAddressesPerCustomer)
        {
            throw new ApplicationConflictException();
        }

        var shouldBeDefault = command.IsDefault || addressCount == 0;
        if (shouldBeDefault)
        {
            if (await ClearCurrentDefaultAsync(
                    command.CustomerId,
                    exceptAddressId: null,
                    cancellationToken))
            {
                await SaveChangesAsync(cancellationToken);
            }
        }

        var address = new CustomerAddress(
            command.CustomerId,
            command.Title,
            command.RecipientName,
            command.PhoneNumber,
            command.Province,
            command.City,
            command.PostalCode,
            command.AddressLine,
            shouldBeDefault);
        dbContext.CustomerAddresses.Add(address);
        await SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);
        return Map(address);
    }

    public async Task<CustomerAddressInfo> UpdateAddressAsync(
        Guid addressId,
        UpdateCustomerAddressCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureAccess(command.ActorUserId, command.CustomerId, command.CanManageCustomer);
        await using var transaction = await BeginSerializableTransactionAsync(cancellationToken);
        var address = await dbContext.CustomerAddresses.FindAsync([addressId], cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        if (address.CustomerId != command.CustomerId)
        {
            throw new ApplicationResourceNotFoundException();
        }

        SetOriginalRowVersion(address, command.RowVersion);
        if (command.IsDefault)
        {
            if (await ClearCurrentDefaultAsync(command.CustomerId, address.Id, cancellationToken))
            {
                await SaveChangesAsync(cancellationToken);
            }
        }
        else if (address.IsDefault)
        {
            var replacement = await dbContext.CustomerAddresses
                .Where(candidate => candidate.CustomerId == command.CustomerId && candidate.Id != address.Id)
                .OrderBy(candidate => candidate.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken) ?? throw new ApplicationConflictException();
            address.Update(
                command.Title,
                command.RecipientName,
                command.PhoneNumber,
                command.Province,
                command.City,
                command.PostalCode,
                command.AddressLine,
                isDefault: false);
            await SaveChangesAsync(cancellationToken);
            replacement.Update(
                replacement.Title,
                replacement.RecipientName,
                replacement.PhoneNumber,
                replacement.Province,
                replacement.City,
                replacement.PostalCode,
                replacement.AddressLine,
                isDefault: true);
            await SaveChangesAsync(cancellationToken);
            await CommitAsync(transaction, cancellationToken);
            return Map(address);
        }

        address.Update(
            command.Title,
            command.RecipientName,
            command.PhoneNumber,
            command.Province,
            command.City,
            command.PostalCode,
            command.AddressLine,
            command.IsDefault);
        await SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);
        return Map(address);
    }

    public async Task DeleteAddressAsync(
        Guid addressId,
        Guid actorUserId,
        bool canManageCustomer,
        string rowVersion,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginSerializableTransactionAsync(cancellationToken);
        var address = await dbContext.CustomerAddresses.FindAsync([addressId], cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        EnsureAccess(actorUserId, address.CustomerId, canManageCustomer);
        SetOriginalRowVersion(address, rowVersion);

        CustomerAddress? replacement = null;
        if (address.IsDefault)
        {
            replacement = await dbContext.CustomerAddresses
                .Where(candidate => candidate.CustomerId == address.CustomerId && candidate.Id != address.Id)
                .OrderBy(candidate => candidate.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        dbContext.CustomerAddresses.Remove(address);
        await SaveChangesAsync(cancellationToken);
        if (replacement is not null)
        {
            replacement.Update(
                replacement.Title,
                replacement.RecipientName,
                replacement.PhoneNumber,
                replacement.Province,
                replacement.City,
                replacement.PostalCode,
                replacement.AddressLine,
                isDefault: true);
        }

        if (replacement is not null)
        {
            await SaveChangesAsync(cancellationToken);
        }
        await CommitAsync(transaction, cancellationToken);
    }

    private async Task<bool> ClearCurrentDefaultAsync(
        Guid customerId,
        Guid? exceptAddressId,
        CancellationToken cancellationToken)
    {
        var defaults = await dbContext.CustomerAddresses
            .Where(address =>
                address.CustomerId == customerId &&
                address.IsDefault &&
                address.Id != exceptAddressId)
            .ToListAsync(cancellationToken);
        foreach (var address in defaults)
        {
            address.ClearDefault();
        }

        return defaults.Count > 0;
    }

    private static void EnsureAccess(Guid actorUserId, Guid customerId, bool canManageCustomer)
    {
        if (actorUserId == Guid.Empty || customerId == Guid.Empty)
        {
            throw new ArgumentException("Valid user identifiers are required.");
        }

        if (!canManageCustomer && actorUserId != customerId)
        {
            throw new ApplicationResourceNotFoundException();
        }
    }

    private static CustomerAddressInfo Map(CustomerAddress address) => new(
        address.Id,
        address.CustomerId,
        address.Title,
        address.RecipientName,
        address.PhoneNumber,
        address.Province,
        address.City,
        address.PostalCode,
        address.AddressLine,
        address.IsDefault,
        Convert.ToBase64String(address.RowVersion));

    private void SetOriginalRowVersion(CustomerAddress address, string value) =>
        dbContext.Entry(address).Property(item => item.RowVersion).OriginalValue = DecodeRowVersion(value);

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ApplicationConcurrencyException();
        }
        catch (DbUpdateException)
        {
            throw new ApplicationConflictException();
        }
    }

    private async Task<IDbContextTransaction?> BeginSerializableTransactionAsync(
        CancellationToken cancellationToken) =>
        dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

    private static async Task CommitAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static byte[] DecodeRowVersion(string value)
    {
        try
        {
            return Convert.FromBase64String(value ?? string.Empty);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("The concurrency token is invalid.", nameof(value), exception);
        }
    }
}
