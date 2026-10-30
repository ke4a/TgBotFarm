using Telegram.Bot.Types;

namespace BotFarm.Core.Extensions;

/// <summary>
/// Helpers for working with Telegram <see cref="User"/> values.
/// </summary>
public static class TelegramUserExtensions
{
    /// <summary>
    /// Describes a Telegram user for log messages, e.g. <c>user 'name' (123)</c>, or <c>an unknown user</c>
    /// when <paramref name="user"/> is null.
    /// </summary>
    public static string Describe(this User? user)
    {
        return user == null ? "an unknown user" : $"user '{user.Username}' ({user.Id})";
    }
}
