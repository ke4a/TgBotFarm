using Telegram.Bot;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Creates <see cref="TelegramBotClient"/> instances.
/// </summary>
public interface ITelegramBotClientFactory
{
    TelegramBotClient Create(string token);
}
