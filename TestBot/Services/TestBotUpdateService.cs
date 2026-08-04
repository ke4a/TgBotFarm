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
        ILogger<TestBotUpdateService> logger,
        ITestBotDatabaseService databaseService,
        ITestBotMarkupService markupService,
        ILocalizationService localizationService,
        IOptionsMonitor<BotConfig> options,
        INotificationService notificationService,
        [FromKeyedServices(Constants.Name)] IEnumerable<ICommandHandler> commandHandlers,
        [FromKeyedServices(Constants.Name)] IEnumerable<ICallbackHandler> callbackHandlers)
        : base(new BotIdentity(Constants.Name), botService, logger, databaseService, localizationService, markupService, commandHandlers, callbackHandlers)
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
                || update.Message?.EntityValues?.FirstOrDefault()?.EndsWith(_config.Handle) == true))
        {
            // handle commands
            var language = await _databaseService.GetChatLanguage<TestBotChatSettings>(update.Message.Chat.Id);
            var command = update.Message.EntityValues.First().Split('@')[0];

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
            // handle callback queries
            var language = await _databaseService.GetChatLanguage<TestBotChatSettings>(update.CallbackQuery.Message.Chat.Id);
            var message = update.CallbackQuery.Message;
            var command = update.CallbackQuery.Data.Split(':')[0];
            var parameter = update.CallbackQuery.Data.Split(':')[1];
            var user = update.CallbackQuery.From;

            await (command switch
            {
                BotFarm.Core.Constants.Callbacks.LanguageSet => SetLanguage<TestBotChatSettings>(update.CallbackQuery.Id, message, user, parameter),
                _ => HandleCallback(command, update.CallbackQuery.Id, message, user, parameter, language)
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
            Logger.LogInformation($"{Identity.LogPrefix} Saved GIF data from user '{message.From.Username}' ({message.From.Id}) in chat '{message.Chat.Title}' ({message.Chat.Id}).");
        }
        catch (Exception ex)
        {
            var errorMessage = $"{Identity.LogPrefix} Error saving GIF data from user '{message.From.Username}' ({message.From.Id}) in chat '{message.Chat.Title}' ({message.Chat.Id}).";
            Logger.LogError(ex, errorMessage);
            await _notificationService.SendErrorNotification(errorMessage, Name, message);
        }
    }
}
