using BotFarm.Core.Abstractions;
using BotFarm.Core.Base;
using BotFarm.Core.Extensions;
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

/// <summary>
/// Bot-specific update handler that demonstrates command, callback, and GIF-processing flows.
/// Bot-specific commands/callbacks are implemented as separate <see cref="ICommandHandler"/>/
/// <see cref="ICallbackHandler"/> classes under <c>TestBot/Handlers/</c> and dispatched via the
/// base class's handler registry, so this file only owns update-type routing.
/// </summary>
public class TestBotUpdateService : UpdateService
{
    private readonly ITestBotDatabaseService _databaseService;
    private readonly INotificationService _notificationService;
    private readonly BotConfig _config;

    /// <summary>
    /// Creates the TestBot update handler and wires in its bot-specific collaborators.
    /// </summary>
    public TestBotUpdateService(
        [FromKeyedServices(Constants.Name)] IBotService botService,
        IBotControlService botControlService,
        ILogger<TestBotUpdateService> logger,
        ITestBotDatabaseService databaseService,
        ITestBotMarkupService markupService,
        ILocalizationService localizationService,
        IOptionsMonitor<BotConfig> options,
        INotificationService notificationService,
        [FromKeyedServices(Constants.Name)] IEnumerable<ICommandHandler> commandHandlers,
        [FromKeyedServices(Constants.Name)] IEnumerable<ICallbackHandler> callbackHandlers)
        : base(new BotIdentity(Constants.Name), botService, botControlService, logger, databaseService,
            localizationService, markupService, commandHandlers, callbackHandlers)
    {
        _databaseService = databaseService;
        _notificationService = notificationService;
        _config = options.Get(Name);
    }

    /// <summary>
    /// Routes Telegram updates to the TestBot command, callback, or GIF message handlers.
    /// </summary>
    public override async Task ProcessUpdate(Update update)
    {
        if (update.Type == UpdateType.Message
            && update.Message?.Entities?.FirstOrDefault()?.Type == MessageEntityType.BotCommand
            && (update.Message?.Chat.Type == ChatType.Private
                || update.Message?.EntityValues?.FirstOrDefault()
                    ?.EndsWith($"@{_config.Handle}", StringComparison.OrdinalIgnoreCase) == true))
        {
            // handle commands
            var command = update.Message.EntityValues?.FirstOrDefault()?.Split('@')[0];
            if (string.IsNullOrEmpty(command))
            {
                return;
            }

            var language = await _databaseService.GetChatLanguage<TestBotChatSettings>(update.Message.Chat.Id);

            await (command switch
            {
                BotFarm.Core.Constants.Commands.ChangeLanguage => ChangeLanguage(update.Message, language),
                BotFarm.Core.Constants.Commands.Start => Welcome(update.Message.Chat.Id),
                _ => HandleCommand(command, update.Message, language)
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
            // handle callback queries; ignore ones whose message is no longer available or whose data
            // isn't in the expected format, since there is nothing to act on
            var message = update.CallbackQuery.Message;
            if (message == null
                || !TryParseCallbackData(update.CallbackQuery.Data, out var command, out var parameter))
            {
                return;
            }

            var language = await _databaseService.GetChatLanguage<TestBotChatSettings>(message.Chat.Id);
            var user = update.CallbackQuery.From;

            await (command switch
            {
                BotFarm.Core.Constants.Callbacks.LanguageSet => SetLanguage<TestBotChatSettings>(
                    update.CallbackQuery.Id, message, user, parameter),
                _ => HandleCallback(command, update.CallbackQuery.Id, message, user, parameter, language)
            });
        }
        else if (update.Type == UpdateType.Message
                 && update.Message?.Type == MessageType.NewChatMembers
                 && update.Message.NewChatMembers != null
                 && BotService.Me != null
                 && update.Message.NewChatMembers.Any(u => u.Id == BotService.Me.Id))
        {
            // send welcome message when added to a chat
            await Welcome(update.Message.Chat.Id);
        }
    }

    private async Task GifHandler(Message message)
    {
        var fileId = message.Animation?.FileId;
        // GIF data is stored per user, so messages without a sender user (e.g. channel posts) are skipped.
        if (fileId == null || message.From == null)
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
            await _databaseService.SaveGifData(message.Chat.Id, gifData);
            Logger.LogInformation(
                $"{Identity.LogPrefix} Saved GIF data from {message.From.Describe()} in chat '{message.Chat.Title}' ({message.Chat.Id}).");
        }
        catch (Exception ex)
        {
            var errorMessage =
                $"{Identity.LogPrefix} Error saving GIF data from {message.From.Describe()} in chat '{message.Chat.Title}' ({message.Chat.Id}).";
            Logger.LogError(ex, errorMessage);
            await _notificationService.SendErrorNotification(errorMessage, Name, message);
        }
    }
}