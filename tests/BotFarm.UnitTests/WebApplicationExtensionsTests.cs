using BotFarm.Core.Models;
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

        builder.AddBotFarm();

        using var provider = builder.Services.BuildServiceProvider();
        var botConfig = provider.GetRequiredService<IOptionsMonitor<BotConfig>>().Get(TestBot.Constants.Name);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(botConfig.Token, Is.EqualTo("123456:FromHostConfiguration"));
            Assert.That(botConfig.AdminChatId, Is.EqualTo(42));
        }
    }
}
