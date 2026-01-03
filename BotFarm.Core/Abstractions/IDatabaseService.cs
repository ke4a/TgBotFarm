using BotFarm.Core.Models;

namespace BotFarm.Core.Abstractions;

public interface IDatabaseService : INamedService
{
    Task<bool> Disconnect();

    Task<bool> Reconnect();

    Task<IEnumerable<long>> GetAllChatIds();

    Task<string> GetChatLanguage<TSettings>(long chatId) where TSettings : ChatSettings;

    Task SetChatLanguage<TSettings>(long chatId, string language) where TSettings : ChatSettings;
}
