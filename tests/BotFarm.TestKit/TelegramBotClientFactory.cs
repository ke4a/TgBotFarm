using NSubstitute;
using Telegram.Bot;

namespace BotFarm.TestKit;

public static class TelegramBotClientFactory
{
    public static TelegramBotClient CreateSubstitute(string token = "123456789:test")
    {
        return Substitute.For<TelegramBotClient>(token, null, CancellationToken.None);
    }
}
