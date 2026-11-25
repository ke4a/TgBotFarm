using BotFarm.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;

namespace BotFarm.Core.Services;

/// <summary>
/// Applies bot lifecycle transitions using the configured webhook URL resolver.
/// </summary>
public class BotWebhookInitializerService(
    IConfiguration configuration,
    IEnumerable<IWebhookUrlResolver> resolvers,
    IServiceScopeFactory scopeFactory,
    ILocalizationService localizationService,
    INotificationService notificationService,
    ILogger<BotWebhookInitializerService> logger) : IBotWebhookInitializer
{
    /// <summary>
    /// Initializes the bot, sets its webhook, then registers its command menus (see <see cref="BotCommandMenu"/>).
    /// A failed command menu registration is logged and reported to the bot's admin chat but doesn't fail enabling.
    /// </summary>
    public async Task EnableAsync(IBotService botService, CancellationToken cancellationToken = default)
    {
        var webHookUrl = configuration.GetValue<string>("WebHookUrl") ?? string.Empty;

        await botService.Initialize(cancellationToken);
        var baseUrl = await ResolveBaseUrl(webHookUrl, cancellationToken);
        await botService.InitializeWebHook($"{baseUrl}/api/{botService.Name}/update", cancellationToken);
        await RegisterCommandMenus(botService, cancellationToken);
    }

    public Task<bool> DisableAsync(IBotService botService, CancellationToken cancellationToken = default)
    {
        return botService.Pause(cancellationToken);
    }

    private async Task<string> ResolveBaseUrl(string webHookUrl, CancellationToken cancellationToken)
    {
        var resolver = resolvers.FirstOrDefault(r => r.CanResolve(webHookUrl))
                       ?? throw new InvalidOperationException(
                           $"No webhook URL resolver registered for WebHookUrl '{webHookUrl}'.");

        logger.LogInformation("Resolving webhook base URL using {Resolver} for WebHookUrl '{WebHookUrl}'.",
            resolver.GetType().Name, webHookUrl);

        return await resolver.Resolve(webHookUrl, cancellationToken);
    }

    private async Task RegisterCommandMenus(IBotService botService, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<BotCommandMenu.CommandList> lists;
            using (var scope = scopeFactory.CreateScope())
            {
                var handlers = scope.ServiceProvider.GetKeyedServices<ICommandHandler>(botService.Name);
                lists = BotCommandMenu.Build(botService.Name, handlers, localizationService);
            }

            // An empty list is deleted, so Telegram falls back to the next broader scope's list.
            foreach (var list in lists)
            {
                if (list.Commands.Count == 0)
                {
                    await botService.Client.DeleteMyCommands(list.Scope, list.LanguageCode, cancellationToken);
                }
                else
                {
                    await botService.Client.SetMyCommands(list.Commands, list.Scope, list.LanguageCode,
                        cancellationToken);
                }
            }

            logger.LogInformation("Registered {ListCount} command menus for bot {BotName}.", lists.Count,
                botService.Name);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var reason = $"{exception.GetType().Name}: {exception.Message}";

            logger.LogError(exception, "Could not register the command menus of bot {BotName}.", botService.Name);
            await notificationService.SendErrorNotification(
                $"Could not register the command menus of bot {botService.Name} ({reason}). The bot keeps running " +
                "without an up-to-date command menu.",
                botService.Name);
        }
    }
}
