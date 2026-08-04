using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TestBot.Abstractions;

namespace TestBot.Handlers.Callbacks;

/// <summary>
/// Handles the <c>chatdata-clear</c> callback: confirms or cancels a pending chat-data clear request.
/// </summary>
public class ClearChatDataCallbackHandler : ICallbackHandler
{
    private readonly BotIdentity _identity;
    private readonly IBotService _botService;
    private readonly ILocalizationService _localizationService;
    private readonly ITestBotDatabaseService _databaseService;
    private readonly ILogger<ClearChatDataCallbackHandler> _logger;

    public string CallbackKey => TestBot.Constants.Callbacks.ChatDataClear;

    public ClearChatDataCallbackHandler(
        IBotService botService,
        ILocalizationService localizationService,
        ITestBotDatabaseService databaseService,
        ILogger<ClearChatDataCallbackHandler> logger)
    {
        _identity = new BotIdentity(TestBot.Constants.Name);
        _botService = botService;
        _localizationService = localizationService;
        _databaseService = databaseService;
        _logger = logger;
    }

    public async Task HandleAsync(string callbackId, Message message, User user, string parameter, string language)
    {
        var from = await _botService.Client.GetChatMember(message.Chat.Id, user.Id);
        if (from.IsAdmin || message.Chat.Type == ChatType.Private)
        {
            if (parameter.Equals("yes"))
            {
                _databaseService.ClearChatData(message.Chat.Id);
                _logger.LogInformation($"{_identity.LogPrefix} Chat data cleared by user '{user.Username}' ({user.Id}) in chat '{message.Chat.Title}' ({message.Chat.Id}).");

                await _botService.Client.EditMessageText(
                    message.Chat.Id,
                    message.MessageId,
                    _localizationService.GetLocalizedString(_identity.Name, "DataCleared", language));
            }
            else
            {
                await _botService.Client.DeleteMessage(message.Chat.Id, message.MessageId);
            }

            await _botService.Client.AnswerCallbackQuery(callbackQueryId: callbackId);
        }
    }
}
