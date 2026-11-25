using BotFarm.Core.Abstractions;
using Telegram.Bot;

namespace BotFarm.Core.Services;

/// <inheritdoc cref="ITelegramBotClientFactory"/>
/// <remarks>
/// All clients share one <see cref="SocketsHttpHandler"/>, so they share a connection pool and a client that is
/// replaced or dropped leaves no handler behind. Each client gets its own <see cref="HttpClient"/> for its timeout.
/// The clients do not come from <see cref="IHttpClientFactory"/>, because that factory's request logging writes the
/// full request URL, and Bot API URLs contain the token.
/// </remarks>
internal sealed class TelegramBotClientFactory : ITelegramBotClientFactory, IDisposable
{
    /// <summary>
    /// How long a Bot API request may take by default. It is kept well under
    /// <see cref="Constants.TelegramWebhookTimeout"/>, so an update whose request stalls fails, and the webhook
    /// answers, before Telegram gives up.
    /// </summary>
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(25);

    /// <summary>
    /// The handler every client sends through. Pooled connections are replaced regularly, so a connection that was
    /// silently dropped somewhere on the network is less likely to be reused. Cookies are off, so no state passes
    /// from one bot's responses to another bot's requests.
    /// </summary>
    internal SocketsHttpHandler Handler { get; } = new()
    {
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        UseCookies = false
    };

    public TelegramBotClient Create(string token, TimeSpan? timeout = null)
    {
        var httpClient = new HttpClient(Handler, disposeHandler: false) { Timeout = timeout ?? DefaultTimeout };

        return new TelegramBotClient(new TelegramBotClientOptions(token), httpClient);
    }

    public void Dispose() => Handler.Dispose();
}
