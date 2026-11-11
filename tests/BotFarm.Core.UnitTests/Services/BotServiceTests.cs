using BotFarm.Core.Abstractions;
using BotFarm.Core.Base;
using BotFarm.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
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
    private IAppDataPaths _dataPaths = null!;
    private string _tempRoot = null!;

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
            AdminChatId = 12345
        });

        _webhookSecretProvider = Substitute.For<IWebhookSecretProvider>();
        _webhookSecretProvider.GetSecret("TestBot").Returns(WebhookSecret);

        _tempRoot = Path.Combine(Path.GetTempPath(), "BotFarm_Tests", Guid.NewGuid().ToString(), "tmp");
        _dataPaths = Substitute.For<IAppDataPaths>();
        _dataPaths.Temp.Returns(_tempRoot);

        _botService = new TestableBotService(
            _clientFactory, _logger, _optionsMonitor, _webhookSecretProvider, _dataPaths);
    }

    [TearDown]
    public void TearDown()
    {
        var testRoot = Path.GetDirectoryName(_tempRoot)!;
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, true);
        }
    }

    [Test]
    public void TempPath_IsBotFolderInsideConfiguredTempFolder()
    {
        Assert.That(_botService.TempPath, Is.EqualTo(Path.Combine(_tempRoot, "TestBot")));
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

    [Test]
    public void Constructor_CreatesClientFromConfiguredToken()
    {
        _clientFactory.Received(1).Create("123456789:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
    }

    [Test]
    public void Constructor_WithoutConfiguredToken_Throws()
    {
        _optionsMonitor.Get("TestBot").Returns(new BotConfig());

        Assert.Catch<ArgumentException>(() =>
            new TestableBotService(_clientFactory, _logger, _optionsMonitor, _webhookSecretProvider, _dataPaths));
    }

    [Test]
    public async Task Initialize_FetchesBotAccount()
    {
        var me = new User { Id = 42, Username = "testbot", IsBot = true };
        _mockClient.SendRequest(Arg.Any<GetMeRequest>(), Arg.Any<CancellationToken>()).Returns(me);

        await _botService.Initialize();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_botService.Me, Is.SameAs(me));
            Assert.That(Directory.Exists(_botService.TempPath), Is.True);
        }
    }

    [Test]
    public async Task DropPendingUpdates_WithRegisteredWebhook_SetsSameUrlAndDropsUpdates()
    {
        const string webhookUrl = "https://example.com/api/TestBot/update";
        _mockClient.SendRequest(Arg.Any<GetWebhookInfoRequest>(), Arg.Any<CancellationToken>())
            .Returns(new WebhookInfo { Url = webhookUrl, PendingUpdateCount = 7 });

        await _botService.DropPendingUpdates();

        var request = GetSingleRequest(_mockClient, "SetWebhookRequest");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(GetPropertyValue(request, "Url"), Is.EqualTo(webhookUrl));
            Assert.That(GetPropertyValue(request, "SecretToken"), Is.EqualTo(WebhookSecret));
            Assert.That(GetPropertyValue(request, "DropPendingUpdates"), Is.True);
        }
    }

    [Test]
    public async Task DropPendingUpdates_WithoutWebhook_DeletesWebhookAndDropsUpdates()
    {
        _mockClient.SendRequest(Arg.Any<GetWebhookInfoRequest>(), Arg.Any<CancellationToken>())
            .Returns(new WebhookInfo { Url = string.Empty, PendingUpdateCount = 3 });

        await _botService.DropPendingUpdates();

        var request = GetSingleRequest(_mockClient, "DeleteWebhookRequest");
        Assert.That(GetPropertyValue(request, "DropPendingUpdates"), Is.True);
        Assert.That(
            _mockClient.ReceivedCalls().SelectMany(call => call.GetArguments()).OfType<SetWebhookRequest>(),
            Is.Empty);
    }

    [Test]
    public async Task Pause_WhenWebhookIsDeleted_ReturnsTrue()
    {
        _mockClient.SendRequest(Arg.Any<DeleteWebhookRequest>(), Arg.Any<CancellationToken>()).Returns(true);

        var paused = await _botService.Pause();

        Assert.That(paused, Is.True);
        GetSingleRequest(_mockClient, "DeleteWebhookRequest");
    }

    [Test]
    public async Task Pause_WhenTelegramRequestFails_ReturnsFalse()
    {
        _mockClient.SendRequest(Arg.Any<DeleteWebhookRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiRequestException("Unauthorized", 401));

        var paused = await _botService.Pause();

        Assert.That(paused, Is.False);
    }

    [Test]
    public void Pause_WhenCancelled_Throws()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        _mockClient.SendRequest(Arg.Any<DeleteWebhookRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));

        Assert.CatchAsync<OperationCanceledException>(() => _botService.Pause(cancellation.Token));
    }

    private class TestableBotService : BotService
    {
        public TestableBotService(
            ITelegramBotClientFactory clientFactory,
            ILogger<BotService> logger,
            IOptionsMonitor<BotConfig> botConfigs,
            IWebhookSecretProvider webhookSecretProvider,
            IAppDataPaths dataPaths)
            : base(new BotIdentity("TestBot"), clientFactory, logger, botConfigs, webhookSecretProvider, dataPaths)
        {
        }

        public string GetLogPrefix() => Identity.LogPrefix;
    }
}