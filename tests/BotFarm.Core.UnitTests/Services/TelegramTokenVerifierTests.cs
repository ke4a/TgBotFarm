using BotFarm.Core.Abstractions;
using BotFarm.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using TestClients = BotFarm.TestKit.TelegramBotClientFactory;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class TelegramTokenVerifierTests
{
    private const string Token = "123456:TEST-TOKEN";

    private TelegramBotClient _client = default!;
    private ITelegramBotClientFactory _clientFactory = default!;
    private TelegramTokenVerifier _verifier = default!;

    [SetUp]
    public void SetUp()
    {
        _client = TestClients.CreateSubstitute(Token);
        _clientFactory = Substitute.For<ITelegramBotClientFactory>();
        _clientFactory.Create(Token, Arg.Any<TimeSpan?>()).Returns(_client);
        _verifier = new TelegramTokenVerifier(_clientFactory, NullLogger<TelegramTokenVerifier>.Instance);
    }

    [Test]
    public async Task VerifyAsync_WhenTelegramAcceptsTheToken_ReturnsTheBotIdAndUsername()
    {
        _client.SendRequest(Arg.Any<GetMeRequest>(), Arg.Any<CancellationToken>())
            .Returns(new BotFullInfo { Id = 123456, IsBot = true, FirstName = "Test", Username = "test_bot" });

        var result = await _verifier.VerifyAsync(Token);

        Assert.That(result, Is.EqualTo(new TelegramTokenVerification.Verified(123456, "test_bot")));
        _clientFactory.Received(1).Create(Token, Arg.Any<TimeSpan?>());
    }

    [TestCase(401)]
    [TestCase(404)]
    public async Task VerifyAsync_WhenTelegramRejectsTheToken_ReturnsRejected(int errorCode)
    {
        _client.SendRequest(Arg.Any<GetMeRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiRequestException("Unauthorized", errorCode));

        var result = await _verifier.VerifyAsync(Token);

        Assert.That(result, Is.TypeOf<TelegramTokenVerification.RejectedByTelegram>());
    }

    [Test]
    public async Task VerifyAsync_WhenTelegramCannotBeReached_ReturnsUnreachable()
    {
        _client.SendRequest(Arg.Any<GetMeRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("network"));

        var result = await _verifier.VerifyAsync(Token);

        Assert.That(result, Is.TypeOf<TelegramTokenVerification.TelegramUnreachable>());
    }

    [Test]
    public async Task VerifyAsync_WhenTheRequestTimesOut_ReturnsUnreachable()
    {
        _client.SendRequest(Arg.Any<GetMeRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestException("Bot API Request timed out", new TaskCanceledException()));

        var result = await _verifier.VerifyAsync(Token);

        Assert.That(result, Is.TypeOf<TelegramTokenVerification.TelegramUnreachable>());
    }

    [Test]
    public async Task VerifyAsync_WhenTimeoutExceptionIsThrown_ReturnsUnreachable()
    {
        _client.SendRequest(Arg.Any<GetMeRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException());

        var result = await _verifier.VerifyAsync(Token);

        Assert.That(result, Is.TypeOf<TelegramTokenVerification.TelegramUnreachable>());
    }

    [TestCase(400)]
    [TestCase(429)]
    [TestCase(500)]
    public async Task VerifyAsync_OnOtherApiErrors_ReturnsUnreachable(int errorCode)
    {
        _client.SendRequest(Arg.Any<GetMeRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiRequestException("Some error", errorCode));

        var result = await _verifier.VerifyAsync(Token);

        Assert.That(result, Is.TypeOf<TelegramTokenVerification.TelegramUnreachable>());
    }

    [Test]
    public void VerifyAsync_WhenCancelled_Throws()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        _client.SendRequest(Arg.Any<GetMeRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));

        Assert.CatchAsync<OperationCanceledException>(() => _verifier.VerifyAsync(Token, cancellation.Token));
    }
}
