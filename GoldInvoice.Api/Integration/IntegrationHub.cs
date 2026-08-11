using GoldInvoice.Api.Security;
using GoldInvoice.Application.Security;
using GoldInvoice.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace GoldInvoice.Api.Integration;

[Authorize]
public sealed class IntegrationHub(GoldInvoiceDbContext dbContext) : Hub
{
    public const string Route = "/hubs/integration";

    public override async Task OnConnectedAsync()
    {
        var user = Context.User ?? throw new HubException("Authentication is required.");
        var userId = user.GetRequiredUserId();
        await Groups.AddToGroupAsync(Context.ConnectionId, IntegrationHubGroups.User(userId));

        var roles = user.FindAll(SecurityClaimNames.Role)
            .Select(claim => claim.Value)
            .Where(role => SecurityRoles.All.Contains(role, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (var role in roles)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, IntegrationHubGroups.Role(role));
        }

        var deviceValue = Context.GetHttpContext()?.Request.Query["deviceId"].ToString();
        if (!string.IsNullOrWhiteSpace(deviceValue))
        {
            if (!Guid.TryParse(deviceValue, out var deviceId) ||
                !await dbContext.DesktopDevices.AsNoTracking().AnyAsync(
                    device => device.Id == deviceId &&
                        device.RegisteredByUserId == userId &&
                        device.IsActive,
                    Context.ConnectionAborted))
            {
                Context.Abort();
                throw new HubException("The device identity is not approved for this connection.");
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, IntegrationHubGroups.Device(deviceId));
        }

        await base.OnConnectedAsync();
    }
}

internal static class IntegrationHubGroups
{
    public static string User(Guid userId) => $"user:{userId:N}";

    public static string Role(string role) => $"role:{role.Trim().ToLowerInvariant()}";

    public static string Device(Guid deviceId) => $"device:{deviceId:N}";
}
