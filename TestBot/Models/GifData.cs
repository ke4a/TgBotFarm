using MongoDB.Bson.Serialization.Attributes;

namespace TestBot.Models;

public class GifData
{
    [BsonId]
    public long UserId { get; set; }

    public string FileId { get; set; }
}
