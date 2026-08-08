using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TestBot.Abstractions;

namespace TestBot.Handlers.Commands;

/// <summary>
/// Handles <c>/clearchatdata</c>; prompts for confirmation before clearing a chat's stored data.
/// </summary>
public class ClearChatDataCommandHandler : ICommandHandler
{
    private readonly BotIdentity _identity;
    private readonly IBotService _botService;
    private readonly ILocalizationService _localizationService;
    private readonly ITestBotMarkupService _markupService;
    private readonly ILogger<ClearChatDataCommandHandler> _logger;

    public string Command => Constants.Commands.ClearChatData;

    public ClearChatDataCommandHandler(
        [FromKeyedServices(Constants.Name)] IBotService botService,
        ILocalizationService localizationService,
        ITestBotMarkupService markupService,
        ILogger<ClearChatDataCommandHandler> logger)
    {
        _identity = new BotIdentity(TestBot.Constants.Name);
        _botService = botService;
        _localizationService = localizationService;
        _markupService = markupService;
        _logger = logger;
    }

    public async Task Handle(Message message, string language)
    {
        _logger.LogInformation($"{_identity.LogPrefix} Chat data clearing requested by user '{message.From.Username}' ({message.From.Id}) in chat '{message.Chat.Title}' ({message.Chat.Id}).");

        var from = await _botService.Client.GetChatMember(message.Chat.Id, message.From.Id);
        if (from.IsAdmin || message.Chat.Type == ChatType.Private)
        {
            await _botService.Client.SendMessage(
                message.Chat.Id,
                _localizationService.GetLocalizedString(_identity.Name, "AreYouSureClear", language),
                replyParameters: message.MessageId,
                replyMarkup: _markupService.GenerateClearChatDataMarkup(language));
        }
        else
        {
            await _botService.Client.SendMessage(
                message.Chat.Id,
                _localizationService.GetLocalizedString(_identity.Name, "OnlyAdminsClear", language));
        }
    }
}
