using Telegram.Bot.Types;

namespace BotFarm.Core.Abstractions;

public interface IUpdateService : INamedService
{
    Task ProcessUpdateAsync(Update update);
}
