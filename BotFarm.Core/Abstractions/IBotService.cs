using Telegram.Bot;

namespace BotFarm.Core.Abstractions;

public interface IBotService : INamedService
{
    TelegramBotClient Client { get; }

    Task InitializeWebHook(string url);

    Task<bool> Pause();

    Task<bool> Resume();
}
