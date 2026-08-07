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
    public async Task Resolve_WithValidHttpsUrl_ReturnsTrimmedUrl()
    {
        var result = await _resolver.Resolve("https://example.com/");

        Assert.That(result, Is.EqualTo("https://example.com"));
    }

    [Test]
    public async Task Resolve_WithHttpsUrlWithoutTrailingSlash_ReturnsUnchanged()
    {
        var result = await _resolver.Resolve("https://example.com");

        Assert.That(result, Is.EqualTo("https://example.com"));
    }

    [Test]
    public void Resolve_WithHttpUrl_ThrowsInvalidOperationException()
    {
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => _resolver.Resolve("http://example.com"));

        Assert.That(ex.Message, Does.Contain("HTTPS"));
    }

    [TestCase("not-a-url")]
    [TestCase("")]
    [TestCase("ftp://example.com")]
    [TestCase("example.com")]
    public void Resolve_WithInvalidOrNonHttpsUrl_ThrowsInvalidOperationException(string webHookUrl)
    {
        Assert.ThrowsAsync<InvalidOperationException>(() => _resolver.Resolve(webHookUrl));
    }

    [Test]
    public void Resolve_ExceptionMessage_MentionsRecognizedKeywords()
    {
        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => _resolver.Resolve("not-a-url"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex.Message, Does.Contain(Constants.WebhookProviders.DevTunnel));
            Assert.That(ex.Message, Does.Contain(Constants.WebhookProviders.LocalTunnel));
            Assert.That(ex.Message, Does.Contain(Constants.WebhookProviders.Ngrok));
        }
    }
}
