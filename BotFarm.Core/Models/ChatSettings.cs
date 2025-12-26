using MongoDB.Bson.Serialization.Attributes;

namespace BotFarm.Core.Models;

public abstract class ChatSettings
{
    [BsonId]
    public long ChatId { get; set; }

    public string? Language { get; set; }
}
