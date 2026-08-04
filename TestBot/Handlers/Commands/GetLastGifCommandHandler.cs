using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using TestBot.Abstractions;

namespace TestBot.Handlers.Commands;

/// <summary>
/// Handles <c>/getlastgif</c>: re-sends the last GIF stored for the requesting user in this chat.
/// </summary>
public class GetLastGifCommandHandler : ICommandHandler
{
    private readonly BotIdentity _identity;
    private readonly IBotService _botService;
    private readonly ILocalizationService _localizationService;
    private readonly ITestBotDatabaseService _databaseService;
    private readonly ILogger<GetLastGifCommandHandler> _logger;

    public string Command => TestBot.Constants.Commands.GetLastGif;

    public GetLastGifCommandHandler(
        IBotService botService,
        ILocalizationService localizationService,
        ITestBotDatabaseService databaseService,
        ILogger<GetLastGifCommandHandler> logger)
    {
        _identity = new BotIdentity(TestBot.Constants.Name);
        _botService = botService;
        _localizationService = localizationService;
        _databaseService = databaseService;
        _logger = logger;
    }

    public async Task HandleAsync(Message message, string language)
    {
        _logger.LogInformation($"{_identity.LogPrefix} Last GIF retrieval requested by user '{message.From.Username}' ({message.From.Id}) in chat '{message.Chat.Title}' ({message.Chat.Id}).");

        var lastGif = _databaseService.GetGifData(message.Chat.Id, message.From.Id);
        if (lastGif != null)
        {
            await _botService.Client.SendAnimation(message.Chat.Id, lastGif.FileId, replyParameters: message.MessageId);
        }
        else
        {
            await _botService.Client.SendMessage(
                message.Chat.Id,
                _localizationService.GetLocalizedString(_identity.Name, "NoGifsFound", language),
                replyParameters: message.MessageId);
        }
    }
}
