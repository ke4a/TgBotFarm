using BotFarm.Core.Abstractions;

namespace BotFarm.Core.Services.WebhookUrlResolvers;

/// <summary>
/// Resolves the webhook base URL when running docker-compose with the localtunnel service.
/// The public URL is fixed for the lifetime of the container, so it's read from a static
/// environment variable set by docker-compose.
/// https://theboroer.github.io/localtunnel-www
/// </summary>
public class LocalTunnelWebhookUrlResolver : IWebhookUrlResolver
{
    public bool CanResolve(string webHookUrl) => webHookUrl == Constants.WebhookProviders.LocalTunnel;

    public Task<string> Resolve(string webHookUrl, CancellationToken cancellationToken = default)
    {
        var localTunnel = Environment.GetEnvironmentVariable("LOCALTUNNEL_URL")?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(localTunnel))
        {
            throw new InvalidOperationException("Could not get localtunnel URL. Ensure LOCALTUNNEL_URL is set.");
        }

        return Task.FromResult(localTunnel);
    }
}
