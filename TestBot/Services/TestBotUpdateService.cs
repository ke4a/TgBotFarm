using BotFarm.Core.Abstractions;
using BotFarm.Core.Base;
using BotFarm.Core.Extensions;
using BotFarm.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
/// base class's handler registry, so this file only handles the GIF messages the base class does not route.
/// </summary>
public class TestBotUpdateService : UpdateService<TestBotChatSettings>
{
    private readonly ITestBotDatabaseService _databaseService;
    private readonly INotificationService _notificationService;

    /// <summary>
    /// Creates the TestBot update handler and wires in its bot-specific collaborators.
    /// </summary>
    public TestBotUpdateService(
        [FromKeyedServices(Constants.Name)] BotIdentity identity,
        [FromKeyedServices(Constants.Name)] IBotService botService,
        IBotControlService botControlService,
        ILogger<TestBotUpdateService> logger,
        ITestBotDatabaseService databaseService,
        ITestBotMarkupService markupService,
        ILocalizationService localizationService,
        INotificationService notificationService,
        [FromKeyedServices(Constants.Name)] IEnumerable<ICommandHandler> commandHandlers,
        [FromKeyedServices(Constants.Name)] IEnumerable<ICallbackHandler> callbackHandlers)
        : base(identity, botService, botControlService, logger, databaseService,
            localizationService, markupService, commandHandlers, callbackHandlers)
    {
        _databaseService = databaseService;
        _notificationService = notificationService;
    }

    /// <summary>
    /// Saves GIFs posted in a chat; other updates are ignored.
    /// </summary>
    protected override async Task OnUpdate(Update update)
    {
        if (update.Type == UpdateType.Message
            && update.Message?.Type == MessageType.Animation)
        {
            await GifHandler(update.Message);
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
            await _databaseService.SaveGifData(message.Chat.Id, message.From.Id, fileId);
            Logger.LogInformation(
                "{LogPrefix} Saved GIF data from {User} in chat '{ChatTitle}' ({ChatId}).",
                Identity.LogPrefix,
                message.From.Describe(),
                message.Chat.Title,
                message.Chat.Id);
        }
        catch (Exception ex)
        {
            var errorMessage =
                $"{Identity.LogPrefix} Error saving GIF data from {message.From.Describe()} in chat '{message.Chat.Title}' ({message.Chat.Id}).";
            Logger.LogError(
                ex,
                "{LogPrefix} Error saving GIF data from {User} in chat '{ChatTitle}' ({ChatId}).",
                Identity.LogPrefix,
                message.From.Describe(),
                message.Chat.Title,
                message.Chat.Id);
            await _notificationService.SendErrorNotification(errorMessage, Name, message);
        }
    }
}