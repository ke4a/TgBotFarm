using BotFarm.Core.Abstractions;
using BotFarm.Core.Base;
using BotFarm.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using BotTemplate.Abstractions;
using BotTemplate.Models;

namespace BotTemplate.Services;

/// <summary>
/// Bot-specific update handler for BotTemplate. Commands and callbacks are dispatched to the keyed
/// <see cref="ICommandHandler"/>/<see cref="ICallbackHandler"/> services by the base class;
/// <see cref="OnUpdate"/> receives the updates the base class does not route.
/// </summary>
public class BotTemplateUpdateService : UpdateService<BotTemplateChatSettings>
{
    /// <summary>
    /// Creates the BotTemplate update handler and wires in its bot-specific collaborators.
    /// </summary>
    public BotTemplateUpdateService(
        [FromKeyedServices(Constants.Name)] BotIdentity identity,
        [FromKeyedServices(Constants.Name)] IBotService botService,
        IBotControlService botControlService,
        ILogger<BotTemplateUpdateService> logger,
        IBotTemplateDatabaseService databaseService,
        IBotTemplateMarkupService markupService,
        ILocalizationService localizationService,
        [FromKeyedServices(Constants.Name)] IEnumerable<ICommandHandler> commandHandlers,
        [FromKeyedServices(Constants.Name)] IEnumerable<ICallbackHandler> callbackHandlers)
        : base(identity, botService, botControlService, logger, databaseService,
            localizationService, markupService, commandHandlers, callbackHandlers)
    {
    }

    /// <summary>
    /// Handles updates that are not commands or callbacks.
    /// </summary>
    protected override Task OnUpdate(Update update)
    {
        return Task.CompletedTask;
    }
}
