using System.Text.Json.Nodes;

namespace BotFarm.IntegrationTests.Support;

/// <summary>
/// Builds Telegram updates in the JSON form Telegram posts to a bot's webhook.
/// </summary>
/// <example>
/// <code>
/// var update = TelegramUpdates.Message(TelegramUpdates.PrivateChat(chatId)).WithCommand("/start").AsUpdate();
/// </code>
/// </example>
public static class TelegramUpdates
{
    private static int _lastUpdateId;
    private static long _lastChatId;

    /// <summary>
    /// An update ID no other update in the test run has.
    /// </summary>
    public static int NewUpdateId() => Interlocked.Increment(ref _lastUpdateId);

    /// <summary>
    /// A positive chat ID no other chat in the test run has. A private chat has its user's ID.
    /// </summary>
    public static long NewChatId() => 1_000_000 + Interlocked.Increment(ref _lastChatId);

    /// <summary>
    /// A private chat with the user <paramref name="userId"/>.
    /// </summary>
    public static JsonObject PrivateChat(long userId)
    {
        return new JsonObject { ["id"] = userId, ["type"] = "private", ["first_name"] = "Tester" };
    }

    /// <summary>
    /// A supergroup with the ID <paramref name="chatId"/>, which is negative for groups.
    /// </summary>
    public static JsonObject GroupChat(long chatId)
    {
        return new JsonObject { ["id"] = chatId, ["type"] = "supergroup", ["title"] = "Integration tests" };
    }

    /// <summary>
    /// A message in <paramref name="chat"/> from the user <paramref name="fromUserId"/>; in a private chat the sender
    /// is the chat's user unless given.
    /// </summary>
    public static JsonObject Message(JsonObject chat, long? fromUserId = null)
    {
        var senderId = fromUserId ?? (chat["type"]?.GetValue<string>() == "private" ? chat["id"]!.GetValue<long>() : 777);

        return new JsonObject
        {
            ["message_id"] = NewUpdateId(),
            ["date"] = 1_767_225_600,
            ["chat"] = chat.DeepClone(),
            ["from"] = new JsonObject { ["id"] = senderId, ["is_bot"] = false, ["first_name"] = "Tester" }
        };
    }

    /// <summary>
    /// Makes <paramref name="message"/> a bot command, such as <c>/start</c> or <c>/start@some_bot</c>.
    /// </summary>
    public static JsonObject WithCommand(this JsonObject message, string command)
    {
        message["text"] = command;
        message["entities"] = new JsonArray(new JsonObject
        {
            ["type"] = "bot_command",
            ["offset"] = 0,
            ["length"] = command.Length
        });

        return message;
    }

    /// <summary>
    /// Makes <paramref name="message"/> carry the GIF <paramref name="fileId"/>.
    /// </summary>
    public static JsonObject WithAnimation(this JsonObject message, string fileId)
    {
        message["animation"] = new JsonObject
        {
            ["file_id"] = fileId,
            ["file_unique_id"] = $"unique-{fileId}",
            ["width"] = 320,
            ["height"] = 240,
            ["duration"] = 2
        };

        return message;
    }

    /// <summary>
    /// Wraps <paramref name="message"/> in an update, with a new update ID unless given.
    /// </summary>
    public static JsonObject AsUpdate(this JsonObject message, int? updateId = null)
    {
        return new JsonObject { ["update_id"] = updateId ?? NewUpdateId(), ["message"] = message };
    }
}
