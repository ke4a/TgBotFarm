using MongoDB.Bson.Serialization.Attributes;

namespace TestBot.Models;

/// <summary>
/// Stores the last GIF sent by a user in a chat.
/// </summary>
public class GifData
{
    [BsonId]
    public long UserId { get; set; }

    public string FileId { get; set; }
}
