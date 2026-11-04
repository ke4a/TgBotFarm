using BotFarm.Core.Models;
using BotFarm.TestKit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BotFarm.UnitTests;

[TestFixture]
public class StartupTests
{
    [Test]
    public void ConfigureServices_UsesTheConfigurationProvidedByTheHost()
    {
        using var dataFolder = new TempDirectoryScope();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataPath"] = dataFolder.RootPath,
                ["ConnectionStrings:MongoDb"] = "mongodb://localhost:1",
                ["Bots:TestBot:BotConfig:Token"] = "123456:FromHostConfiguration",
                ["Bots:TestBot:BotConfig:AdminChatId"] = "42"
            })
            .Build();
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns("Production");
        environment.ContentRootPath.Returns(dataFolder.RootPath);
        var services = new ServiceCollection();

        new Startup(configuration, environment).ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var botConfig = provider.GetRequiredService<IOptionsMonitor<BotConfig>>().Get(TestBot.Constants.Name);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(botConfig.Token, Is.EqualTo("123456:FromHostConfiguration"));
            Assert.That(botConfig.AdminChatId, Is.EqualTo(42));
        }
    }
}
