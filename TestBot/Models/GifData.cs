using MongoDB.Bson.Serialization.Attributes;

namespace TestBot.Models;

/// <summary>
/// Stores the last GIF sent by a user in a chat.
/// </summary>
public class GifData
{
    /// <summary>
    /// The chat and user the GIF belongs to; stored as the document <c>_id</c>, so each pair has at most one record.
    /// </summary>
    [BsonId]
    public GifDataKey Id { get; set; } = new();

    /// <summary>
    /// The Telegram file identifier of the last GIF.
    /// </summary>
    public string FileId { get; set; } = string.Empty;
}

/// <summary>
/// Compound <c>_id</c> of a <see cref="GifData"/> record. <see cref="ChatId"/> is always serialized before
/// <see cref="UserId"/>; chat-wide <c>_id</c> range queries depend on this field order.
/// </summary>
public class GifDataKey
{
    /// <summary>
    /// Telegram chat ID; negative for groups.
    /// </summary>
    [BsonElement(Order = 1)]
    public long ChatId { get; set; }

    /// <summary>
    /// Telegram user ID of the GIF sender.
    /// </summary>
    [BsonElement(Order = 2)]
    public long UserId { get; set; }
}
