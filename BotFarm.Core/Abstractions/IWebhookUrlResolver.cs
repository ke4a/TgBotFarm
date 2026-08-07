namespace BotFarm.Core.Abstractions;

/// <summary>
/// Resolves the public base URL that Telegram webhooks should point to, for a given
/// "WebHookUrl" configuration value (e.g. a literal production URL, or a
/// keyword such as "devtunnel", "localtunnel" or "ngrok").
/// </summary>
public interface IWebhookUrlResolver
{
    /// <summary>
    /// Whether this resolver knows how to handle the given "WebHookUrl" configuration value.
    /// </summary>
    bool CanResolve(string webHookUrl);

    /// <summary>
    /// Resolves the public base URL for the given "WebHookUrl" value.
    /// </summary>
    Task<string> ResolveAsync(string webHookUrl, CancellationToken cancellationToken = default);
}
