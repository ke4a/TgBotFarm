using Telegram.Bot.Types;

namespace BotFarm.Core.Services.Interfaces
{
    public interface IUpdateService : IHandle
    {
        Task ProcessUpdateAsync(Update update);
    }
}
