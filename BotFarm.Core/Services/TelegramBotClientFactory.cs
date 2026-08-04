using BotFarm.Core.Abstractions;
using Telegram.Bot;

namespace BotFarm.Core.Services;

/// <inheritdoc cref="ITelegramBotClientFactory"/>
internal sealed class TelegramBotClientFactory : ITelegramBotClientFactory
{
    public TelegramBotClient Create(string token) => new(token);
}
