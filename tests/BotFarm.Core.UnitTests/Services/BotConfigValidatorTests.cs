using BotFarm.Core.Services;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class BotConfigValidatorTests
{
    private const string BotName = "TestBot";

    [TestCase("123456789:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [TestCase("123456:ABC-def_GhI")]
    [TestCase("1:x")]
    public void IsValidToken_TokenWithAnySecretPart_ReturnsTrue(string token)
    {
        Assert.That(BotConfigValidator.IsValidToken(token), Is.True);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("bad")]
    [TestCase("123456789")]
    [TestCase("123456789:")]
    [TestCase(":AAAAAAAAAA")]
    [TestCase("abc:AAAAAAAAAA")]
    [TestCase("123 456:AAAAAAAAAA")]
    [TestCase("123456:AAA AAA")]
    [TestCase("99999999999999999999:AAAA")]
    public void IsValidToken_MalformedToken_ReturnsFalse(string? token)
    {
        Assert.That(BotConfigValidator.IsValidToken(token), Is.False);
    }

    [Test]
    public void TryGetTelegramBotId_ValidToken_ReturnsTheDigitsBeforeTheColon()
    {
        var found = BotConfigValidator.TryGetTelegramBotId("123456789:AAAA:BBBB", out var telegramBotId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found, Is.True);
            Assert.That(telegramBotId, Is.EqualTo(123456789));
        }
    }

    [TestCase(null)]
    [TestCase("bad")]
    [TestCase("abc:AAAA")]
    public void TryGetTelegramBotId_MalformedToken_ReturnsFalse(string? token)
    {
        Assert.That(BotConfigValidator.TryGetTelegramBotId(token, out _), Is.False);
    }

    [TestCase(12345L)]
    [TestCase(-1001234567890L)]
    public void IsValidAdminChatId_NonZero_ReturnsTrue(long adminChatId)
    {
        // Group chat IDs are negative.
        Assert.That(BotConfigValidator.IsValidAdminChatId(adminChatId), Is.True);
    }

    [TestCase(null)]
    [TestCase(0L)]
    public void IsValidAdminChatId_MissingOrZero_ReturnsFalse(long? adminChatId)
    {
        Assert.That(BotConfigValidator.IsValidAdminChatId(adminChatId), Is.False);
    }

    [TestCase("my_Secret-123")]
    [TestCase("a")]
    public void IsValidWebhookSecret_AllowedCharacters_ReturnsTrue(string secret)
    {
        Assert.That(BotConfigValidator.IsValidWebhookSecret(secret), Is.True);
    }

    [Test]
    public void IsValidWebhookSecret_256Characters_ReturnsTrue()
    {
        Assert.That(BotConfigValidator.IsValidWebhookSecret(new string('a', 256)), Is.True);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("has space")]
    [TestCase("bad!chars")]
    [TestCase("ünicode")]
    public void IsValidWebhookSecret_InvalidSecret_ReturnsFalse(string? secret)
    {
        Assert.That(BotConfigValidator.IsValidWebhookSecret(secret), Is.False);
    }

    [Test]
    public void IsValidWebhookSecret_TooLong_ReturnsFalse()
    {
        Assert.That(BotConfigValidator.IsValidWebhookSecret(new string('a', 257)), Is.False);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("🤖")]
    [TestCase("A")]
    [TestCase("\U0001F44D\U0001F3FD", TestName = "IsValidEmoji_EmojiWithSkinTone_ReturnsTrue")]
    [TestCase("\U0001F1FA\U0001F1E6", TestName = "IsValidEmoji_Flag_ReturnsTrue")]
    public void IsValidEmoji_NoneOrOneCharacter_ReturnsTrue(string? emoji)
    {
        Assert.That(BotConfigValidator.IsValidEmoji(emoji), Is.True);
    }

    [Test]
    public void IsValidEmoji_TheLongestEmojiSequence_ReturnsTrue()
    {
        // Kiss with two skin tones: ten code points, 15 UTF-16 code units, one visible character.
        const string kiss = "\U0001F469\U0001F3FB\u200D\u2764\uFE0F\u200D\U0001F48B\u200D\U0001F468\U0001F3FC";

        Assert.That(BotConfigValidator.IsValidEmoji(kiss), Is.True);
    }

    [TestCase("🤖✨")]
    [TestCase("ab")]
    [TestCase("\U0001F1FA\U0001F1E6\U0001F1EC\U0001F1E7", TestName = "IsValidEmoji_TwoFlags_ReturnsFalse")]
    public void IsValidEmoji_MoreThanOneCharacter_ReturnsFalse(string emoji)
    {
        Assert.That(BotConfigValidator.IsValidEmoji(emoji), Is.False);
    }

    [Test]
    public void IsValidEmoji_OneCharacterWithManyCombiningMarks_ReturnsFalse()
    {
        var emoji = "a" + new string('\u0301', 20);

        Assert.That(BotConfigValidator.IsValidEmoji(emoji), Is.False);
    }

    [Test]
    public void InvalidWebhookSecretMessage_NamesTheBotAndTheRules()
    {
        Assert.That(BotConfigValidator.InvalidWebhookSecretMessage(BotName),
            Does.Contain("webhook secret of bot TestBot is invalid").And.Contain("1-256 characters"));
    }
}
