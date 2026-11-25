using System.Net;
using System.Net.Sockets;
using System.Text;
using BotFarm.Core.Services;
using BotFarm.TestKit;
using Telegram.Bot;

namespace BotFarm.Core.UnitTests.Services;

/// <summary>
/// Makes a real <see cref="TelegramBotClient"/> fail in the ways a Bot API call fails in production and checks that
/// the resulting exception, including its inner exceptions and stack trace, never contains the bot token. Failures
/// are logged with the full exception, so a library or runtime change that puts the request URL into an exception
/// message fails here first.
/// </summary>
[TestFixture]
public class TelegramClientExceptionTests
{
    private const string Token = "123456:TEST-TOKEN-SECRET-PART-abcdefghijklmnop";
    private const string SecretPart = "TEST-TOKEN-SECRET-PART";

    [Test]
    public async Task ConnectionRefused_ExceptionDoesNotContainTheToken()
    {
        var port = GetClosedLocalPort();
        var client = CreateClient(new SocketsHttpHandler(), $"http://127.0.0.1:{port}");

        var exception = await CatchFromGetMe(client);

        AssertTokenNotIn(exception);
    }

    [Test]
    public async Task Timeout_ExceptionDoesNotContainTheToken()
    {
        var client = CreateClient(new NeverAnsweringHandler(), timeout: TimeSpan.FromMilliseconds(100));

        var exception = await CatchFromGetMe(client);

        AssertTokenNotIn(exception);
    }

    [Test]
    public async Task HtmlErrorPage_ExceptionDoesNotContainTheToken()
    {
        var client = CreateClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html><body>502 Bad Gateway</body></html>", Encoding.UTF8, "text/html")
        }));

        var exception = await CatchFromGetMe(client);

        AssertTokenNotIn(exception);
    }

    [Test]
    public async Task MalformedJson_ExceptionDoesNotContainTheToken()
    {
        var client = CreateClient(new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.JsonResponse("{not json")));

        var exception = await CatchFromGetMe(client);

        AssertTokenNotIn(exception);
    }

    [TestCase(401, "Unauthorized")]
    [TestCase(404, "Not Found")]
    [TestCase(429, "Too Many Requests: retry after 5")]
    [TestCase(500, "Internal Server Error")]
    public async Task TelegramError_ExceptionDoesNotContainTheToken(int errorCode, string description)
    {
        var client = CreateClient(new FakeHttpMessageHandler(_ => new HttpResponseMessage((HttpStatusCode)errorCode)
        {
            Content = new StringContent(
                $$"""{"ok":false,"error_code":{{errorCode}},"description":"{{description}}"}""",
                Encoding.UTF8,
                "application/json")
        }));

        var exception = await CatchFromGetMe(client);

        AssertTokenNotIn(exception);
    }

    [Test]
    public async Task HandlerNamingTheRequestUrl_ExceptionContainsTheToken()
    {
        // A handler that writes the request URL into its exception leaks the token; this proves the checks above
        // would catch it.
        var client = CreateClient(new FakeHttpMessageHandler(
            request => throw new HttpRequestException($"Request to {request.RequestUri} failed.")));

        var exception = await CatchFromGetMe(client);

        Assert.That(exception.ToString().Contains(SecretPart, StringComparison.Ordinal), Is.True);
    }

    [Test]
    public void TelegramBotClientFactory_DoesNotTakeAnHttpClientFactory()
    {
        var parameterTypes = typeof(BotFarm.Core.Services.TelegramBotClientFactory)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType);

        Assert.That(parameterTypes, Has.No.Member(typeof(IHttpClientFactory)));
    }

    private static TelegramBotClient CreateClient(
        HttpMessageHandler handler,
        string? baseUrl = null,
        TimeSpan? timeout = null)
    {
        var httpClient = new HttpClient(handler) { Timeout = timeout ?? TimeSpan.FromSeconds(5) };
        return new TelegramBotClient(new TelegramBotClientOptions(Token, baseUrl), httpClient);
    }

    private static async Task<Exception> CatchFromGetMe(TelegramBotClient client)
    {
        try
        {
            await client.GetMe();
        }
        catch (Exception exception)
        {
            return exception;
        }

        Assert.Fail("GetMe did not fail.");
        throw new InvalidOperationException();
    }

    private static void AssertTokenNotIn(Exception exception) =>
        Assert.That(
            exception.ToString().Contains(SecretPart, StringComparison.Ordinal),
            Is.False,
            $"The {exception.GetType().Name} contains the token.");

    private static int GetClosedLocalPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class NeverAnsweringHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("The request was not cancelled.");
        }
    }
}
