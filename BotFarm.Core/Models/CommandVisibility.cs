namespace BotFarm.Core.Models;

/// <summary>
/// Who sees a command in the bot's Telegram command menu. It only controls the menu; a handler still checks
/// permissions itself.
/// </summary>
public enum CommandVisibility
{
    /// <summary>Shown in private chats and to every group member.</summary>
    Everyone,

    /// <summary>Shown in private chats and to group administrators.</summary>
    GroupAdmins,

    /// <summary>Shown in private chats only.</summary>
    PrivateChats
}
