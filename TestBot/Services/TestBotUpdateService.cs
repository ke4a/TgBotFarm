using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TestBot.Abstractions;
using TestBot.Models;

namespace TestBot.Services;

public class TestBotUpdateService : UpdateService
{
    private readonly ITestBotDatabaseService _databaseService;
    private readonly ITestBotMarkupService _markupService;
    private readonly INotificationService _notificationService;
    private readonly BotConfig _config;

    private const string logPrefix = $"[{nameof(TestBotUpdateService)}]";

    public override string Name => Constants.Name;

    public TestBotUpdateService(
        [FromKeyedServices(Constants.Name)] IBotService botService,
        ILogger<TestBotUpdateService> logger,
        ITestBotDatabaseService databaseService,
        ITestBotMarkupService markupService,
        ILocalizationService localizationService,
        IOptionsMonitor<BotConfig> options,
        INotificationService notificationService)
        : base(botService, logger, databaseService, localizationService, markupService)
    {
        _databaseService = databaseService;
        _markupService = markupService;
        _notificationService = notificationService;
        _config = options.Get(Name);
    }

    public override async Task ProcessUpdate(Update update)
    {
        if (update.Type == UpdateType.Message
            && update.Message?.Entities?.FirstOrDefault()?.Type == MessageEntityType.BotCommand
            && (update.Message?.Chat.Type == ChatType.Private
                || update.Message?.EntityValues?.FirstOrDefault()?.EndsWith(_config.Handle) == true))
        {
            // handle commands
            var language = await _databaseService.GetChatLanguage<TestBotChatSettings>(update.Message.Chat.Id);
            var command = update.Message.EntityValues.First().Split('@')[0];

            await (command switch
            {
                Constants.Commands.GetLastGif => GetLastGif(update.Message, language),
                Constants.Commands.ClearChatData => ClearChatData(update.Message, language),
                BotFarm.Core.Constants.Commands.ChangeLanguage => ChangeLanguage(update.Message, language),
                BotFarm.Core.Constants.Commands.Start => Welcome(update.Message.Chat.Id),
                _ => Task.CompletedTask
            });
        }
        else if (update.Type == UpdateType.Message
                 && update.Message?.Type == MessageType.Animation)
        {
            // handle gif messages
            await GifHandler(update.Message);
        }
        else if (update.Type == UpdateType.CallbackQuery
                 && update.CallbackQuery != null)
        {
            // handle callback queries
            var language = await _databaseService.GetChatLanguage<TestBotChatSettings>(update.CallbackQuery.Message.Chat.Id);
            var message = update.CallbackQuery.Message;
            var command = update.CallbackQuery.Data.Split(':')[0];
            var parameter = update.CallbackQuery.Data.Split(':')[1];
            var user = update.CallbackQuery.From;

            await (command switch
            {
                Constants.Callbacks.ChatDataClear => ClearChatData(update.CallbackQuery.Id, message, user, parameter, language),
                BotFarm.Core.Constants.Callbacks.LanguageSet => SetLanguage<TestBotChatSettings>(update.CallbackQuery.Id, message, user, parameter),
                _ => Task.CompletedTask
            });
        }
        else if (update.Type == UpdateType.Message
                 && update.Message?.Type == MessageType.NewChatMembers
                 && update.Message.NewChatMembers != null
                 && update.Message.NewChatMembers.Any(u => u.Id.Equals(BotService.Me.Id)))
        {
            // send welcome message when added to a chat
            await Welcome(update.Message.Chat.Id);
        }
    }

    #region Callbacks
    private async Task ClearChatData(string callbackId, Message message, User user, string response, string language)
    {
        var from = await BotService.Client.GetChatMember(message.Chat.Id, user.Id);
        if (from.IsAdmin || message.Chat.Type == ChatType.Private)
        {
            if (response.Equals("yes"))
            {
                _databaseService.ClearChatData(message.Chat.Id);
                Logger.LogInformation($"{logPrefix} Chat data cleared by user '{user.Username}' ({user.Id}) in chat '{message.Chat.Title}' ({message.Chat.Id}).");

                await BotService.Client.EditMessageText(
                    message.Chat.Id,
                    message.MessageId,
                    LocalizationService.GetLocalizedString(Name, "DataCleared", language));
            }
            else
            {
                await BotService.Client.DeleteMessage(message.Chat.Id, message.MessageId);
            }

            await BotService.Client.AnswerCallbackQuery(callbackQueryId: callbackId);
        }
    }
    #endregion

    #region Commands
    private async Task ClearChatData(Message message, string language)
    {
        Logger.LogInformation($"{logPrefix} Chat data clearing requested by user '{message.From.Username}' ({message.From.Id}) in chat '{message.Chat.Title}' ({message.Chat.Id}).");

        var from = await BotService.Client.GetChatMember(message.Chat.Id, message.From.Id);
        if (from.IsAdmin || message.Chat.Type == ChatType.Private)
        {
            await BotService.Client.SendMessage(
                message.Chat.Id,
                LocalizationService.GetLocalizedString(Name, "AreYouSureClear", language),
                replyParameters: message.MessageId,
                replyMarkup: _markupService.GenerateClearChatDataMarkup(language));
        }
        else
        {
            await BotService.Client.SendMessage(
                message.Chat.Id,
                LocalizationService.GetLocalizedString(Name, "OnlyAdminsClear", language));
        }
    }

    private async Task GetLastGif(Message message, string language)
    {
        Logger.LogInformation($"{logPrefix} Last GIF retrieval requested by user '{message.From.Username}' ({message.From.Id}) in chat '{message.Chat.Title}' ({message.Chat.Id}).");

        var lastGif = _databaseService.GetGifData(message.Chat.Id, message.From.Id);
        if (lastGif != null)
        {
            await BotService.Client.SendAnimation(message.Chat.Id, lastGif.FileId, replyParameters: message.MessageId);
        }
        else
        {
            await BotService.Client.SendMessage(
                message.Chat.Id,
                LocalizationService.GetLocalizedString(Name, "NoGifsFound", language),
                replyParameters: message.MessageId);
        }
    }
    #endregion

    private async Task GifHandler(Message message)
    {
        var fileId = message.Animation?.FileId;
        if (fileId == null)
        {
            return;
        }

        try
        {
            var gifData = new GifData
            {
                FileId = fileId,
                UserId = message.From.Id,
            };
            _databaseService.SaveGifData(message.Chat.Id, gifData);
            Logger.LogInformation($"{logPrefix} Saved GIF data from user '{message.From.Username}' ({message.From.Id}) in chat '{message.Chat.Title}' ({message.Chat.Id}).");
        }
        catch (Exception ex)
        {
            var errorMessage = $"{logPrefix} Error saving GIF data from user '{message.From.Username}' ({message.From.Id}) in chat '{message.Chat.Title}' ({message.Chat.Id}).";
            Logger.LogError(ex, errorMessage);
            await _notificationService.SendErrorNotification(errorMessage, Name, message);
        }
    }
}
