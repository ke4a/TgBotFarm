using BotFarm.Core.Models;
using BotFarm.Core.Services;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class BotConfigValidatorTests
{
    private const string BotName = "TestBot";
    private const string Token = "123456789:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private readonly BotConfigValidator _validator = new();

    private static BotConfig ValidConfig() => new()
    {
        Token = Token,
        AdminChatId = 12345,
        WebhookSecret = "my_Secret-123"
    };

    [Test]
    public void Validate_ValidConfig_Succeeds()
    {
        var result = _validator.Validate(BotName, ValidConfig());

        Assert.That(result.Succeeded, Is.True);
    }

    [Test]
    public void Validate_ValidConfigWithoutOptionalValues_Succeeds()
    {
        var config = ValidConfig();
        config.WebhookSecret = null;
        config.Emoji = null;

        Assert.That(_validator.Validate(BotName, config).Succeeded, Is.True);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void Validate_MissingToken_FailsNamingTokenPath(string token)
    {
        var config = ValidConfig();
        config.Token = token;

        var result = _validator.Validate(BotName, config);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failed, Is.True);
            Assert.That(result.FailureMessage, Does.Contain("Bots:TestBot:BotConfig:Token is required"));
        }
    }

    [TestCase("bad")]
    [TestCase("123456789")]
    [TestCase("123456789:")]
    [TestCase(":AAAAAAAAAA")]
    [TestCase("abc:AAAAAAAAAA")]
    [TestCase("123 456:AAAAAAAAAA")]
    [TestCase("123456:AAA AAA")]
    public void Validate_MalformedToken_FailsWithoutExposingToken(string token)
    {
        var config = ValidConfig();
        config.Token = token;

        var result = _validator.Validate(BotName, config);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failed, Is.True);
            Assert.That(result.FailureMessage, Does.Contain("Bots:TestBot:BotConfig:Token is not a valid"));
            Assert.That(result.FailureMessage, Does.Not.Contain(token));
        }
    }

    [TestCase("123456:ABC-def_GhI")]
    [TestCase("1:x")]
    public void Validate_TokenWithAnySecretPart_Succeeds(string token)
    {
        var config = ValidConfig();
        config.Token = token;

        Assert.That(_validator.Validate(BotName, config).Succeeded, Is.True);
    }

    [TestCase("has space")]
    [TestCase("bad!chars")]
    [TestCase("ünicode")]
    public void Validate_InvalidWebhookSecret_FailsWithoutExposingSecretOrToken(string secret)
    {
        var config = ValidConfig();
        config.WebhookSecret = secret;

        var result = _validator.Validate(BotName, config);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failed, Is.True);
            Assert.That(result.FailureMessage, Does.Contain("Bots:TestBot:BotConfig:WebhookSecret is invalid"));
            Assert.That(result.FailureMessage, Does.Not.Contain(secret));
            Assert.That(result.FailureMessage, Does.Not.Contain(Token));
        }
    }

    [Test]
    public void Validate_WebhookSecretTooLong_Fails()
    {
        var config = ValidConfig();
        config.WebhookSecret = new string('a', 257);

        Assert.That(_validator.Validate(BotName, config).Failed, Is.True);
    }

    [Test]
    public void Validate_ZeroAdminChatId_FailsNamingAdminChatIdPath()
    {
        var config = ValidConfig();
        config.AdminChatId = 0;

        var result = _validator.Validate(BotName, config);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failed, Is.True);
            Assert.That(result.FailureMessage, Does.Contain("Bots:TestBot:BotConfig:AdminChatId is required"));
        }
    }

    [Test]
    public void Validate_NegativeAdminChatId_Succeeds()
    {
        // Group chat IDs are negative.
        var config = ValidConfig();
        config.AdminChatId = -1001234567890;

        Assert.That(_validator.Validate(BotName, config).Succeeded, Is.True);
    }

    [Test]
    public void Validate_SeveralProblems_ReportsEach()
    {
        var result = _validator.Validate(BotName, new BotConfig { WebhookSecret = "bad secret" });

        Assert.That(result.Failures, Has.Count.EqualTo(3));
    }
}
