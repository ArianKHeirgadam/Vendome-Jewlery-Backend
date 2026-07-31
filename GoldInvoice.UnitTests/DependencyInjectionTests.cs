using GoldInvoice.Application;
using Microsoft.Extensions.DependencyInjection;

namespace GoldInvoice.UnitTests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public void AddApplication_ReturnsTheSameServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddApplication();

        Assert.Same(services, result);
    }

    [Fact]
    public void AddApplication_WithNullServiceCollection_Throws()
    {
        IServiceCollection services = null!;

        Assert.Throws<ArgumentNullException>(() => services.AddApplication());
    }
}
