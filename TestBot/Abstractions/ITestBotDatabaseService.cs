using BotFarm.Core.Abstractions;
using TestBot.Models;

namespace TestBot.Abstractions;

/// <summary>
/// Bot-specific database operations used by the <see cref="Services.TestBotUpdateService"/>.
/// </summary>
public interface ITestBotDatabaseService : IMongoDbDatabaseService
{
    /// <summary>
    /// Saves the last GIF sent by a user in a chat in the bot's GIF collection, replacing any earlier one.
    /// </summary>
    Task SaveGifData(long chatId, long userId, string fileId);

    /// <summary>
    /// Returns the last GIF stored in the bot's GIF collection for the specified user in a chat.
    /// </summary>
    Task<GifData?> GetGifData(long chatId, long userId);

    /// <summary>
    /// Removes all of a chat's records from the bot's GIF collection.
    /// </summary>
    Task ClearChatData(long chatId);
}
