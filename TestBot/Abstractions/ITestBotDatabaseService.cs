using BotFarm.Core.Abstractions;
using TestBot.Models;

namespace TestBot.Abstractions;

public interface ITestBotDatabaseService : IMongoDbDatabaseService
{
    void SaveGifData(long chatId, GifData imageData);

    GifData? GetGifData(long chatId, long userId);

    void ClearChatData(long chatId);
}
