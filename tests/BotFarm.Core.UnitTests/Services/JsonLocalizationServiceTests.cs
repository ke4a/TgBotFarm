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

        File.WriteAllText(Path.Combine(_languagesPath, "Bot1", "en.json"), "{\"hello\": \"Hello\"}");
        File.WriteAllText(Path.Combine(_languagesPath, "Bot1", "es.json"), "{\"hello\": \"Hola\"}");
        File.WriteAllText(Path.Combine(_languagesPath, "Bot2", "en.json"), "{\"hello\": \"Hello\"}");

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
