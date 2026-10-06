using MongoDB.Bson.Serialization.Attributes;

namespace BotFarm.Core.Models;

/// <summary>
/// Durable desired state for a registered bot.
/// </summary>
[BsonIgnoreExtraElements]
public sealed record BotControlState
{
    [BsonId] public string BotName { get; init; } = string.Empty;

    public bool DesiredEnabled { get; init; }

    public string? LastCommandId { get; init; }

    public DateTime UpdatedAtUtc { get; init; }
}