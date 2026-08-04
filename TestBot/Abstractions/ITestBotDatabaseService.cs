using BotFarm.Core.Abstractions;
using TestBot.Models;

namespace TestBot.Abstractions;

/// <summary>
/// Bot-specific database operations used by the <see cref="Services.TestBotUpdateService"/>.
/// </summary>
public interface ITestBotDatabaseService : IMongoDbDatabaseService
{
    /// <summary>
    /// Saves the last GIF sent by a user in a chat.
    /// </summary>
    void SaveGifData(long chatId, GifData imageData);

    /// <summary>
    /// Returns the last stored GIF for the specified user in a chat.
    /// </summary>
    GifData? GetGifData(long chatId, long userId);

    /// <summary>
    /// Removes all bot-specific stored data for a chat.
    /// </summary>
    void ClearChatData(long chatId);
}
