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
        services.AddKeyedSingleton<ITestNamedService>("Alpha", expectedService);
        using var serviceProvider = services.BuildServiceProvider();
        var registry = new BotRegistry(serviceProvider, Modules("Alpha"));

        var result = registry.GetService<ITestNamedService>("Alpha");

        Assert.That(result, Is.SameAs(expectedService));
    }

    [Test]
    public void GetService_WithUnknownBotName_Throws()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ITestNamedService>("Alpha", new TestNamedService());
        using var serviceProvider = services.BuildServiceProvider();
        var registry = new BotRegistry(serviceProvider, Modules("Alpha"));

        Assert.Throws<InvalidOperationException>(() => registry.GetService<ITestNamedService>("missing"));
    }

    [Test]
    public void GetService_WithKnownBotWithoutService_Throws()
    {
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var registry = new BotRegistry(serviceProvider, Modules("Alpha"));

        Assert.Throws<InvalidOperationException>(() => registry.GetService<ITestNamedService>("Alpha"));
    }

    [Test]
    public void HasService_WithRegisteredKeyedService_ReturnsTrue()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ITestNamedService>("Alpha", new TestNamedService());
        using var serviceProvider = services.BuildServiceProvider();
        var registry = new BotRegistry(serviceProvider, Modules("Alpha"));

        Assert.That(registry.HasService<ITestNamedService>("Alpha"), Is.True);
    }

    [Test]
    public void HasService_WithMissingServiceOrUnknownBot_ReturnsFalse()
    {
        using var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var registry = new BotRegistry(serviceProvider, Modules("Alpha"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(registry.HasService<ITestNamedService>("Alpha"), Is.False);
            Assert.That(registry.HasService<ITestNamedService>("missing"), Is.False);
        }
    }

    [Test]
    public void GetServicesForAllBots_ReturnsEachBotsKeyedServiceInModuleOrder()
    {
        var beta = new TestNamedService();
        var alpha = new TestNamedService();
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ITestNamedService>("Alpha", alpha);
        services.AddKeyedSingleton<ITestNamedService>("Beta", beta);
        services.AddSingleton<ITestNamedService>(new TestNamedService());
        using var serviceProvider = services.BuildServiceProvider();
        var registry = new BotRegistry(serviceProvider, Modules("Beta", "Alpha"));

        var result = registry.GetServicesForAllBots<ITestNamedService>();

        Assert.That(result, Is.EqualTo(new ITestNamedService[] { beta, alpha }).AsCollection);
    }

    [Test]
    public void GetServicesForAllBots_WhenABotHasNoService_Throws()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ITestNamedService>("Alpha", new TestNamedService());
        using var serviceProvider = services.BuildServiceProvider();
        var registry = new BotRegistry(serviceProvider, Modules("Alpha", "Beta"));

        Assert.Throws<InvalidOperationException>(() => registry.GetServicesForAllBots<ITestNamedService>());
    }

    private static IBotModule[] Modules(params string[] names)
    {
        return names.Select(name =>
        {
            var module = Substitute.For<IBotModule>();
            module.Name.Returns(name);
            return module;
        }).ToArray();
    }

    private interface ITestNamedService;

    private sealed class TestNamedService : ITestNamedService;
}
