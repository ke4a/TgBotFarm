using BotFarm.Core.Extensions;
using BotFarm.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BotFarm.Core.UnitTests.Extensions;

[TestFixture]
public class BotConfigRegistrationTests
{
    private const string BotName = "TestBot";
    private const string Token = "123456789:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddCoreServices(configuration, Substitute.For<IHostEnvironment>())
            .AddBotConfig(BotName, configuration);
        return services.BuildServiceProvider();
    }

    [Test]
    public void AddBotConfig_BindsTheNamedBotSection()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Bots:TestBot:BotConfig:Enabled"] = "true",
            ["Bots:TestBot:BotConfig:Emoji"] = "🤖",
            ["Bots:TestBot:BotConfig:Token"] = Token,
            ["Bots:TestBot:BotConfig:AdminChatId"] = "12345",
            ["Bots:TestBot:BotConfig:WebhookSecret"] = "my_Secret-123",
            ["Bots:OtherBot:BotConfig:Token"] = "987654321:BBBB"
        });

        var config = provider.GetRequiredService<IOptionsMonitor<BotConfig>>().Get(BotName);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(config.Enabled, Is.True);
            Assert.That(config.Emoji, Is.EqualTo("🤖"));
            Assert.That(config.Token, Is.EqualTo(Token));
            Assert.That(config.AdminChatId, Is.EqualTo(12345));
            Assert.That(config.WebhookSecret, Is.EqualTo("my_Secret-123"));
        }
    }

    [Test]
    public void AddBotConfig_InvalidSection_ThrowsValidationErrorNamingThePath()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Bots:TestBot:BotConfig:Token"] = "bad",
            ["Bots:TestBot:BotConfig:AdminChatId"] = "12345"
        });

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptionsMonitor<BotConfig>>().Get(BotName));

        Assert.That(exception!.Message, Does.Contain("Bots:TestBot:BotConfig:Token"));
    }

    [Test]
    public async Task AddBotConfig_InvalidSection_FailsHostStartup()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bots:TestBot:BotConfig:Token"] = Token
            })
            .Build();
        using var host = new HostBuilder()
            .ConfigureServices(services => services
                .AddCoreServices(configuration, Substitute.For<IHostEnvironment>())
                .AddBotConfig(BotName, configuration))
            .Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(async () => await host.StartAsync());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("Bots:TestBot:BotConfig:AdminChatId"));
            Assert.That(exception.Message, Does.Not.Contain(Token));
        }
    }
}
