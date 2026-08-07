using BotFarm.Core.Services.WebhookUrlResolvers;
using BotFarm.Core.UnitTests.TestHelpers;
using NSubstitute;

namespace BotFarm.Core.UnitTests.Services.WebhookUrlResolvers;

[TestFixture]
public class NgrokWebhookUrlResolverTests
{
    private IHttpClientFactory _httpClientFactory;

    [SetUp]
    public void SetUp()
    {
        _httpClientFactory = Substitute.For<IHttpClientFactory>();
    }

    [Test]
    public void CanResolve_WithNgrokKeyword_ReturnsTrue()
    {
        var resolver = new NgrokWebhookUrlResolver(_httpClientFactory);

        Assert.That(resolver.CanResolve(Constants.WebhookProviders.Ngrok), Is.True);
    }

    [TestCase("devtunnel")]
    [TestCase("localtunnel")]
    [TestCase("https://example.com")]
    [TestCase("")]
    public void CanResolve_WithOtherValues_ReturnsFalse(string webHookUrl)
    {
        var resolver = new NgrokWebhookUrlResolver(_httpClientFactory);

        Assert.That(resolver.CanResolve(webHookUrl), Is.False);
    }

    [Test]
    public async Task ResolveAsync_WithSingleHttpsTunnel_ReturnsTrimmedPublicUrl()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.JsonResponse(
            """{"tunnels":[{"public_url":"https://abcd1234.ngrok-free.app/"}]}"""));
        _httpClientFactory.CreateClient().Returns(new HttpClient(handler));
        var resolver = new NgrokWebhookUrlResolver(_httpClientFactory);

        var result = await resolver.ResolveAsync(Constants.WebhookProviders.Ngrok);

        Assert.That(result, Is.EqualTo("https://abcd1234.ngrok-free.app"));
    }

    [Test]
    public async Task ResolveAsync_WithMultipleTunnels_PrefersHttpsOverHttp()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.JsonResponse(
            """
            {"tunnels":[
                {"public_url":"http://abcd1234.ngrok-free.app"},
                {"public_url":"https://abcd1234.ngrok-free.app"}
            ]}
            """));
        _httpClientFactory.CreateClient().Returns(new HttpClient(handler));
        var resolver = new NgrokWebhookUrlResolver(_httpClientFactory);

        var result = await resolver.ResolveAsync(Constants.WebhookProviders.Ngrok);

        Assert.That(result, Is.EqualTo("https://abcd1234.ngrok-free.app"));
    }

    [Test]
    public async Task ResolveAsync_WithOnlyHttpTunnel_FallsBackToHttpUrl()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.JsonResponse(
            """{"tunnels":[{"public_url":"http://abcd1234.ngrok-free.app"}]}"""));
        _httpClientFactory.CreateClient().Returns(new HttpClient(handler));
        var resolver = new NgrokWebhookUrlResolver(_httpClientFactory);

        var result = await resolver.ResolveAsync(Constants.WebhookProviders.Ngrok);

        Assert.That(result, Is.EqualTo("http://abcd1234.ngrok-free.app"));
    }

    [Test]
    public async Task ResolveAsync_WithTransientHttpFailureThenSuccess_RetriesAndSucceeds()
    {
        var attempt = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            attempt++;
            if (attempt == 1)
            {
                throw new HttpRequestException("connection refused");
            }

            return FakeHttpMessageHandler.JsonResponse("""{"tunnels":[{"public_url":"https://retry.ngrok-free.app"}]}""");
        });
        _httpClientFactory.CreateClient().Returns(new HttpClient(handler));
        var resolver = new NgrokWebhookUrlResolver(_httpClientFactory, maxAttempts: 5, retryDelay: TimeSpan.Zero);

        var result = await resolver.ResolveAsync(Constants.WebhookProviders.Ngrok);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo("https://retry.ngrok-free.app"));
            Assert.That(attempt, Is.EqualTo(2));
        }
    }

    [Test]
    public void ResolveAsync_WithNoTunnelsAfterMaxAttempts_ThrowsInvalidOperationException()
    {
        var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.JsonResponse("""{"tunnels":[]}"""));
        _httpClientFactory.CreateClient().Returns(new HttpClient(handler));
        var resolver = new NgrokWebhookUrlResolver(_httpClientFactory, maxAttempts: 3, retryDelay: TimeSpan.Zero);

        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(Constants.WebhookProviders.Ngrok));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex.Message, Does.Contain("ngrok"));
            Assert.That(handler.CallCount, Is.EqualTo(3));
        }
    }

    [Test]
    public void ResolveAsync_WithPersistentHttpFailure_ThrowsAfterExhaustingAttempts()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("connection refused"));
        _httpClientFactory.CreateClient().Returns(new HttpClient(handler));
        var resolver = new NgrokWebhookUrlResolver(_httpClientFactory, maxAttempts: 3, retryDelay: TimeSpan.Zero);

        Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(Constants.WebhookProviders.Ngrok));
        Assert.That(handler.CallCount, Is.EqualTo(3));
    }
}
