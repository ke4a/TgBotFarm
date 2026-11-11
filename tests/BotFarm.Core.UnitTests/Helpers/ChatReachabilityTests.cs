using BotFarm.Core.Helpers;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;

namespace BotFarm.Core.UnitTests.Helpers;

[TestFixture]
public class ChatReachabilityTests
{
    private static IEnumerable<TestCaseData> UnreachableCases()
    {
        yield return new TestCaseData(new ApiRequestException("Forbidden: bot was kicked from the group chat", 403)).SetName("Kicked");
        yield return new TestCaseData(new ApiRequestException("Forbidden: bot was blocked by the user", 403)).SetName("Blocked");
        yield return new TestCaseData(new ApiRequestException("Bad Request: chat not found", 400)).SetName("ChatNotFound");
    }

    private static IEnumerable<TestCaseData> ReachableCases()
    {
        yield return new TestCaseData(new ApiRequestException("Too Many Requests", 429)).SetName("RateLimited");
        yield return new TestCaseData(new ApiRequestException("Internal Server Error", 500)).SetName("ServerError");
        yield return new TestCaseData(new ApiRequestException("Bad Request: invalid user_id specified", 400)).SetName("OtherBadRequest");
        yield return new TestCaseData(new ApiRequestException(
            "Bad Request: group chat was upgraded to a supergroup chat", 400,
            new ResponseParameters { MigrateToChatId = -1001 })).SetName("Migrated");
        yield return new TestCaseData(new HttpRequestException("network down")).SetName("Network");
    }

    [TestCaseSource(nameof(UnreachableCases))]
    public void IsUnreachable_WhenBotLostAccess_ReturnsTrue(Exception exception)
    {
        Assert.That(ChatReachability.IsUnreachable(exception), Is.True);
    }

    [TestCaseSource(nameof(ReachableCases))]
    public void IsUnreachable_ForOtherErrors_ReturnsFalse(Exception exception)
    {
        Assert.That(ChatReachability.IsUnreachable(exception), Is.False);
    }
}
