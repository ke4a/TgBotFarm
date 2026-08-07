using BotFarm.Core.Services.WebhookUrlResolvers;

namespace BotFarm.Core.UnitTests.Services.WebhookUrlResolvers;

[TestFixture]
public class DevTunnelWebhookUrlResolverTests
{
    private const string EnvVarName = "VS_TUNNEL_URL";
    private string? _originalValue;
    private DevTunnelWebhookUrlResolver _resolver;

    [SetUp]
    public void SetUp()
    {
        _originalValue = Environment.GetEnvironmentVariable(EnvVarName);
        _resolver = new DevTunnelWebhookUrlResolver();
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(EnvVarName, _originalValue);
    }

    [Test]
    public void CanResolve_WithDevTunnelKeyword_ReturnsTrue()
    {
        Assert.That(_resolver.CanResolve(Constants.WebhookProviders.DevTunnel), Is.True);
    }

    [TestCase("localtunnel")]
    [TestCase("ngrok")]
    [TestCase("https://example.com")]
    [TestCase("")]
    public void CanResolve_WithOtherValues_ReturnsFalse(string webHookUrl)
    {
        Assert.That(_resolver.CanResolve(webHookUrl), Is.False);
    }

    [Test]
    public async Task Resolve_WithEnvVarSet_ReturnsTrimmedUrl()
    {
        Environment.SetEnvironmentVariable(EnvVarName, "https://foo.devtunnels.ms/");

        var result = await _resolver.Resolve(Constants.WebhookProviders.DevTunnel);

        Assert.That(result, Is.EqualTo("https://foo.devtunnels.ms"));
    }

    [Test]
    public void Resolve_WithEnvVarMissing_ThrowsInvalidOperationException()
    {
        Environment.SetEnvironmentVariable(EnvVarName, null);

        Assert.ThrowsAsync<InvalidOperationException>(() => _resolver.Resolve(Constants.WebhookProviders.DevTunnel));
    }

    [Test]
    public void Resolve_WithEnvVarWhitespace_ThrowsInvalidOperationException()
    {
        Environment.SetEnvironmentVariable(EnvVarName, "   ");

        Assert.ThrowsAsync<InvalidOperationException>(() => _resolver.Resolve(Constants.WebhookProviders.DevTunnel));
    }
}
