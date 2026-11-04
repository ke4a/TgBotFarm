using BotFarm.Core.Abstractions;
using BotFarm.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using TestBot.Services;

namespace TestBot.UnitTests;

[TestFixture]
public class TestBotModuleTests
{
    [Test]
    public void Discover_FindsTestBotModuleInTestBotAssembly()
    {
        var modules = BotModuleDiscovery.Discover(new[] { typeof(TestBotModule).Assembly });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(modules.Select(module => module.GetType()), Is.EqualTo(new[] { typeof(TestBotModule) }));
            Assert.That(modules[0].Name, Is.EqualTo(Constants.Name));
            Assert.That(modules[0].ComponentsAssembly, Is.SameAs(typeof(TestBotModule).Assembly));
        }
    }

    [Test]
    public void AddServices_RegistersTestBotServicesUnderItsName()
    {
        var services = new ServiceCollection();

        new TestBotModule().AddServices(services, new ConfigurationBuilder().Build());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(services.Any(descriptor =>
                descriptor.ServiceType == typeof(IUpdateService)
                && Equals(descriptor.ServiceKey, Constants.Name)
                && descriptor.KeyedImplementationType == typeof(TestBotUpdateService)), Is.True);
            Assert.That(services.Any(descriptor =>
                descriptor.ServiceType == typeof(IBotService)
                && Equals(descriptor.ServiceKey, Constants.Name)
                && descriptor.KeyedImplementationType == typeof(TestBotService)), Is.True);
        }
    }

    [Test]
    public void AddHealthChecks_AddsTestBotStatsHealthCheck()
    {
        var services = new ServiceCollection();

        new TestBotModule().AddHealthChecks(services.AddHealthChecks());

        using var provider = services.BuildServiceProvider();
        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        Assert.That(registrations.Select(registration => registration.Name), Does.Contain($"{Constants.Name}Stats"));
    }
}
