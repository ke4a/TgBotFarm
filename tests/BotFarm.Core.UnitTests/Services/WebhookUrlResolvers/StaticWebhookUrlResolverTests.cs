using BotFarm.Core.Services.WebhookUrlResolvers;

namespace BotFarm.Core.UnitTests.Services.WebhookUrlResolvers;

[TestFixture]
public class StaticWebhookUrlResolverTests
{
    private StaticWebhookUrlResolver _resolver;

    [SetUp]
    public void SetUp()
    {
        _resolver = new StaticWebhookUrlResolver();
    }

    [TestCase("https://example.com")]
    [TestCase("devtunnel")]
    [TestCase("localtunnel")]
    [TestCase("ngrok")]
    [TestCase("")]
    [TestCase("garbage")]
    public void CanResolve_AlwaysReturnsTrue(string webHookUrl)
    {
        Assert.That(_resolver.CanResolve(webHookUrl), Is.True);
    }

    [Test]
    public async Task ResolveAsync_WithValidHttpsUrl_ReturnsTrimmedUrl()
    {
        var result = await _resolver.ResolveAsync("https://example.com/");

        Assert.That(result, Is.EqualTo("https://example.com"));
    }

    [Test]
    public async Task ResolveAsync_WithHttpsUrlWithoutTrailingSlash_ReturnsUnchanged()
    {
        var result = await _resolver.ResolveAsync("https://example.com");

        Assert.That(result, Is.EqualTo("https://example.com"));
    }

    [Test]
    public void ResolveAsync_WithHttpUrl_ThrowsInvalidOperationException()
    {
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => _resolver.ResolveAsync("http://example.com"));

        Assert.That(ex.Message, Does.Contain("HTTPS"));
    }

    [TestCase("not-a-url")]
    [TestCase("")]
    [TestCase("ftp://example.com")]
    [TestCase("example.com")]
    public void ResolveAsync_WithInvalidOrNonHttpsUrl_ThrowsInvalidOperationException(string webHookUrl)
    {
        Assert.ThrowsAsync<InvalidOperationException>(() => _resolver.ResolveAsync(webHookUrl));
    }

    [Test]
    public void ResolveAsync_ExceptionMessage_MentionsRecognizedKeywords()
    {
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => _resolver.ResolveAsync("not-a-url"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex.Message, Does.Contain(Constants.WebhookProviders.DevTunnel));
            Assert.That(ex.Message, Does.Contain(Constants.WebhookProviders.LocalTunnel));
            Assert.That(ex.Message, Does.Contain(Constants.WebhookProviders.Ngrok));
        }
    }
}
