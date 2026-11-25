using BotFarm.Core.Exceptions;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Supplies the per-bot secret Telegram sends in the <c>X-Telegram-Bot-Api-Secret-Token</c> webhook header.
/// </summary>
public interface IWebhookSecretProvider
{
    /// <summary>
    /// Returns the webhook secret for <paramref name="botName"/>: the configured
    /// <c>WebhookSecret</c> when set, otherwise a stable value derived from the bot token.
    /// </summary>
    /// <exception cref="InvalidOperationException">The configured secret is invalid.</exception>
    /// <exception cref="BotNotConfiguredException">No secret is configured and the bot token is missing.</exception>
    string GetSecret(string botName);
}
