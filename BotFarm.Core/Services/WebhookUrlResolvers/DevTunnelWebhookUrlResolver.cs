using BotFarm.Core.Abstractions;

namespace BotFarm.Core.Services.WebhookUrlResolvers;

/// <summary>
/// Resolves the webhook base URL when running under Visual Studio Dev Tunnels.
/// https://learn.microsoft.com/en-us/aspnet/core/test/dev-tunnels?view=aspnetcore-8.0
/// </summary>
public class DevTunnelWebhookUrlResolver : IWebhookUrlResolver
{
    public bool CanResolve(string webHookUrl) => webHookUrl == Constants.WebhookProviders.DevTunnel;

    public Task<string> Resolve(string webHookUrl, CancellationToken cancellationToken = default)
    {
        var devTunnel = Environment.GetEnvironmentVariable("VS_TUNNEL_URL")?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(devTunnel))
        {
            throw new InvalidOperationException("Could not get tunnel URL. Ensure VS dev tunnel is active.");
        }

        return Task.FromResult(devTunnel);
    }
}
