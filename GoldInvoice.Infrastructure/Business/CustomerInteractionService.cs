using GoldInvoice.Application.Business;
using GoldInvoice.Application.Common;
using GoldInvoice.Application.Security;
using GoldInvoice.Domain.Business;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoldInvoice.Infrastructure.Business;

internal sealed class CustomerInteractionService(
    GoldInvoiceDbContext dbContext,
    TimeProvider timeProvider) : ICustomerInteractionService
{
    private const int MaximumPageSize = 100;

    public async Task<PagedResult<CustomerInteractionInfo>> GetInteractionsAsync(
        int page,
        int pageSize,
        Guid? customerId,
        CustomerInteractionStatus? status,
        CancellationToken cancellationToken)
    {
        ValidatePagination(page, pageSize);
        var query = dbContext.CustomerInteractions.AsNoTracking();
        if (customerId is not null)
        {
            query = query.Where(interaction => interaction.CustomerId == customerId);
        }

        if (status is not null)
        {
            query = query.Where(interaction => interaction.Status == status);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await (
                from interaction in query
                join user in dbContext.Users.AsNoTracking()
                    on interaction.CustomerId equals user.Id
                orderby interaction.OccurredAt descending, interaction.Id
                select new { Interaction = interaction, user.DisplayName })
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<CustomerInteractionInfo>(
            items.Select(item => Map(item.Interaction, item.DisplayName)).ToArray(),
            page,
            pageSize,
            totalCount);
    }

    public async Task<CustomerInteractionInfo> CreateInteractionAsync(
        CreateCustomerInteractionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var customerName = await GetCustomerNameAsync(command.CustomerId, cancellationToken);
        var interaction = new CustomerInteraction(
            command.CustomerId,
            command.InteractionType,
            command.Subject,
            command.Notes,
            command.OccurredAt,
            command.NextFollowUpAt);
        dbContext.CustomerInteractions.Add(interaction);
        await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken);
        return Map(interaction, customerName);
    }

    public async Task<CustomerInteractionInfo> ChangeStatusAsync(
        Guid interactionId,
        ChangeCustomerInteractionStatusCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var interaction = await dbContext.CustomerInteractions.FindAsync([interactionId], cancellationToken) ??
            throw new ApplicationResourceNotFoundException();
        PersistenceUtilities.SetOriginalRowVersion(dbContext, interaction, command.RowVersion);
        interaction.ChangeStatus(command.Status, timeProvider.GetUtcNow());
        await PersistenceUtilities.SaveChangesAsync(dbContext, cancellationToken);
        var customerName = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == interaction.CustomerId)
            .Select(user => user.DisplayName)
            .SingleAsync(cancellationToken);
        return Map(interaction, customerName);
    }

    private async Task<string> GetCustomerNameAsync(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        var customer = await (
                from user in dbContext.Users.AsNoTracking()
                join userRole in dbContext.UserRoles.AsNoTracking()
                    on user.Id equals userRole.UserId
                join role in dbContext.Roles.AsNoTracking()
                    on userRole.RoleId equals role.Id
                where user.Id == customerId &&
                    user.IsActive &&
                    role.Name == SecurityRoles.Customer
                select user.DisplayName)
            .SingleOrDefaultAsync(cancellationToken);
        return customer ?? throw new ApplicationResourceNotFoundException();
    }

    private static CustomerInteractionInfo Map(
        CustomerInteraction interaction,
        string customerName) => new(
        interaction.Id,
        interaction.CustomerId,
        customerName,
        interaction.InteractionType.ToString(),
        interaction.Subject,
        interaction.Notes,
        interaction.OccurredAt,
        interaction.NextFollowUpAt,
        interaction.Status.ToString(),
        interaction.CompletedAt,
        Convert.ToBase64String(interaction.RowVersion));

    private static void ValidatePagination(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }
    }
}
