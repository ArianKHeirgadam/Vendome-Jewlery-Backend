using GoldInvoice.Application.Pricing;
using Microsoft.Extensions.DependencyInjection;

namespace GoldInvoice.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IProductPriceCalculator, ProductPriceCalculator>();

        return services;
    }
}
