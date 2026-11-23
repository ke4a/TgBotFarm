using BotFarm.Core.Abstractions;
using Telegram.Bot;

namespace BotFarm.Core.Services;

/// <inheritdoc cref="ITelegramBotClientFactory"/>
internal sealed class TelegramBotClientFactory : ITelegramBotClientFactory
{
    /// <summary>
    /// How long a Bot API request may take by default. It is kept well under
    /// <see cref="Constants.TelegramWebhookTimeout"/>, so an update whose request stalls fails, and the webhook
    /// answers, before Telegram gives up.
    /// </summary>
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(25);

    public TelegramBotClient Create(string token, TimeSpan? timeout = null)
    {
        // Pooled connections are replaced regularly, so a connection that was silently dropped
        // somewhere on the network is less likely to be reused.
        var handler = new SocketsHttpHandler
        {
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        var httpClient = new HttpClient(handler) { Timeout = timeout ?? DefaultTimeout };

        return new TelegramBotClient(new TelegramBotClientOptions(token), httpClient);
    }
}
