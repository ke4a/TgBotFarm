using Telegram.Bot;
using Telegram.Bot.Types;

namespace BotFarm.Core.Abstractions;

public interface IBotService : INamedService
{
    TelegramBotClient Client { get; }

    Task InitializeWebHook(string url);

    Task<bool> Pause();

    Task<bool> Resume();

    Task Initialize();

    string TempPath { get; }

    User Me { get; }
}
