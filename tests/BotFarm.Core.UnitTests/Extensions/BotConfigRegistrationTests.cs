using BotFarm.Core.Abstractions;
using BotFarm.Core.Extensions;
using BotFarm.Core.Models;
using BotFarm.Core.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BotFarm.Core.UnitTests.Extensions;

[TestFixture]
public class BotConfigRegistrationTests
{
    private const string BotName = "TestBot";
    private const string Token = "123456:TEST-TOKEN";

    private static readonly Dictionary<string, string?> ConfiguredSettings = new()
    {
        ["ConnectionStrings:MongoDb"] = "mongodb://localhost:1",
        ["Bots:TestBot:BotConfig:Emoji"] = "🤖",
        ["Bots:TestBot:BotConfig:Token"] = "987654:CONFIG-TOKEN",
        ["Bots:TestBot:BotConfig:AdminChatId"] = "12345",
        ["Bots:TestBot:BotConfig:WebhookSecret"] = "config_Secret-123"
    };

    private static ServiceProvider BuildProvider(IBotSettingsStore store, IDataProtectionProvider dataProtection)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(ConfiguredSettings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCoreServices(configuration, Substitute.For<IHostEnvironment>())
            .AddBotConfig(BotName);
        services.Replace(ServiceDescriptor.Singleton(store));
        services.Replace(ServiceDescriptor.Singleton(dataProtection));
        return services.BuildServiceProvider();
    }

    [Test]
    public async Task AddBotConfig_FillsTheNamedOptionsFromTheSettingsProvider()
    {
        var dataProtection = new EphemeralDataProtectionProvider();
        var store = Substitute.For<IBotSettingsStore>();
        store.GetAsync(BotName, Arg.Any<CancellationToken>()).Returns(new BotSettings
        {
            BotName = BotName,
            ProtectedToken = new BotSettingsProtector(dataProtection).Protect(BotName, Token),
            TelegramBotId = 123456,
            AdminChatId = 42,
            Emoji = "🚀",
            UpdatedAtUtc = DateTime.UtcNow,
            UpdatedBy = "admin"
        });
        await using var provider = BuildProvider(store, dataProtection);
        var botConfigs = provider.GetRequiredService<IOptionsMonitor<BotConfig>>();

        await provider.GetRequiredService<IBotSettingsProvider>().LoadAsync(BotName);

        var config = botConfigs.Get(BotName);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(config.Token == Token, Is.True);
            Assert.That(config.AdminChatId, Is.EqualTo(42));
            Assert.That(config.Emoji, Is.EqualTo("🚀"));
            Assert.That(config.WebhookSecret, Is.Null);
        }
    }

    [Test]
    public async Task AddBotConfig_DoesNotBindTheBotsConfigurationSection()
    {
        await using var provider = BuildProvider(
            Substitute.For<IBotSettingsStore>(),
            new EphemeralDataProtectionProvider());

        var config = provider.GetRequiredService<IOptionsMonitor<BotConfig>>().Get(BotName);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(config.Token, Is.Empty);
            Assert.That(config.AdminChatId, Is.Zero);
            Assert.That(config.WebhookSecret, Is.Null);
            Assert.That(config.Emoji, Is.Null);
        }
    }

    [Test]
    public async Task AddBotConfig_HostStartsWithoutBotSettings()
    {
        var configuration = new ConfigurationBuilder().Build();
        using var host = new HostBuilder()
            .ConfigureServices(services => services
                .AddCoreServices(configuration, Substitute.For<IHostEnvironment>())
                .AddBotConfig(BotName))
            .Build();

        await host.StartAsync();

        var config = host.Services.GetRequiredService<IOptionsMonitor<BotConfig>>().Get(BotName);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(config.Token, Is.Empty);
            Assert.That(config.AdminChatId, Is.Zero);
        }

        await host.StopAsync();
    }
}
