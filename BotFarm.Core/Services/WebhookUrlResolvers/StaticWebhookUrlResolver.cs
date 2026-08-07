using BotFarm.Core.Abstractions;

namespace BotFarm.Core.Services.WebhookUrlResolvers;

/// <summary>
/// Fallback resolver used when "WebHookUrl" is a literal base URL (e.g. a production domain).
/// Must be registered last, since it matches any value not handled by a more specific resolver.
/// </summary>
public class StaticWebhookUrlResolver : IWebhookUrlResolver
{
    public bool CanResolve(string webHookUrl) => true;

    public Task<string> Resolve(string webHookUrl, CancellationToken cancellationToken = default)
    {
        var trimmed = webHookUrl.TrimEnd('/');

        // Telegram only accepts HTTPS webhook URLs:
        // https://core.telegram.org/bots/api#setwebhook
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"Invalid WebHookUrl '{webHookUrl}'. Expected an absolute HTTPS URL (e.g. https://yourdomain.com), " +
                $"or one of the recognized keywords: {Constants.WebhookProviders.DevTunnel}, {Constants.WebhookProviders.LocalTunnel}, {Constants.WebhookProviders.Ngrok}.");
        }

        return Task.FromResult(trimmed);
    }
}
