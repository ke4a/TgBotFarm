using BotFarm.Core.Services.WebhookUrlResolvers;

namespace BotFarm.Core.UnitTests.Services.WebhookUrlResolvers;

[TestFixture]
public class LocalTunnelWebhookUrlResolverTests
{
    private const string EnvVarName = "LOCALTUNNEL_URL";
    private string? _originalValue;
    private LocalTunnelWebhookUrlResolver _resolver;

    [SetUp]
    public void SetUp()
    {
        _originalValue = Environment.GetEnvironmentVariable(EnvVarName);
        _resolver = new LocalTunnelWebhookUrlResolver();
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(EnvVarName, _originalValue);
    }

    [Test]
    public void CanResolve_WithLocalTunnelKeyword_ReturnsTrue()
    {
        Assert.That(_resolver.CanResolve(Constants.WebhookProviders.LocalTunnel), Is.True);
    }

    [TestCase("devtunnel")]
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
        Environment.SetEnvironmentVariable(EnvVarName, "https://botfarm-webhook.loca.lt/");

        var result = await _resolver.Resolve(Constants.WebhookProviders.LocalTunnel);

        Assert.That(result, Is.EqualTo("https://botfarm-webhook.loca.lt"));
    }

    [Test]
    public void Resolve_WithEnvVarMissing_ThrowsInvalidOperationException()
    {
        Environment.SetEnvironmentVariable(EnvVarName, null);

        Assert.ThrowsAsync<InvalidOperationException>(() => _resolver.Resolve(Constants.WebhookProviders.LocalTunnel));
    }

    [Test]
    public void Resolve_WithEnvVarWhitespace_ThrowsInvalidOperationException()
    {
        Environment.SetEnvironmentVariable(EnvVarName, "   ");

        Assert.ThrowsAsync<InvalidOperationException>(() => _resolver.Resolve(Constants.WebhookProviders.LocalTunnel));
    }
}
