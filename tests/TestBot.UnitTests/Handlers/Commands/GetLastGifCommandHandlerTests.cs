using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.TestKit;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Telegram.Bot;
using Telegram.Bot.Types;
using static BotFarm.TestKit.TelegramRequestAssertHelpers;
using TestBot.Abstractions;
using TestBot.Handlers.Commands;
using TestBot.Models;

namespace TestBot.UnitTests.Handlers.Commands;

[TestFixture]
public class GetLastGifCommandHandlerTests
{
    private const long ChatId = 12345;
    private const long UserId = 67890;

    private GetLastGifCommandHandler _handler;
    private IBotService _botService;
    private ILocalizationService _localizationService;
    private ITestBotDatabaseService _databaseService;
    private TelegramBotClient _client;

    [SetUp]
    public void SetUp()
    {
        _botService = Substitute.For<IBotService>();
        _localizationService = Substitute.For<ILocalizationService>();
        _databaseService = Substitute.For<ITestBotDatabaseService>();
        _client = TelegramBotClientFactory.CreateSubstitute();

        _botService.Client.Returns(_client);
        _handler = new GetLastGifCommandHandler(
            _botService,
            _localizationService,
            _databaseService,
            Substitute.For<ILogger<GetLastGifCommandHandler>>());
    }

    [Test]
    public async Task Handle_WithStoredGif_SendsAnimation()
    {
        var message = TelegramMessageFactory.CreateMessage(ChatId, UserId, 7, "GIF chat", "gif-user");
        _databaseService.GetGifData(ChatId, UserId).Returns(new GifData { UserId = UserId, FileId = "gif-file-id" });

        await _handler.Handle(message, "en");

        var request = GetSingleRequest(_client, "SendAnimationRequest");
        Assert.That(GetPropertyValue(request, "ChatId")?.ToString(), Is.EqualTo(ChatId.ToString()));
    }

    [Test]
    public async Task Handle_WithoutStoredGif_SendsLocalizedFallbackMessage()
    {
        var message = TelegramMessageFactory.CreateMessage(ChatId, UserId, 7, "GIF chat", "gif-user");
        _localizationService.GetLocalizedString(Constants.Name, "NoGifsFound", "en").Returns("No GIFs found.");

        await _handler.Handle(message, "en");

        var request = GetSingleRequest(_client, "SendMessageRequest");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(GetPropertyValue(request, "ChatId")?.ToString(), Is.EqualTo(ChatId.ToString()));
            Assert.That(GetPropertyValue(request, "Text"), Is.EqualTo("No GIFs found."));
        }
    }

}
