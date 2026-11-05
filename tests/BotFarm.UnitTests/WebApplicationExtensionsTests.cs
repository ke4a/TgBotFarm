using BotFarm.Core.Abstractions;
using BotFarm.Core.Base;
using BotFarm.Core.Models;
using BotFarm.Core.Services;
using BotFarm.Extensions;
using BotFarm.TestKit;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BotFarm.UnitTests;

[TestFixture]
public class WebApplicationExtensionsTests
{
    [Test]
    public void AddBotFarm_UsesTheConfigurationProvidedByTheHost()
    {
        using var dataFolder = new TempDirectoryScope();
        var builder = CreateBuilder(dataFolder);

        builder.AddBotFarm();

        using var provider = builder.Services.BuildServiceProvider();
        var botConfig = provider.GetRequiredService<IOptionsMonitor<BotConfig>>().Get(TestBot.Constants.Name);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(botConfig.Token, Is.EqualTo("123456:FromHostConfiguration"));
            Assert.That(botConfig.AdminChatId, Is.EqualTo(42));
        }
    }

    [Test]
    public void AddBotFarm_RegistersTheReferencedBotModulesAndTheirServices()
    {
        using var dataFolder = new TempDirectoryScope();
        var builder = CreateBuilder(dataFolder);

        builder.AddBotFarm();

        using var provider = builder.Services.BuildServiceProvider();
        var modules = provider.GetServices<IBotModule>().ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(modules.Select(module => module.GetType()), Is.EqualTo(new[] { typeof(TestBot.TestBotModule) }));
            Assert.That(builder.Services.Any(descriptor =>
                descriptor.ServiceType == typeof(IUpdateService)
                && Equals(descriptor.ServiceKey, TestBot.Constants.Name)), Is.True);
        }
    }

    [Test]
    public void AddBotFarm_RegistersEachBotsIdentityKeyedByName()
    {
        using var dataFolder = new TempDirectoryScope();
        var builder = CreateBuilder(dataFolder);

        builder.AddBotFarm();

        using var provider = builder.Services.BuildServiceProvider();
        var keyed = provider.GetRequiredKeyedService<BotIdentity>(TestBot.Constants.Name);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(keyed.Name, Is.EqualTo(TestBot.Constants.Name));
            Assert.That(provider.GetServices<BotIdentity>(), Is.Empty);
        }
    }

    [Test]
    public void AddBotFarm_TestBotServicesReceiveTheBotsIdentity()
    {
        using var dataFolder = new TempDirectoryScope();
        var builder = CreateBuilder(dataFolder);

        builder.AddBotFarm();

        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(services.GetRequiredKeyedService<IBotService>(TestBot.Constants.Name).Name,
                Is.EqualTo(TestBot.Constants.Name));
            Assert.That(services.GetRequiredKeyedService<IUpdateService>(TestBot.Constants.Name).Name,
                Is.EqualTo(TestBot.Constants.Name));
            Assert.That(((MarkupService)services.GetRequiredService<TestBot.Abstractions.ITestBotMarkupService>()).Name,
                Is.EqualTo(TestBot.Constants.Name));
            Assert.That(services.GetRequiredService<TestBot.Services.TestBotDatabaseService>(), Is.Not.Null);
            Assert.That(services.GetKeyedServices<ICommandHandler>(TestBot.Constants.Name).ToList(), Has.Count.EqualTo(2));
            Assert.That(services.GetKeyedServices<ICallbackHandler>(TestBot.Constants.Name).ToList(), Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void AddBotFarm_RegistersTestBotsCoreServicesKeyedOnly()
    {
        using var dataFolder = new TempDirectoryScope();
        var builder = CreateBuilder(dataFolder);

        builder.AddBotFarm();

        using var provider = builder.Services.BuildServiceProvider();
        var databaseService = provider.GetRequiredService<TestBot.Services.TestBotDatabaseService>();
        var registry = provider.GetRequiredService<IBotRegistry>();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(provider.GetRequiredKeyedService<IDatabaseService>(TestBot.Constants.Name), Is.SameAs(databaseService));
            Assert.That(provider.GetRequiredKeyedService<IMongoDbDatabaseService>(TestBot.Constants.Name), Is.SameAs(databaseService));
            Assert.That(provider.GetRequiredKeyedService<IBotService>(TestBot.Constants.Name),
                Is.InstanceOf<TestBot.Services.TestBotService>());
            Assert.That(provider.GetServices<IDatabaseService>(), Is.Empty);
            Assert.That(provider.GetServices<IMongoDbDatabaseService>(), Is.Empty);
            Assert.That(provider.GetServices<IBotService>(), Is.Empty);
            Assert.That(registry.GetServicesForAllBots<IBotService>(),
                Is.EqualTo(new[] { provider.GetRequiredKeyedService<IBotService>(TestBot.Constants.Name) }));
            Assert.That(provider.GetRequiredService<BotControlCoordinator>().GetStatuses().Select(status => status.BotName),
                Is.EqualTo(new[] { TestBot.Constants.Name }));
        }
    }

    [Test]
    public void Discover_FromTheHostAssembly_FindsTestBotModule()
    {
        var modules = BotModuleDiscovery.Discover(typeof(Program).Assembly);

        Assert.That(modules.Select(module => module.Name), Is.EqualTo(new[] { TestBot.Constants.Name }));
    }

    private static WebApplicationBuilder CreateBuilder(TempDirectoryScope dataFolder)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production",
            ContentRootPath = dataFolder.RootPath
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DataPath"] = dataFolder.RootPath,
            ["ConnectionStrings:MongoDb"] = "mongodb://localhost:1",
            ["Bots:TestBot:BotConfig:Token"] = "123456:FromHostConfiguration",
            ["Bots:TestBot:BotConfig:AdminChatId"] = "42"
        });
        return builder;
    }
}
