using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Telegram.Bot;
using static BotFarm.TestKit.TelegramRequestAssertHelpers;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class BotServiceTests
{
    private TestableBotService _botService;
    private ILogger<BotService> _logger;
    private IOptionsMonitor<BotConfig> _optionsMonitor;
    private ITelegramBotClientFactory _clientFactory;
    private TelegramBotClient _mockClient;
    private IWebhookSecretProvider _webhookSecretProvider = null!;

    private const string WebhookSecret = "test_webhook-secret";

    [SetUp]
    public void SetUp()
    {
        _logger = Substitute.For<ILogger<BotService>>();
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

        _webhookSecretProvider = Substitute.For<IWebhookSecretProvider>();
        _webhookSecretProvider.GetSecret("TestBot").Returns(WebhookSecret);

        _botService = new TestableBotService(
            _clientFactory, _logger, _optionsMonitor, _webhookSecretProvider);
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
    public async Task InitializeWebHook_ValidUrl_SetsWebhookWithSecret()
    {
        // Arrange
        const string webhookUrl = "https://example.com/webhook";
        
        // Act
        await _botService.InitializeWebHook(webhookUrl);

        // Assert
        var request = GetSingleRequest(_mockClient, "SetWebhookRequest");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(GetPropertyValue(request, "Url"), Is.EqualTo(webhookUrl));
            Assert.That(GetPropertyValue(request, "SecretToken"), Is.EqualTo(WebhookSecret));
        }
    }

    private class TestableBotService : BotService
    {
        public TestableBotService(
            ITelegramBotClientFactory clientFactory,
            ILogger<BotService> logger,
            IOptionsMonitor<BotConfig> botConfigs,
            IWebhookSecretProvider webhookSecretProvider)
            : base(new BotIdentity("TestBot"), clientFactory, logger, botConfigs, webhookSecretProvider)
        {
        }

        public string GetLogPrefix() => Identity.LogPrefix;
    }
}