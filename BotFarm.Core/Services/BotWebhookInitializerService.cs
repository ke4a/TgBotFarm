using BotFarm.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BotFarm.Core.Services;

/// <summary>
/// Pauses disabled bots and points enabled bots' Telegram webhooks at the base URL resolved
/// by the first matching <see cref="IWebhookUrlResolver"/> for the configured "WebHookUrl" value.
/// </summary>
public class BotWebhookInitializerService(
    IConfiguration configuration,
    IEnumerable<IBotService> botServices,
    IEnumerable<IWebhookUrlResolver> resolvers,
    ILogger<BotWebhookInitializerService> logger) : IBotWebhookInitializer
{
    public async Task InitializeAllAsync(CancellationToken cancellationToken = default)
    {
        var webHookUrl = configuration.GetValue<string>("WebHookUrl") ?? string.Empty;
        string? baseUrl = null;

        foreach (var botService in botServices)
        {
            if (!botService.Enabled)
            {
                _ = await botService.Pause();
                continue;
            }

            await botService.Initialize();

            // Resolved lazily and cached: only needed once at least one bot is enabled, and
            // shared across all bots since they all point at the same tunnel/domain.
            baseUrl ??= await ResolveBaseUrlAsync(webHookUrl, cancellationToken);

            await botService.InitializeWebHook($"{baseUrl}/api/{botService.Name}/update");
        }
    }

    private async Task<string> ResolveBaseUrlAsync(string webHookUrl, CancellationToken cancellationToken)
    {
        var resolver = resolvers.FirstOrDefault(r => r.CanResolve(webHookUrl))
            ?? throw new InvalidOperationException($"No webhook URL resolver registered for WebHookUrl '{webHookUrl}'.");

        logger.LogInformation("Resolving webhook base URL using {Resolver} for WebHookUrl '{WebHookUrl}'.", resolver.GetType().Name, webHookUrl);

        return await resolver.ResolveAsync(webHookUrl, cancellationToken);
    }
}
