using BotFarm.Core.Models;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace BotFarm.Core.Abstractions;

public abstract class UpdateService : IUpdateService
{
    protected readonly IBotService BotService;
    protected readonly ILogger Logger;
    protected readonly IDatabaseService DatabaseService;
    protected readonly ILocalizationService LocalizationService;
    protected readonly IMarkupService MarkupService;

    public abstract string Name { get; }

    protected UpdateService(
        IBotService botService,
        ILogger logger,
        IDatabaseService databaseService,
        ILocalizationService localizationService,
        IMarkupService markupService)
    {
        BotService = botService;
        Logger = logger;
        DatabaseService = databaseService;
        LocalizationService = localizationService;
        MarkupService = markupService;
    }

    public abstract Task ProcessUpdateAsync(Update update);

    protected async Task ChangeLanguage(Message message, string language)
    {
        Logger.LogInformation($"[{Name}] Chat language change requested by user '{message.From.Username}' ({message.From.Id}) in chat '{message.Chat.Title}' ({message.Chat.Id}).");

        await BotService.Client.SendMessage(
            message.Chat.Id,
            LocalizationService.GetLocalizedString(Name, "ChooseLanguage", language),
            replyParameters: message.MessageId,
            replyMarkup: MarkupService.GenerateChangeLanguageMarkup(Name));
    }

    protected async Task SetLanguage<TSettings>(string callbackId, Message message, User user, string newLanguage) where TSettings : ChatSettings
    {
        await DatabaseService.SetChatLanguage<TSettings>(message.Chat.Id, newLanguage);
        Logger.LogInformation($"[{Name}] Chat language changed to '{newLanguage}' by user '{user.Username}' ({user.Id}) in chat '{message.Chat.Title}' ({message.Chat.Id}).");

        await BotService.Client.EditMessageText(
            message.Chat.Id,
            message.MessageId,
            LocalizationService.GetLocalizedString(Name, "NowISpeak", newLanguage));

        await BotService.Client.AnswerCallbackQuery(callbackQueryId: callbackId);
    }
}
