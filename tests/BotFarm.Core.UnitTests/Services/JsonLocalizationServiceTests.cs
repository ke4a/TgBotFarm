using BotFarm.Core.Services;

namespace BotFarm.Core.UnitTests.Services;

[NonParallelizable]
[TestFixture]
public class JsonLocalizationServiceTests
{
    private JsonLocalizationService _service;
    private string _languagesPath;
    private string _originalCurrentDirectory;

    [SetUp]
    public void SetUp()
    {
        _originalCurrentDirectory = Directory.GetCurrentDirectory();
        _languagesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Languages");
        Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
        Directory.CreateDirectory(Path.Combine(_languagesPath, "Bot1"));
        Directory.CreateDirectory(Path.Combine(_languagesPath, "Bot2"));
        Directory.CreateDirectory(Path.Combine(_languagesPath, "Bot3"));

        File.WriteAllText(Path.Combine(_languagesPath, "Bot1", "en.json"), "{\"hello\": \"Hello\"}");
        File.WriteAllText(Path.Combine(_languagesPath, "Bot1", "es.json"), "{\"hello\": \"Hola\"}");
        File.WriteAllText(Path.Combine(_languagesPath, "Bot2", "en.json"), "{\"hello\": \"Hello\"}");
        File.WriteAllText(Path.Combine(_languagesPath, "Bot3", $"{Constants.DefaultLanguage}.json"),
            "{\"hello\": \"Hello\", \"bye\": \"Bye\"}");
        File.WriteAllText(Path.Combine(_languagesPath, "Bot3", "fr.json"), "{\"hello\": \"Bonjour\"}");

        _service = new JsonLocalizationService();
    }

    [Test]
    public void GetAvailableLanguages_WhenBotHasLanguageFiles_ReturnsLanguageCodes()
    {
        var languages = _service.GetAvailableLanguages("Bot1").ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(languages, Has.Count.EqualTo(2));
            Assert.That(languages, Does.Contain("en"));
            Assert.That(languages, Does.Contain("es"));
        }
    }

    [Test]
    public void GetLocalizedString_WhenKeyExists_ReturnsLocalizedValue()
    {
        var localizedString = _service.GetLocalizedString("Bot1", "hello", "en");
        Assert.That(localizedString, Is.EqualTo("Hello"));

        localizedString = _service.GetLocalizedString("Bot1", "hello", "es");
        Assert.That(localizedString, Is.EqualTo("Hola"));
    }

    [Test]
    public void GetLocalizedString_MatchesBotAndLanguageIgnoringCase()
    {
        Assert.That(_service.GetLocalizedString("bot1", "hello", "ES"), Is.EqualTo("Hola"));
    }

    [Test]
    public void GetLocalizedString_WithMissingKey_ReturnsEmptyString()
    {
        Assert.That(_service.GetLocalizedString("Bot1", "missing", "en"), Is.Empty);
    }

    [TestCase("UnknownBot", "en")]
    [TestCase("Bot2", "es")]
    public void GetLocalizedString_WithUnknownBotOrLanguage_ReturnsEmptyString(string botName, string language)
    {
        Assert.That(_service.GetLocalizedString(botName, "hello", language), Is.Empty);
    }

    [Test]
    public void GetLocalizedString_WithUnknownLanguage_FallsBackToDefaultLanguage()
    {
        Assert.That(_service.GetLocalizedString("Bot3", "hello", "zz"), Is.EqualTo("Hello"));
    }

    [Test]
    public void GetLocalizedString_WithKeyMissingInLanguage_FallsBackToDefaultLanguage()
    {
        Assert.That(_service.GetLocalizedString("Bot3", "bye", "fr"), Is.EqualTo("Bye"));
    }

    [Test]
    public void GetLocalizedString_WithKeyMissingInDefaultLanguageToo_ReturnsEmptyString()
    {
        Assert.That(_service.GetLocalizedString("Bot3", "missing", "fr"), Is.Empty);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.SetCurrentDirectory(_originalCurrentDirectory);
        if (Directory.Exists(_languagesPath))
        {
            Directory.Delete(_languagesPath, true);
        }
    }
}
