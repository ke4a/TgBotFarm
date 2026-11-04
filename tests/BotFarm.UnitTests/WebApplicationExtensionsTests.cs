using BotFarm.Core.Abstractions;
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
