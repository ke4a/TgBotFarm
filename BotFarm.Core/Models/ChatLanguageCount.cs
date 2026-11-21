namespace BotFarm.Core.Models;

/// <summary>
/// The number of a bot's chats that use one language.
/// </summary>
/// <param name="Language">The language code, such as <c>en-US</c>, or <see langword="null"/> for chats with no language set.</param>
/// <param name="Count">The number of chats.</param>
public sealed record ChatLanguageCount(string? Language, long Count);
