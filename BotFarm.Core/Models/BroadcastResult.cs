namespace BotFarm.Core.Models;

/// <summary>
/// The outcome of sending one message to many chats.
/// </summary>
/// <param name="Sent">The number of chats that received the message.</param>
/// <param name="Unreachable">The chats the bot can no longer reach: it was removed or blocked, or the chat no longer exists.</param>
/// <param name="Failed">The chats the message could not be sent to for any other reason.</param>
public sealed record BroadcastResult(int Sent, IReadOnlyList<long> Unreachable, IReadOnlyList<long> Failed);
