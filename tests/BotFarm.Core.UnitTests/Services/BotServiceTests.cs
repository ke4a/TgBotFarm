using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Telegram.Bot;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class BotServiceTests
{
    private TestableBotService _botService;
    private ILogger<BotService> _logger;
    private IHostApplicationLifetime _appLifetime;
    private IOptionsMonitor<BotConfig> _optionsMonitor;
    private TelegramBotClient _mockClient;

    [SetUp]
    public void SetUp()
    {
        _logger = Substitute.For<ILogger<BotService>>();
        _appLifetime = Substitute.For<IHostApplicationLifetime>();
        _mockClient = Substitute.For<TelegramBotClient>("111111111:AAAAAbAAAAbbAAbbAAAbAbAAbbb_bAAbAb1", null, CancellationToken.None);
        _optionsMonitor = Substitute.For<IOptionsMonitor<BotConfig>>();
        _optionsMonitor.Get("TestBot").Returns(new BotConfig
        {
            Token = "123456789:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            Emoji = "🤖",
            Handle = "testbot",
            AdminChatId = 12345
        });

        _botService = new TestableBotService(_logger, _appLifetime, _optionsMonitor);
        _botService.SetClient(_mockClient);
    }

    [Test]
    public void Constructor_SetsProperties_Correctly()
    {
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_botService.Client, Is.EqualTo(_mockClient));
            Assert.That(_botService.Name, Is.EqualTo("TestBot"));
        }
    }

    [Test]
    public async Task InitializeWebHook_ValidUrl_SetsWebhookAndStoresUrl()
    {
        // Arrange
        const string webhookUrl = "https://example.com/webhook";
        
        // Act
        await _botService.InitializeWebHook(webhookUrl);

        // Assert
        Assert.That(_botService.GetCurrentWebHook(), Is.EqualTo(webhookUrl));
    }

    [Test]
    public async Task Resume_SuccessfulResume_ReturnsTrue()
    {
        // Arrange
        const string webhookUrl = "https://example.com/webhook";
        await _botService.InitializeWebHook(webhookUrl);

        // Act
        var result = await _botService.Resume();

        // Assert
        Assert.That(result, Is.True);
    }

    private class TestableBotService : BotService
    {
        public TestableBotService(ILogger<BotService> logger, IHostApplicationLifetime appLifetime, IOptionsMonitor<BotConfig> botConfigs) 
            : base(logger, appLifetime, botConfigs)
        {
        }
        public override string Name => "TestBot";

        public void SetClient(TelegramBotClient client)
        {
            Client = client;
        }

        public string GetCurrentWebHook() => currentWebHook;
        public string GetLogPrefix() => logPrefix;
    }
}