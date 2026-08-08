using Telegram.Bot.Types;

namespace BotFarm.TestKit;

public static class TelegramMessageFactory
{
    public static Message CreateMessage(long chatId, long userId, int messageId = 0, string chatTitle = "Test Chat", string username = "testuser")
    {
        return new Message
        {
            Id = messageId,
            Chat = new Chat { Id = chatId, Title = chatTitle },
            From = new User { Id = userId, Username = username }
        };
    }
}
