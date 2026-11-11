using BotFarm.Core.Localization;
using BotFarm.TestKit;
using Microsoft.Extensions.Logging.Abstractions;

namespace BotFarm.Core.UnitTests.Localization;

/// <summary>
/// Checks which admin UI languages <see cref="AdminLanguageCatalog"/> offers and how it validates the shared set.
/// </summary>
[TestFixture]
public class AdminLanguageCatalogTests
{
    private const string SharedSet = "BotFarm.Shared";

    private TempDirectoryScope _scope = default!;

    private string Root => _scope.GetPath("AdminLanguages");

    [SetUp]
    public void SetUp()
    {
        _scope = new TempDirectoryScope();
    }

    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    [Test]
    public void Languages_ComeFromTheSharedSetOnly_EnglishFirstThenByCultureName()
    {
        // Arrange
        WriteFile(SharedSet, "uk-UA", "Українська");
        WriteFile(SharedSet, "en-US", "English");
        WriteFile(SharedSet, "ru-RU", "Русский");
        WriteFile("BotFarm", "de-DE", "Deutsch");
        WriteFile("BotFarm", "en-US", "English");

        // Act
        var catalog = CreateCatalog();

        // Assert
        Assert.That(catalog.Languages.Select(language => language.Culture.Name),
            Is.EqualTo(new[] { "en-US", "ru-RU", "uk-UA" }));
    }

    [Test]
    public void Languages_TakeDisplayNameFromLanguageName()
    {
        // Arrange
        WriteFile(SharedSet, "en-US", "English");
        WriteFile(SharedSet, "ru-RU", "Русский");
        WriteFile(SharedSet, "uk-UA", "Українська");

        // Act
        var catalog = CreateCatalog();

        // Assert
        Assert.That(catalog.Languages.Select(language => language.DisplayName),
            Is.EqualTo(new[] { "English", "Русский", "Українська" }));
    }

    [Test]
    public void Default_IsEnglish()
    {
        // Arrange
        WriteFile(SharedSet, "ru-RU", "Русский");
        WriteFile(SharedSet, "en-US", "English");

        // Act
        var catalog = CreateCatalog();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(catalog.Default.Culture.Name, Is.EqualTo("en-US"));
            Assert.That(catalog.Default.DisplayName, Is.EqualTo("English"));
        }
    }

    [Test]
    public void Find_MatchesCultureNameIgnoringCase()
    {
        // Arrange
        WriteFile(SharedSet, "en-US", "English");
        WriteFile(SharedSet, "ru-RU", "Русский");
        var catalog = CreateCatalog();

        // Act
        var language = catalog.Find("RU-ru");

        // Assert
        Assert.That(language, Is.SameAs(catalog.Languages.Single(entry => entry.Culture.Name == "ru-RU")));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("de-DE")]
    [TestCase("ru")]
    [TestCase("not a culture")]
    [TestCase("ru-RU|uic=ru-RU")]
    public void Find_UnsupportedOrMalformedCulture_ReturnsNull(string? culture)
    {
        // Arrange
        WriteFile(SharedSet, "en-US", "English");
        WriteFile(SharedSet, "ru-RU", "Русский");
        var catalog = CreateCatalog();

        // Act
        var language = catalog.Find(culture);

        // Assert
        Assert.That(language, Is.Null);
    }

    [Test]
    public void Constructor_SharedSetWithoutEnglish_ThrowsInvalidOperationException()
    {
        // Arrange
        WriteFile(SharedSet, "ru-RU", "Русский");
        WriteFile("BotFarm", "en-US", "English");

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => CreateCatalog());
    }

    [Test]
    public void Constructor_MissingSharedSet_ThrowsInvalidOperationException()
    {
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => CreateCatalog());
    }

    [Test]
    public void Constructor_FileWithoutLanguageName_ThrowsInvalidOperationException()
    {
        // Arrange
        WriteFile(SharedSet, "en-US", "English");
        WriteRawFile(SharedSet, "ru-RU", """{ "Common.Cancel": "Отмена" }""");

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => CreateCatalog());

        // Assert
        Assert.That(exception!.Message, Does.Contain("ru-RU"));
    }

    [Test]
    public void Constructor_FileNotNamedAfterCulture_ThrowsWithFileNameAndInvariantModeHint()
    {
        // Arrange
        WriteFile(SharedSet, "en-US", "English");
        WriteFile(SharedSet, "not-a-culture", "Unknown");

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => CreateCatalog());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("not-a-culture.json"));
            Assert.That(exception.Message, Does.Contain("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"));
        }
    }

    private AdminLanguageCatalog CreateCatalog() =>
        new(new JsonAdminStringLocalizerFactory(NullLoggerFactory.Instance, Root));

    private void WriteFile(string set, string culture, string languageName) =>
        WriteRawFile(set, culture, $$"""{ "LanguageName": "{{languageName}}" }""");

    private void WriteRawFile(string set, string culture, string content)
    {
        var directory = Path.Combine(Root, set);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"{culture}.json"), content);
    }
}
