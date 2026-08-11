using System.Reflection;
using GoldInvoice.Api.Controllers;
using GoldInvoice.Api.Integration;
using GoldInvoice.Application.Integration;
using GoldInvoice.Application.Security;
using Microsoft.AspNetCore.Authorization;

namespace GoldInvoice.IntegrationTests;

public sealed class PhaseSixApiTests
{
    [Fact]
    public void IntegrationHub_IsAuthorizedAndExposesNoArbitraryGroupJoinMethod()
    {
        Assert.NotNull(typeof(IntegrationHub).GetCustomAttribute<AuthorizeAttribute>());
        var declaredPublicMethods = typeof(IntegrationHub)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.Collection(
            declaredPublicMethods,
            method => Assert.Equal(nameof(IntegrationHub.OnConnectedAsync), method.Name));
    }

    [Fact]
    public void DeadLetterOperations_RequireExplicitReprocessPermission()
    {
        var methods = typeof(IntegrationController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.Name is "GetDeadLetters" or "Reprocess")
            .ToArray();

        Assert.Equal(2, methods.Length);
        Assert.All(methods, method => Assert.Contains(
            method.GetCustomAttributes<AuthorizeAttribute>(),
            attribute => attribute.Policy == SecurityPermissions.OutboxReprocess));
    }

    [Fact]
    public void VersionedRealtimeContracts_ExcludeCustomerIdentityAndProviderSecrets()
    {
        var publicPropertyNames = new[]
            {
                typeof(InvoiceCreatedV1),
                typeof(InventoryChangedV1),
                typeof(OrderStatusChangedV1),
                typeof(MarketPriceUpdatedV1)
            }
            .SelectMany(type => type.GetProperties())
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("CustomerName", publicPropertyNames);
        Assert.DoesNotContain("NationalId", publicPropertyNames);
        Assert.DoesNotContain("ConfigurationReference", publicPropertyNames);
        Assert.DoesNotContain("RawPayload", publicPropertyNames);
        Assert.DoesNotContain("Secret", publicPropertyNames);
    }
}
