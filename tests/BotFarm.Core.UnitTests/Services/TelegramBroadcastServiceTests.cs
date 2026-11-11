using BotFarm.Core.Abstractions;
using BotFarm.Core.Services;
using BotFarm.TestKit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class TelegramBroadcastServiceTests
{
    private const string TestBotName = "TestBot";
    private const string Text = "<b>Hello</b>";
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(50);

    private TelegramBotClient _client = default!;
    private List<TimeSpan> _delays = default!;
    private TelegramBroadcastService _service = default!;

    [SetUp]
    public void SetUp()
    {
        _client = BotFarm.TestKit.TelegramBotClientFactory.CreateSubstitute();
        var botService = Substitute.For<IBotService>();
        botService.Client.Returns(_client);
        var botRegistry = Substitute.For<IBotRegistry>();
        botRegistry.GetService<IBotService>(TestBotName).Returns(botService);

        _delays = [];
        _service = new TelegramBroadcastService(
            botRegistry,
            NullLogger<TelegramBroadcastService>.Instance,
            Interval,
            (delay, _) =>
            {
                _delays.Add(delay);
                return Task.CompletedTask;
            });
    }

    private void SetupSend(Func<long, int, Message> respond)
    {
        var attempts = new Dictionary<long, int>();
        _client.SendRequest(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var chatId = call.Arg<SendMessageRequest>().ChatId.Identifier!.Value;
                attempts[chatId] = attempts.GetValueOrDefault(chatId) + 1;
                return respond(chatId, attempts[chatId]);
            });
    }

    private List<SendMessageRequest> SentRequests() => _client.ReceivedCalls()
        .SelectMany(call => call.GetArguments())
        .OfType<SendMessageRequest>()
        .ToList();

    [Test]
    public async Task Broadcast_SendsHtmlMessageToEveryChatWithPauseBetweenSends()
    {
        SetupSend((id, _) => new Message { Chat = new Chat { Id = id } });

        var result = await _service.Broadcast(TestBotName, [1, 2, 3], Text);

        var requests = SentRequests();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(requests.Select(r => r.ChatId.Identifier), Is.EqualTo(new long?[] { 1, 2, 3 }));
            Assert.That(requests.Select(r => r.Text), Is.All.EqualTo(Text));
            Assert.That(requests.Select(r => r.ParseMode), Is.All.EqualTo(ParseMode.Html));
            Assert.That(_delays, Is.EqualTo(new[] { Interval, Interval }));
            Assert.That(result.Sent, Is.EqualTo(3));
            Assert.That(result.Unreachable, Is.Empty);
            Assert.That(result.Failed, Is.Empty);
        }
    }

    [Test]
    public async Task Broadcast_WhenChatsFail_ContinuesAndSeparatesUnreachableFromFailed()
    {
        SetupSend((id, _) => id switch
        {
            2 => throw new ApiRequestException("Forbidden: bot was blocked by the user", 403),
            3 => throw new ApiRequestException("Bad Request: chat not found", 400),
            4 => throw new ApiRequestException("Bad Request: message is too long", 400),
            5 => throw new HttpRequestException("Network down"),
            _ => new Message { Chat = new Chat { Id = id } },
        });

        var result = await _service.Broadcast(TestBotName, [1, 2, 3, 4, 5, 6], Text);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(SentRequests(), Has.Count.EqualTo(6));
            Assert.That(result.Sent, Is.EqualTo(2));
            Assert.That(result.Unreachable, Is.EqualTo(new long[] { 2, 3 }));
            Assert.That(result.Failed, Is.EqualTo(new long[] { 4, 5 }));
        }
    }

    [Test]
    public async Task Broadcast_WhenRateLimited_WaitsRetryAfterAndRetriesSameChat()
    {
        SetupSend((id, attempt) => id == 1 && attempt == 1
            ? throw new ApiRequestException("Too Many Requests: retry after 7", 429, new ResponseParameters { RetryAfter = 7 })
            : new Message { Chat = new Chat { Id = id } });

        var result = await _service.Broadcast(TestBotName, [1, 2], Text);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(SentRequests().Select(r => r.ChatId.Identifier), Is.EqualTo(new long?[] { 1, 1, 2 }));
            Assert.That(_delays, Is.EqualTo(new[] { TimeSpan.FromSeconds(7), Interval }));
            Assert.That(result.Sent, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task Broadcast_WhenRateLimitPersists_GivesUpOnChatAfterMaxAttempts()
    {
        SetupSend((id, _) => id == 1
            ? throw new ApiRequestException("Too Many Requests", 429)
            : new Message { Chat = new Chat { Id = id } });

        var result = await _service.Broadcast(TestBotName, [1, 2], Text);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(SentRequests().Count(r => r.ChatId.Identifier == 1), Is.EqualTo(TelegramBroadcastService.MaxAttemptsPerChat));
            Assert.That(result.Failed, Is.EqualTo(new long[] { 1 }));
            Assert.That(result.Sent, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Broadcast_ReportsProgressAfterEachChat()
    {
        SetupSend((id, _) => id == 2
            ? throw new ApiRequestException("Forbidden", 403)
            : new Message { Chat = new Chat { Id = id } });
        var reported = new List<int>();

        await _service.Broadcast(TestBotName, [1, 2, 3], Text, new SyncProgress(reported.Add));

        Assert.That(reported, Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void Broadcast_WhenCancelled_StopsBeforeNextChat()
    {
        using var cts = new CancellationTokenSource();
        SetupSend((id, _) =>
        {
            cts.Cancel();
            return new Message { Chat = new Chat { Id = id } };
        });

        Assert.ThrowsAsync<OperationCanceledException>(() => _service.Broadcast(TestBotName, [1, 2], Text, null, cts.Token));
        Assert.That(SentRequests(), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Broadcast_WhileSameBotIsBroadcasting_RejectsSecondBroadcastUntilFirstEnds()
    {
        var pending = new TaskCompletionSource<Message>();
        _client.SendRequest(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>()).Returns(pending.Task);

        var first = _service.Broadcast(TestBotName, [1], Text);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.Broadcast(TestBotName, [2], Text));
        pending.SetResult(new Message { Chat = new Chat { Id = 1 } });
        var firstResult = await first;

        _client.SendRequest(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new Message { Chat = new Chat { Id = 3 } });
        var later = await _service.Broadcast(TestBotName, [3], Text);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex!.Message, Does.Contain(TestBotName));
            Assert.That(firstResult.Sent, Is.EqualTo(1));
            Assert.That(later.Sent, Is.EqualTo(1));
        }
    }

    private sealed class SyncProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
