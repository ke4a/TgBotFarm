using BotFarm.Core.Abstractions;
using BotFarm.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using BotTemplate.Services;

namespace BotTemplate.UnitTests;

[TestFixture]
public class BotTemplateModuleTests
{
    [Test]
    public void Discover_FindsBotTemplateModuleInBotTemplateAssembly()
    {
        var modules = BotModuleDiscovery.Discover(new[] { typeof(BotTemplateModule).Assembly });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(modules.Select(module => module.GetType()), Is.EqualTo(new[] { typeof(BotTemplateModule) }));
            Assert.That(modules[0].Name, Is.EqualTo(Constants.Name));
            Assert.That(modules[0].ComponentsAssembly, Is.SameAs(typeof(BotTemplateModule).Assembly));
        }
    }

    [Test]
    public void AddServices_RegistersBotTemplateServicesUnderItsName()
    {
        var services = new ServiceCollection();

        new BotTemplateModule().AddServices(services, new ConfigurationBuilder().Build());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(services.Any(descriptor =>
                descriptor.ServiceType == typeof(IUpdateService)
                && Equals(descriptor.ServiceKey, Constants.Name)
                && descriptor.KeyedImplementationType == typeof(BotTemplateUpdateService)), Is.True);
            Assert.That(services.Any(descriptor =>
                descriptor.ServiceType == typeof(IBotService)
                && Equals(descriptor.ServiceKey, Constants.Name)
                && descriptor.KeyedImplementationType == typeof(BotTemplateService)), Is.True);
        }
    }

    [Test]
    public void AddHealthChecks_AddsBotTemplateStatsHealthCheck()
    {
        var services = new ServiceCollection();

        new BotTemplateModule().AddHealthChecks(services.AddHealthChecks());

        using var provider = services.BuildServiceProvider();
        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        Assert.That(registrations.Select(registration => registration.Name), Does.Contain($"{Constants.Name}Stats"));
    }
}
