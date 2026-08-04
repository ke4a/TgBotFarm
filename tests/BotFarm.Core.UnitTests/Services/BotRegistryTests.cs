using BotFarm.Core.Abstractions;
using BotFarm.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class BotRegistryTests
{
    [Test]
    public void GetService_WithRegisteredKeyedService_ReturnsResolvedService()
    {
        var expectedService = new TestNamedService();
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ITestNamedService>("alpha", expectedService);
        using var serviceProvider = services.BuildServiceProvider();
        var registry = new BotRegistry(serviceProvider, []);

        var result = registry.GetService<ITestNamedService>("alpha");

        Assert.That(result, Is.SameAs(expectedService));
    }

    [Test]
    public void HasService_WithRegisteredKeyedService_ReturnsTrue()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ITestNamedService>("alpha", new TestNamedService());
        using var serviceProvider = services.BuildServiceProvider();
        var registry = new BotRegistry(serviceProvider, []);

        var result = registry.HasService<ITestNamedService>("alpha");

        Assert.That(result, Is.True);
    }

    [Test]
    public void HasService_WithMissingKeyedService_ReturnsFalse()
    {
        var services = new ServiceCollection();
        using var serviceProvider = services.BuildServiceProvider();
        var registry = new BotRegistry(serviceProvider, []);

        var result = registry.HasService<ITestNamedService>("missing");

        Assert.That(result, Is.False);
    }

    [Test]
    public void AllBotServices_ReturnsInjectedCollectionAsIs()
    {
        var allBotServices = new List<IBotService> { Substitute.For<IBotService>(), Substitute.For<IBotService>() };
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var registry = new BotRegistry(serviceProvider, allBotServices);

        Assert.That(registry.AllBotServices, Is.SameAs(allBotServices));
    }

    private interface ITestNamedService;

    private sealed class TestNamedService : ITestNamedService;
}
