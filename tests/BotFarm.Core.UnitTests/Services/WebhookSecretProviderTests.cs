using BotFarm.Core.Models;
using BotFarm.Core.Services;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class WebhookSecretProviderTests
{
    private const string BotName = "TestBot";
    private const string Token = "123456789:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private static WebhookSecretProvider CreateProvider(BotConfig config)
    {
        var options = Substitute.For<IOptionsMonitor<BotConfig>>();
        options.Get(BotName).Returns(config);
        return new WebhookSecretProvider(options);
    }

    [Test]
    public void GetSecret_ConfiguredSecret_ReturnsConfiguredValue()
    {
        var provider = CreateProvider(new BotConfig { Token = Token, WebhookSecret = "my_Secret-123" });

        Assert.That(provider.GetSecret(BotName), Is.EqualTo("my_Secret-123"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void GetSecret_NoConfiguredSecret_ReturnsStableCharsetValidDerivedSecret(string? configured)
    {
        var first = CreateProvider(new BotConfig { Token = Token, WebhookSecret = configured }).GetSecret(BotName);
        var second = CreateProvider(new BotConfig { Token = Token, WebhookSecret = configured }).GetSecret(BotName);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(second, Is.EqualTo(first));
            Assert.That(first, Does.Match("^[A-Za-z0-9_-]{1,256}$"));
            Assert.That(first, Does.Not.Contain(Token));
        }
    }

    [Test]
    public void GetSecret_DifferentTokens_ProduceDifferentSecrets()
    {
        var first = CreateProvider(new BotConfig { Token = Token }).GetSecret(BotName);
        var second = CreateProvider(new BotConfig { Token = "987654321:BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB" })
            .GetSecret(BotName);

        Assert.That(second, Is.Not.EqualTo(first));
    }

    [TestCase("has space")]
    [TestCase("bad!chars")]
    [TestCase("ünicode")]
    public void GetSecret_InvalidConfiguredSecret_ThrowsWithoutExposingSecret(string configured)
    {
        var provider = CreateProvider(new BotConfig { Token = Token, WebhookSecret = configured });

        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetSecret(BotName));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("WebhookSecret"));
            Assert.That(exception.Message, Does.Not.Contain(configured));
        }
    }

    [Test]
    public void GetSecret_ConfiguredSecretTooLong_Throws()
    {
        var provider = CreateProvider(new BotConfig { Token = Token, WebhookSecret = new string('a', 257) });

        Assert.Throws<InvalidOperationException>(() => provider.GetSecret(BotName));
    }

    [Test]
    public void GetSecret_NoSecretAndNoToken_Throws()
    {
        var provider = CreateProvider(new BotConfig());

        Assert.Throws<InvalidOperationException>(() => provider.GetSecret(BotName));
    }
}
