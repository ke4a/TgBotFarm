using BotFarm.Core.Extensions;
using BotFarm.TestKit;
using Telegram.Bot.Types;

namespace BotFarm.Core.UnitTests.Extensions;

[TestFixture]
public class TelegramUserExtensionsTests
{
    [Test]
    public void Describe_WithUser_ReturnsUsernameAndId()
    {
        var user = TelegramMessageFactory.CreateUser(123, "alice");

        Assert.That(user.Describe(), Is.EqualTo("user 'alice' (123)"));
    }

    [Test]
    public void Describe_WithNullUser_ReturnsUnknownUser()
    {
        User? user = null;

        Assert.That(user.Describe(), Is.EqualTo("an unknown user"));
    }
}
