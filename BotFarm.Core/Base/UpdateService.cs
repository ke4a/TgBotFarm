using BotFarm.Core.Abstractions;
using BotFarm.Core.Extensions;
using BotFarm.Core.Models;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace BotFarm.Core.Base;

/// <summary>
/// Base class for bot-specific update handlers. Routes bot commands, callback queries, and the bot being
/// added to a chat to the built-in language and welcome flows or to the registered
/// <see cref="ICommandHandler"/>/<see cref="ICallbackHandler"/> instances. All other updates go to
/// <see cref="OnUpdate"/>, where the bot implements its own behavior.
/// </summary>
/// <typeparam name="TChatSettings">The bot's chat settings type, used to read and store the chat language.</typeparam>
/// <remarks>
/// A bot that needs different routing can override <see cref="ProcessUpdate"/> entirely and reuse the
/// protected helpers.
/// </remarks>
public abstract class UpdateService<TChatSettings> : IUpdateService
    where TChatSettings : ChatSettings
{
    protected readonly IBotService BotService;
    protected readonly ILogger Logger;
    protected readonly IDatabaseService DatabaseService;
    protected readonly ILocalizationService LocalizationService;
    protected readonly IMarkupService MarkupService;
    protected readonly BotIdentity Identity;

    private readonly IBotControlService _botControlService;
    private readonly IReadOnlyDictionary<string, ICommandHandler> _commandHandlers;
    private readonly IReadOnlyDictionary<string, ICallbackHandler> _callbackHandlers;

    public string Name => Identity.Name;
    public bool CanProcessUpdates => _botControlService.CanProcessUpdates(Name);

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
        IBotControlService botControlService,
        ILogger logger,
        IDatabaseService databaseService,
        ILocalizationService localizationService,
        IMarkupService markupService,
        IEnumerable<ICommandHandler>? commandHandlers = null,
        IEnumerable<ICallbackHandler>? callbackHandlers = null)
    {
        Identity = identity;
        BotService = botService;
        _botControlService = botControlService;
        Logger = logger;
        DatabaseService = databaseService;
        LocalizationService = localizationService;
        MarkupService = markupService;
        _commandHandlers = (commandHandlers ?? []).ToDictionary(h => h.Command);
        _callbackHandlers = (callbackHandlers ?? []).ToDictionary(h => h.CallbackKey);
    }

    /// <summary>
    /// Routes an update to the matching handler:
    /// <list type="bullet">
    /// <item>bot commands sent in a private chat, or ending with <c>@</c> and this bot's username in other
    /// chats, go to <c>/start</c> (<see cref="Welcome"/>), <c>/changelanguage</c> (<see cref="ChangeLanguage"/>),
    /// or <see cref="HandleCommand"/>;</item>
    /// <item>callback queries go to the <c>language-set</c> flow (<see cref="SetLanguage"/>) or
    /// <see cref="HandleCallback"/>, and are ignored when their message is unavailable or their data is
    /// empty;</item>
    /// <item>a message announcing that this bot joined a chat sends <see cref="Welcome"/>;</item>
    /// <item>everything else goes to <see cref="OnUpdate"/>.</item>
    /// </list>
    /// </summary>
    public virtual async Task ProcessUpdate(Update update)
    {
        var message = update.Message;

        if (update.Type == UpdateType.Message && message != null && TryGetCommandForThisBot(message, out var command))
        {
            var language = await DatabaseService.GetChatLanguage<TChatSettings>(message.Chat.Id);

            await (command switch
            {
                Constants.Commands.ChangeLanguage => ChangeLanguage(message, language),
                Constants.Commands.Start => Welcome(message.Chat.Id),
                _ => HandleCommand(command, message, language)
            });
        }
        else if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
        {
            // ignore callback queries whose message is no longer available or whose data isn't in the
            // expected format, since there is nothing to act on
            var callbackMessage = update.CallbackQuery.Message;
            if (callbackMessage == null
                || !TryParseCallbackData(update.CallbackQuery.Data, out var callbackKey, out var parameter))
            {
                return;
            }

            var language = await DatabaseService.GetChatLanguage<TChatSettings>(callbackMessage.Chat.Id);
            var user = update.CallbackQuery.From;

            await (callbackKey switch
            {
                Constants.Callbacks.LanguageSet => SetLanguage(update.CallbackQuery.Id, callbackMessage, user,
                    parameter),
                _ => HandleCallback(callbackKey, update.CallbackQuery.Id, callbackMessage, user, parameter,
                    language)
            });
        }
        else if (update.Type == UpdateType.Message
                 && message?.Type == MessageType.NewChatMembers
                 && message.NewChatMembers != null
                 && BotService.Me != null
                 && message.NewChatMembers.Any(u => u.Id == BotService.Me.Id))
        {
            await Welcome(message.Chat.Id);
        }
        else
        {
            await OnUpdate(update);
        }
    }

    /// <summary>
    /// Handles the updates that make up this bot's own behavior, such as GIFs, photos, or other members
    /// joining a chat. Commands, callback queries and the welcome message are handled before this is called.
    /// Override it to react to the update types the bot cares about; the default implementation does nothing.
    /// </summary>
    protected virtual Task OnUpdate(Update update)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sends the standard language chooser prompt in response to a change-language command.
    /// </summary>
    protected async Task ChangeLanguage(Message message, string language)
    {
        Logger.LogInformation(
            "{LogPrefix} Chat language change requested by {User} in chat '{ChatTitle}' ({ChatId}).",
            Identity.LogPrefix,
            message.From.Describe(),
            message.Chat.Title,
            message.Chat.Id);

        await BotService.Client.SendMessage(
            message.Chat.Id,
            LocalizationService.GetLocalizedString(Name, "ChooseLanguage", language),
            replyParameters: message.MessageId,
            replyMarkup: MarkupService.GenerateChangeLanguageMarkup(Name));
    }

    /// <summary>
    /// Persists the new language, updates the original callback message, and acknowledges the callback.
    /// </summary>
    protected async Task SetLanguage(string callbackId, Message message, User user, string newLanguage)
    {
        await DatabaseService.SetChatLanguage<TChatSettings>(message.Chat.Id, newLanguage);
        Logger.LogInformation(
            "{LogPrefix} Chat language changed to '{Language}' by {User} in chat '{ChatTitle}' ({ChatId}).",
            Identity.LogPrefix,
            newLanguage,
            user.Describe(),
            message.Chat.Title,
            message.Chat.Id);

        await BotService.Client.EditMessageText(
            message.Chat.Id,
            message.MessageId,
            LocalizationService.GetLocalizedString(Name, "NowISpeak", newLanguage));

        await BotService.Client.AnswerCallbackQuery(callbackQueryId: callbackId);
    }

    /// <summary>
    /// Splits callback data at its first ':' into a callback key and a parameter.
    /// The parameter is empty when the data has no ':' and keeps any further ':' characters.
    /// </summary>
    /// <returns><see langword="false"/> when <paramref name="data"/> is null or empty.</returns>
    protected static bool TryParseCallbackData(string? data, out string key, out string parameter)
    {
        if (string.IsNullOrEmpty(data))
        {
            key = string.Empty;
            parameter = string.Empty;
            return false;
        }

        var separatorIndex = data.IndexOf(':');
        if (separatorIndex < 0)
        {
            key = data;
            parameter = string.Empty;
        }
        else
        {
            key = data[..separatorIndex];
            parameter = data[(separatorIndex + 1)..];
        }

        return true;
    }

    /// <summary>
    /// Sends the standard localized welcome message to a chat.
    /// </summary>
    protected async Task Welcome(long chatId)
    {
        var language = await DatabaseService.GetChatLanguage<TChatSettings>(chatId);

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
            ? handler.Handle(message, language)
            : Task.CompletedTask;
    }

    /// <summary>
    /// Dispatches to the registered <see cref="ICallbackHandler"/> for <paramref name="callbackKey"/>, or
    /// does nothing if no handler is registered for it.
    /// </summary>
    protected Task HandleCallback(string callbackKey, string callbackId, Message message, User user, string parameter,
        string language)
    {
        return _callbackHandlers.TryGetValue(callbackKey, out var handler)
            ? handler.Handle(callbackId, message, user, parameter, language)
            : Task.CompletedTask;
    }

    /// <summary>
    /// Reads the bot command that starts a message when the command is meant for this bot: any command in a
    /// private chat, or in other chats a command addressed to this bot's username (case-insensitive),
    /// e.g. <c>/start@my_bot</c>.
    /// </summary>
    /// <param name="command">The command without its <c>@username</c> part, e.g. <c>/start</c>.</param>
    private bool TryGetCommandForThisBot(Message message, out string command)
    {
        command = string.Empty;

        var entity = message.Entities?.FirstOrDefault();
        if (entity is not { Type: MessageEntityType.BotCommand, Offset: 0 }
            || message.Text is not { } text
            || entity.Length > text.Length)
        {
            return false;
        }

        var commandText = text[..entity.Length];
        var separatorIndex = commandText.IndexOf('@');

        if (message.Chat.Type != ChatType.Private)
        {
            // the bot's username is only known once the bot has been enabled
            var username = BotService.Me?.Username;
            var addressee = separatorIndex < 0 ? null : commandText[(separatorIndex + 1)..];
            if (string.IsNullOrEmpty(username)
                || !string.Equals(addressee, username, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        command = separatorIndex < 0 ? commandText : commandText[..separatorIndex];
        return true;
    }
}