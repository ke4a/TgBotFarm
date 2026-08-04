using MongoDB.Bson.Serialization.Attributes;

namespace BotFarm.Core.Models;

/// <summary>
/// Base persisted settings for a Telegram chat known to a bot.
/// </summary>
public class ChatSettings
{
    [BsonId]
    public long ChatId { get; set; }

    public string? Language { get; set; }
}
