using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.Core.UnitTests.TestHelpers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Telegram.Bot;
using static BotFarm.Core.UnitTests.TestHelpers.TelegramRequestAssertHelpers;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class BotServiceTests
{
    private TestableBotService _botService;
    private ILogger<BotService> _logger;
    private IHostApplicationLifetime _appLifetime;
    private IOptionsMonitor<BotConfig> _optionsMonitor;
    private ITelegramBotClientFactory _clientFactory;
    private TelegramBotClient _mockClient;

    [SetUp]
    public void SetUp()
    {
        _logger = Substitute.For<ILogger<BotService>>();
        _appLifetime = Substitute.For<IHostApplicationLifetime>();
        _mockClient = Substitute.For<TelegramBotClient>("111111111:AAAAAbAAAAbbAAbbAAAbAbAAbbb_bAAbAb1", null, CancellationToken.None);
        _clientFactory = Substitute.For<ITelegramBotClientFactory>();
        _clientFactory.Create(Arg.Any<string>()).Returns(_mockClient);
        _optionsMonitor = Substitute.For<IOptionsMonitor<BotConfig>>();
        _optionsMonitor.Get("TestBot").Returns(new BotConfig
        {
            Token = "123456789:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            Emoji = "🤖",
            Handle = "testbot",
            AdminChatId = 12345
        });

        _botService = new TestableBotService(_clientFactory, _logger, _appLifetime, _optionsMonitor);
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
        var request = GetSingleRequest(_mockClient, "SetWebhookRequest");
        Assert.That(GetPropertyValue(request, "Url"), Is.EqualTo(webhookUrl));
    }

    [Test]
    public async Task Resume_SuccessfulResume_ReturnsTrue()
    {
        // Arrange
        const string webhookUrl = "https://example.com/webhook";
        await _botService.InitializeWebHook(webhookUrl);
        _mockClient.ClearReceivedCalls();

        // Act
        var result = await _botService.Resume();

        // Assert
        Assert.That(result, Is.True);
        var request = GetSingleRequest(_mockClient, "SetWebhookRequest");
        Assert.That(GetPropertyValue(request, "Url"), Is.EqualTo(webhookUrl));
    }

    private class TestableBotService : BotService
    {
        public TestableBotService(
            ITelegramBotClientFactory clientFactory,
            ILogger<BotService> logger,
            IHostApplicationLifetime appLifetime,
            IOptionsMonitor<BotConfig> botConfigs)
            : base(new BotIdentity("TestBot"), clientFactory, logger, appLifetime, botConfigs)
        {
        }

        public string GetCurrentWebHook() => currentWebHook;
        public string GetLogPrefix() => Identity.LogPrefix;
    }
}