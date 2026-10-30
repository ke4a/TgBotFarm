using BotFarm.Core.Helpers;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Servers;
using System.Net;
using System.Net.Sockets;
using Telegram.Bot.Exceptions;

namespace BotFarm.Core.UnitTests.Helpers;

[TestFixture]
public class TransientErrorsTests
{
    private static ConnectionId CreateConnectionId() =>
        new(new ServerId(new ClusterId(), new DnsEndPoint("localhost", 27017)));

    private static IEnumerable<TestCaseData> TransientCases()
    {
        yield return new TestCaseData(new MongoConnectionException(CreateConnectionId(), "down")).SetName("MongoConnection");
        yield return new TestCaseData(new MongoConnectionClosedException(CreateConnectionId())).SetName("MongoConnectionClosed");
        yield return new TestCaseData(new MongoExecutionTimeoutException(CreateConnectionId(), "slow")).SetName("MongoExecutionTimeout");
        yield return new TestCaseData(new TimeoutException("server selection")).SetName("Timeout");
        yield return new TestCaseData(new HttpRequestException("unreachable")).SetName("HttpRequest");
        yield return new TestCaseData(new SocketException((int)SocketError.ConnectionRefused)).SetName("Socket");
        yield return new TestCaseData(new IOException("reset")).SetName("IO");
        yield return new TestCaseData(new TaskCanceledException("timed out", new TimeoutException())).SetName("HttpTimeoutCancellation");
        yield return new TestCaseData(new ApiRequestException("Too Many Requests", 429)).SetName("Telegram429");
        yield return new TestCaseData(new ApiRequestException("Internal Server Error", 500)).SetName("Telegram500");
        yield return new TestCaseData(new ApiRequestException("Bad Gateway", 502)).SetName("Telegram502");
        yield return new TestCaseData(new RequestException("Request timed out", new TaskCanceledException("timeout", new TimeoutException())))
            .SetName("TelegramRequestTimeout");
        yield return new TestCaseData(new RequestException("Exception during making request", new HttpRequestException("down")))
            .SetName("TelegramRequestNetworkFailure");
        yield return new TestCaseData(new InvalidOperationException("wrapper", new Exception("middle", new IOException("reset"))))
            .SetName("NestedInner");
        yield return new TestCaseData(new AggregateException(new ArgumentException("bad"), new HttpRequestException("down")))
            .SetName("AggregateWithTransientMember");
    }

    private static IEnumerable<TestCaseData> PermanentCases()
    {
        yield return new TestCaseData(new ApiRequestException("Bad Request", 400)).SetName("Telegram400");
        yield return new TestCaseData(new ApiRequestException("Forbidden", 403)).SetName("Telegram403");
        yield return new TestCaseData(new RequestException("bad response")).SetName("TelegramRequestWithoutInner");
        yield return new TestCaseData(new ArgumentException("bad")).SetName("Argument");
        yield return new TestCaseData(new NullReferenceException()).SetName("NullReference");
        yield return new TestCaseData(new InvalidOperationException("boom")).SetName("InvalidOperation");
        yield return new TestCaseData(new MongoClientException("bad query")).SetName("MongoClient");
        yield return new TestCaseData(new OperationCanceledException()).SetName("RequestedCancellation");
        yield return new TestCaseData(new TaskCanceledException()).SetName("RequestedTaskCancellation");
        yield return new TestCaseData(new AggregateException(new ArgumentException("a"), new FormatException("b")))
            .SetName("AggregateWithoutTransientMember");
    }

    [TestCaseSource(nameof(TransientCases))]
    public void IsTransient_TemporaryFailure_ReturnsTrue(Exception exception)
    {
        Assert.That(TransientErrors.IsTransient(exception), Is.True);
    }

    [TestCaseSource(nameof(PermanentCases))]
    public void IsTransient_PermanentFailure_ReturnsFalse(Exception exception)
    {
        Assert.That(TransientErrors.IsTransient(exception), Is.False);
    }
}
