using BotFarm.Core.Abstractions;
using NSubstitute;
using Telegram.Bot.Types.ReplyMarkups;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class MarkupServiceTests
{
    private TestMarkupService _markupService;
    private ILocalizationService _localizationService;

    [SetUp]
    public void SetUp()
    {
        _localizationService = Substitute.For<ILocalizationService>();
        _markupService = new TestMarkupService(_localizationService);
    }

    [Test]
    public void GenerateChangeLanguageMarkup_WithEvenNumberOfLanguages_ReturnsCorrectLayout()
    {
        // Arrange
        const string botName = "TestBot";
        var languages = new[] { "en", "es", "fr", "de" };
        _localizationService.GetAvailableLanguages(botName).Returns(languages);
        _localizationService.GetLocalizedString(botName, "Language", "en").Returns("English");
        _localizationService.GetLocalizedString(botName, "Language", "es").Returns("Español");
        _localizationService.GetLocalizedString(botName, "Language", "fr").Returns("Français");
        _localizationService.GetLocalizedString(botName, "Language", "de").Returns("Deutsch");

        // Act
        var result = _markupService.TestGenerateChangeLanguageMarkup(botName);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result.InlineKeyboard.Count(), Is.EqualTo(2));
        }

        // First row
        var firstRow = result.InlineKeyboard?.FirstOrDefault()?.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstRow, Is.Not.Null);
            Assert.That(firstRow, Has.Count.EqualTo(2));
            Assert.That(firstRow?[0].Text, Is.EqualTo("English"));
            Assert.That(firstRow?[0].CallbackData, Is.EqualTo($"{Constants.Callbacks.LanguageSet}:en"));
            Assert.That(firstRow?[1].Text, Is.EqualTo("Español"));
            Assert.That(firstRow?[1].CallbackData, Is.EqualTo($"{Constants.Callbacks.LanguageSet}:es"));
        }

        // Second row
        var secondRow = result.InlineKeyboard?.Skip(1)?.FirstOrDefault()?.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(secondRow, Is.Not.Null);
            Assert.That(secondRow, Has.Count.EqualTo(2));
            Assert.That(secondRow?[0].Text, Is.EqualTo("Français"));
            Assert.That(secondRow?[0].CallbackData, Is.EqualTo($"{Constants.Callbacks.LanguageSet}:fr"));
            Assert.That(secondRow?[1].Text, Is.EqualTo("Deutsch"));
            Assert.That(secondRow?[1].CallbackData, Is.EqualTo($"{Constants.Callbacks.LanguageSet}:de"));
        }

        _localizationService.Received(1).GetAvailableLanguages(botName);
    }

    [Test]
    public void GenerateChangeLanguageMarkup_WithOddNumberOfLanguages_ReturnsCorrectLayout()
    {
        // Arrange
        const string botName = "TestBot";
        var languages = new[] { "en", "es", "fr" };
        _localizationService.GetAvailableLanguages(botName).Returns(languages);
        _localizationService.GetLocalizedString(botName, "Language", "en").Returns("English");
        _localizationService.GetLocalizedString(botName, "Language", "es").Returns("Español");
        _localizationService.GetLocalizedString(botName, "Language", "fr").Returns("Français");

        // Act
        var result = _markupService.TestGenerateChangeLanguageMarkup(botName);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result.InlineKeyboard.Count(), Is.EqualTo(2));
        }

        // First row
        var firstRow = result.InlineKeyboard?.FirstOrDefault()?.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstRow, Is.Not.Null);
            Assert.That(firstRow, Has.Count.EqualTo(2));
            Assert.That(firstRow?[0].Text, Is.EqualTo("English"));
            Assert.That(firstRow?[0].CallbackData, Is.EqualTo($"{Constants.Callbacks.LanguageSet}:en"));
            Assert.That(firstRow?[1].Text, Is.EqualTo("Español"));
            Assert.That(firstRow?[1].CallbackData, Is.EqualTo($"{Constants.Callbacks.LanguageSet}:es"));
        }

        // Second row
        var secondRow = result.InlineKeyboard?.Skip(1)?.FirstOrDefault()?.ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(secondRow, Is.Not.Null);
            Assert.That(secondRow, Has.Count.EqualTo(1));
            Assert.That(secondRow?[0].Text, Is.EqualTo("Français"));
            Assert.That(secondRow?[0].CallbackData, Is.EqualTo($"{Constants.Callbacks.LanguageSet}:fr"));
        }
    }

    [Test]
    public void GenerateChangeLanguageMarkup_WithSingleLanguage_ReturnsSingleButtonMarkup()
    {
        // Arrange
        const string botName = "TestBot";
        var languages = new[] { "en" };
        _localizationService.GetAvailableLanguages(botName).Returns(languages);
        _localizationService.GetLocalizedString(botName, "Language", "en").Returns("English");

        // Act
        var result = _markupService.TestGenerateChangeLanguageMarkup(botName);
        var firstRow = result.InlineKeyboard?.FirstOrDefault()?.ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result.InlineKeyboard?.Count(), Is.EqualTo(1));
            Assert.That(firstRow, Has.Count.EqualTo(1));
            Assert.That(firstRow?[0].Text, Is.EqualTo("English"));
            Assert.That(firstRow?[0].CallbackData, Is.EqualTo($"{Constants.Callbacks.LanguageSet}:en"));
        }
    }

    private class TestMarkupService : MarkupService
    {
        public override string Name => "TestBot";

        public TestMarkupService(ILocalizationService localizationService)
            : base(localizationService)
        {
        }

        public InlineKeyboardMarkup TestGenerateChangeLanguageMarkup(string botName)
        {
            return GenerateChangeLanguageMarkup(botName);
        }
    }
}
