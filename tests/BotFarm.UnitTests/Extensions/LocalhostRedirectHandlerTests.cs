using System.Net;
using BotFarm.Extensions;
using BotFarm.TestKit;

namespace BotFarm.UnitTests.Extensions;

[TestFixture]
public class LocalhostRedirectHandlerTests
{
    [TestCase("0.0.0.0")]
    [TestCase("[::]")]
    public async Task SendAsync_WildcardHost_RewritesHostAndPreservesPortAndPath(string wildcardHost)
    {
        Uri? forwardedUri = null;
        var innerHandler = new FakeHttpMessageHandler(request =>
        {
            forwardedUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var handler = new LocalhostRedirectHandler { InnerHandler = innerHandler };
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync($"http://{wildcardHost}:5050/health");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(forwardedUri?.Host, Is.EqualTo("localhost"));
            Assert.That(forwardedUri?.Port, Is.EqualTo(5050));
            Assert.That(forwardedUri?.AbsolutePath, Is.EqualTo("/health"));
            Assert.That(innerHandler.CallCount, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task SendAsync_NonWildcardHost_LeavesUriUnchanged()
    {
        var originalUri = new Uri("https://example.test:5050/health");
        Uri? forwardedUri = null;
        var innerHandler = new FakeHttpMessageHandler(request =>
        {
            forwardedUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var handler = new LocalhostRedirectHandler { InnerHandler = innerHandler };
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync(originalUri);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(forwardedUri, Is.EqualTo(originalUri));
            Assert.That(innerHandler.CallCount, Is.EqualTo(1));
        }
    }
}
