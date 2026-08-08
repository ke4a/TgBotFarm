using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace BotFarm.TestKit;

public static class TelegramMessageFactory
{
    public static User CreateUser(long userId, string username = "testuser")
    {
        return new User
        {
            Id = userId,
            Username = username
        };
    }

    public static Message CreateMessage(
        long chatId,
        long userId,
        int messageId = 0,
        string chatTitle = "Test Chat",
        string username = "testuser",
        string? text = null,
        ChatType chatType = ChatType.Private,
        IReadOnlyCollection<MessageEntity>? entities = null,
        Animation? animation = null,
        IReadOnlyCollection<User>? newChatMembers = null,
        DateTime? date = null)
    {
        return new Message
        {
            Id = messageId,
            Text = text,
            Chat = new Chat { Id = chatId, Title = chatTitle, Type = chatType },
            From = CreateUser(userId, username),
            Entities = entities?.ToArray(),
            Animation = animation,
            NewChatMembers = newChatMembers?.ToArray(),
            Date = date ?? new DateTime(2024, 01, 01, 0, 0, 0, DateTimeKind.Utc)
        };
    }

    public static CallbackQuery CreateCallbackQuery(
        string callbackId,
        string data,
        Message? message = null,
        User? user = null,
        long chatId = 12345,
        long userId = 67890,
        string username = "testuser")
    {
        return new CallbackQuery
        {
            Id = callbackId,
            Data = data,
            From = user ?? CreateUser(userId, username),
            Message = message ?? CreateMessage(chatId, userId, username: username)
        };
    }

    public static Update CreateUpdate(int updateId = 0, Message? message = null, CallbackQuery? callbackQuery = null)
    {
        return new Update
        {
            Id = updateId,
            Message = message,
            CallbackQuery = callbackQuery
        };
    }
}
