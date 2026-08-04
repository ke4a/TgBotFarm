using BotFarm.Core.Models;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Base class for bot-specific update handlers.
/// </summary>
public abstract class UpdateService : IUpdateService
{
    protected readonly IBotService BotService;
    protected readonly ILogger Logger;
    protected readonly IDatabaseService DatabaseService;
    protected readonly ILocalizationService LocalizationService;
    protected readonly IMarkupService MarkupService;
    protected readonly BotIdentity Identity;

    private readonly IReadOnlyDictionary<string, ICommandHandler> _commandHandlers;
    private readonly IReadOnlyDictionary<string, ICallbackHandler> _callbackHandlers;

    public string Name => Identity.Name;

    /// <summary>
    /// Wires together the bot-specific services commonly needed while processing updates.
    /// </summary>
    /// <param name="commandHandlers">
    /// Bot-specific command handlers, keyed by <see cref="ICommandHandler.Command"/>, dispatched via
    /// <see cref="HandleCommand"/>.
    /// </param>
    /// <param name="callbackHandlers">
    /// Bot-specific callback handlers, keyed by <see cref="ICallbackHandler.CallbackKey"/>, dispatched
    /// via <see cref="HandleCallback"/>.
    /// </param>
    protected UpdateService(
        BotIdentity identity,
        IBotService botService,
        ILogger logger,
        IDatabaseService databaseService,
        ILocalizationService localizationService,
        IMarkupService markupService,
        IEnumerable<ICommandHandler>? commandHandlers = null,
        IEnumerable<ICallbackHandler>? callbackHandlers = null)
    {
        Identity = identity;
        BotService = botService;
        Logger = logger;
        DatabaseService = databaseService;
        LocalizationService = localizationService;
        MarkupService = markupService;
        _commandHandlers = (commandHandlers ?? []).ToDictionary(h => h.Command);
        _callbackHandlers = (callbackHandlers ?? []).ToDictionary(h => h.CallbackKey);
    }

    public abstract Task ProcessUpdate(Update update);

    /// <summary>
    /// Sends the standard language chooser prompt in response to a change-language command.
    /// </summary>
    protected async Task ChangeLanguage(Message message, string language)
    {
        Logger.LogInformation($"{Identity.LogPrefix} Chat language change requested by user '{message.From.Username}' ({message.From.Id}) in chat '{message.Chat.Title}' ({message.Chat.Id}).");

        await BotService.Client.SendMessage(
            message.Chat.Id,
            LocalizationService.GetLocalizedString(Name, "ChooseLanguage", language),
            replyParameters: message.MessageId,
            replyMarkup: MarkupService.GenerateChangeLanguageMarkup(Name));
    }

    /// <summary>
    /// Persists the new language, updates the original callback message, and acknowledges the callback.
    /// </summary>
    protected async Task SetLanguage<TSettings>(string callbackId, Message message, User user, string newLanguage) where TSettings : ChatSettings
    {
        await DatabaseService.SetChatLanguage<TSettings>(message.Chat.Id, newLanguage);
        Logger.LogInformation($"{Identity.LogPrefix} Chat language changed to '{newLanguage}' by user '{user.Username}' ({user.Id}) in chat '{message.Chat.Title}' ({message.Chat.Id}).");

        await BotService.Client.EditMessageText(
            message.Chat.Id,
            message.MessageId,
            LocalizationService.GetLocalizedString(Name, "NowISpeak", newLanguage));

        await BotService.Client.AnswerCallbackQuery(callbackQueryId: callbackId);
    }

    /// <summary>
    /// Sends the standard localized welcome message to a chat.
    /// </summary>
    protected async Task Welcome(long chatId)
    {
        var language = await DatabaseService.GetChatLanguage<ChatSettings>(chatId);

        _ = await BotService.Client.SendMessage(
                chatId,
                LocalizationService.GetLocalizedString(Name, "Welcome", language));
    }

    /// <summary>
    /// Dispatches to the registered <see cref="ICommandHandler"/> for <paramref name="command"/>, or
    /// does nothing if no handler is registered for it.
    /// </summary>
    protected Task HandleCommand(string command, Message message, string language)
    {
        return _commandHandlers.TryGetValue(command, out var handler)
            ? handler.HandleAsync(message, language)
            : Task.CompletedTask;
    }

    /// <summary>
    /// Dispatches to the registered <see cref="ICallbackHandler"/> for <paramref name="callbackKey"/>, or
    /// does nothing if no handler is registered for it.
    /// </summary>
    protected Task HandleCallback(string callbackKey, string callbackId, Message message, User user, string parameter, string language)
    {
        return _callbackHandlers.TryGetValue(callbackKey, out var handler)
            ? handler.HandleAsync(callbackId, message, user, parameter, language)
            : Task.CompletedTask;
    }
}
