using Telegram.Bot;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Creates <see cref="TelegramBotClient"/> instances.
/// </summary>
public interface ITelegramBotClientFactory
{
    /// <summary>
    /// Creates a Telegram client authenticated with <paramref name="token"/>.
    /// </summary>
    TelegramBotClient Create(string token);
}
