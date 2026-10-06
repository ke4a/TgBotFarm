using BotFarm.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BotFarm.Core.Services;

/// <summary>
/// Applies bot lifecycle transitions using the configured webhook URL resolver.
/// </summary>
public class BotWebhookInitializerService(
    IConfiguration configuration,
    IEnumerable<IWebhookUrlResolver> resolvers,
    ILogger<BotWebhookInitializerService> logger) : IBotWebhookInitializer
{
    public async Task EnableAsync(IBotService botService, CancellationToken cancellationToken = default)
    {
        var webHookUrl = configuration.GetValue<string>("WebHookUrl") ?? string.Empty;

        await botService.Initialize(cancellationToken);
        var baseUrl = await ResolveBaseUrl(webHookUrl, cancellationToken);
        await botService.InitializeWebHook($"{baseUrl}/api/{botService.Name}/update", cancellationToken);
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
}