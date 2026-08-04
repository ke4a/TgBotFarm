using BotFarm.Core.Services;
namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class JsonLocalizationServiceTests
{
    private JsonLocalizationService _service;

    [SetUp]
    public void SetUp()
    {
        Directory.SetCurrentDirectory(TestContext.CurrentContext.TestDirectory);
        Directory.CreateDirectory("Languages/Bot1");
        Directory.CreateDirectory("Languages/Bot2");

        File.WriteAllText("Languages/Bot1/en.json", "{\"hello\": \"Hello\"}");
        File.WriteAllText("Languages/Bot1/es.json", "{\"hello\": \"Hola\"}");
        File.WriteAllText("Languages/Bot2/en.json", "{\"hello\": \"Hello\"}");

        _service = new JsonLocalizationService();
    }

    [Test]
    public void GetAvailableLanguages_ShouldReturnCorrectLanguages()
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
    public void GetLocalizedString_ShouldReturnCorrectString()
    {
        var localizedString = _service.GetLocalizedString("Bot1", "hello", "en");
        Assert.That(localizedString, Is.EqualTo("Hello"));

        localizedString = _service.GetLocalizedString("Bot1", "hello", "es");
        Assert.That(localizedString, Is.EqualTo("Hola"));
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete("Languages", true);
    }
}
