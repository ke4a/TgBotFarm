using Telegram.Bot.Types;

namespace BotFarm.Core.Services.Interfaces
{
    public interface IUpdateService : IName
    {
        Task ProcessUpdateAsync(Update update);
    }
}
