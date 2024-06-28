using Telegram.Bot;

namespace BotFarm.Core.Services.Interfaces
{
    public interface IBotService : IName
    {
        TelegramBotClient Client { get; }

        Task InitializeWebHook(string url);

        Task<bool> Pause();

        Task<bool> Resume();
    }
}