using System.Globalization;
using BotFarm.Core.Localization;
using BotFarm.TestKit;
using Microsoft.Extensions.Logging.Abstractions;

namespace BotFarm.Core.UnitTests.Localization;

/// <summary>
/// Checks how <see cref="JsonAdminStringLocalizerFactory"/> loads admin translation sets and how its localizers
/// resolve keys for the current UI culture.
/// </summary>
[TestFixture]
public class JsonAdminStringLocalizerFactoryTests
{
    private const string SetName = "Admin";

    private TempDirectoryScope _scope = default!;
    private CultureInfo _originalCulture = default!;
    private CultureInfo _originalUICulture = default!;

    private string Root => _scope.GetPath("AdminLanguages");

    [SetUp]
    public void SetUp()
    {
        _originalCulture = CultureInfo.CurrentCulture;
        _originalUICulture = CultureInfo.CurrentUICulture;
        _scope = new TempDirectoryScope();
    }

    [TearDown]
    public void TearDown()
    {
        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUICulture;
        _scope.Dispose();
    }

    [Test]
    public void Indexer_KeyInCurrentUICulture_ReturnsThatValue()
    {
        // Arrange
        WriteFile(SetName, "en-US", """{ "Common.Cancel": "Cancel" }""");
        WriteFile(SetName, "ru-RU", """{ "Common.Cancel": "Отмена" }""");
        var localizer = CreateFactory().Create(SetName, SetName);
        UseCulture("ru-RU");

        // Act
        var result = localizer["Common.Cancel"];

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value, Is.EqualTo("Отмена"));
            Assert.That(result.ResourceNotFound, Is.False);
        }
    }

    [Test]
    public void Indexer_KeyMissingInCurrentUICulture_FallsBackToEnglish()
    {
        // Arrange
        WriteFile(SetName, "en-US", """{ "Common.Cancel": "Cancel", "Common.Save": "Save" }""");
        WriteFile(SetName, "ru-RU", """{ "Common.Cancel": "Отмена" }""");
        var localizer = CreateFactory().Create(SetName, SetName);
        UseCulture("ru-RU");

        // Act
        var result = localizer["Common.Save"];

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value, Is.EqualTo("Save"));
            Assert.That(result.ResourceNotFound, Is.False);
        }
    }

    [Test]
    public void Indexer_KeyMissingInCurrentCultureAndEnglish_ReturnsKeyAsNotFound()
    {
        // Arrange
        WriteFile(SetName, "en-US", """{ "Common.Cancel": "Cancel" }""");
        WriteFile(SetName, "ru-RU", """{ "Common.Cancel": "Отмена" }""");
        var localizer = CreateFactory().Create(SetName, SetName);
        UseCulture("ru-RU");

        // Act
        var result = localizer["Common.Missing"];

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Name, Is.EqualTo("Common.Missing"));
            Assert.That(result.Value, Is.EqualTo("Common.Missing"));
            Assert.That(result.ResourceNotFound, Is.True);
        }
    }

    [Test]
    public void CreateFromType_UsesTheSetNamedAfterTheTypesAssemblyIgnoringCase()
    {
        // Arrange
        var assemblyName = typeof(JsonAdminStringLocalizerFactoryTests).Assembly.GetName().Name!;
        WriteFile(assemblyName.ToLowerInvariant(), "en-US", """{ "Greeting": "Hello from the test assembly" }""");
        WriteFile(SetName, "en-US", """{ "Greeting": "Hello from another set" }""");
        var localizer = CreateFactory().Create(typeof(JsonAdminStringLocalizerFactoryTests));
        UseCulture("en-US");

        // Act
        var result = localizer["Greeting"];

        // Assert
        Assert.That(result.Value, Is.EqualTo("Hello from the test assembly"));
    }

    [Test]
    public void CreateFromBaseNameAndLocation_UsesTheSetNamedLocation()
    {
        // Arrange
        WriteFile("First", "en-US", """{ "Greeting": "First" }""");
        WriteFile("Second", "en-US", """{ "Greeting": "Second" }""");
        var localizer = CreateFactory().Create("Any.Base.Name", "second");
        UseCulture("en-US");

        // Act
        var result = localizer["Greeting"];

        // Assert
        Assert.That(result.Value, Is.EqualTo("Second"));
    }

    [Test]
    public void Indexer_UnknownSet_ReturnsKeyAsNotFound()
    {
        // Arrange
        WriteFile(SetName, "en-US", """{ "Greeting": "Hello" }""");
        var localizer = CreateFactory().Create("Any.Base.Name", "Unknown");
        UseCulture("en-US");

        // Act
        var result = localizer["Greeting"];

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value, Is.EqualTo("Greeting"));
            Assert.That(result.ResourceNotFound, Is.True);
        }
    }

    [Test]
    public void Indexer_BotSetWithOnlyEnglish_ReturnsEnglishWhileSharedSetReturnsCurrentCulture()
    {
        // Arrange
        const string sharedSet = "BotFarm.Shared";
        const string botSet = "EnglishOnlyBot";
        WriteFile(sharedSet, "en-US", """{ "Nav.Dashboard": "Dashboard" }""");
        WriteFile(sharedSet, "uk-UA", """{ "Nav.Dashboard": "Панель керування" }""");
        WriteFile(botSet, "en-US", """{ "Page.Footer": "This is a bot" }""");
        var factory = CreateFactory();
        var sharedLocalizer = factory.Create(sharedSet, sharedSet);
        var botLocalizer = factory.Create(botSet, botSet);
        UseCulture("uk-UA");

        // Act
        var shared = sharedLocalizer["Nav.Dashboard"];
        var bot = botLocalizer["Page.Footer"];

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(shared.Value, Is.EqualTo("Панель керування"));
            Assert.That(bot.Value, Is.EqualTo("This is a bot"));
            Assert.That(bot.ResourceNotFound, Is.False);
        }
    }

    [Test]
    public void IndexerWithArguments_FormatsWithCurrentCulture()
    {
        // Arrange
        WriteFile(SetName, "en-US", """{ "Size": "Size: {0} MB" }""");
        WriteFile(SetName, "ru-RU", """{ "Size": "Размер: {0} МБ" }""");
        var localizer = CreateFactory().Create(SetName, SetName);
        UseCulture("ru-RU");

        // Act
        var result = localizer["Size", 1.5];

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value, Is.EqualTo("Размер: 1,5 МБ"));
            Assert.That(result.ResourceNotFound, Is.False);
        }
    }

    [Test]
    public void GetAllStrings_IncludingParentCultures_AddsMissingEnglishEntries()
    {
        // Arrange
        WriteFile(SetName, "en-US", """{ "Common.Cancel": "Cancel", "Common.Save": "Save" }""");
        WriteFile(SetName, "ru-RU", """{ "Common.Cancel": "Отмена" }""");
        var localizer = CreateFactory().Create(SetName, SetName);
        UseCulture("ru-RU");

        // Act
        var withParents = localizer.GetAllStrings(includeParentCultures: true)
            .ToDictionary(entry => entry.Name, entry => entry.Value);
        var withoutParents = localizer.GetAllStrings(includeParentCultures: false)
            .ToDictionary(entry => entry.Name, entry => entry.Value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(withParents, Is.EquivalentTo(new Dictionary<string, string>
            {
                ["Common.Cancel"] = "Отмена",
                ["Common.Save"] = "Save"
            }));
            Assert.That(withoutParents, Is.EquivalentTo(new Dictionary<string, string>
            {
                ["Common.Cancel"] = "Отмена"
            }));
        }
    }

    [Test]
    public void Constructor_MalformedJson_ThrowsInvalidDataExceptionNamingTheFile()
    {
        // Arrange
        var path = WriteFile(SetName, "en-US", """{ "Common.Cancel": "Cancel" """);

        // Act
        var exception = Assert.Throws<InvalidDataException>(() => CreateFactory());

        // Assert
        Assert.That(exception!.Message, Does.Contain(path));
    }

    [TestCase("""{ "Count": 5 }""")]
    [TestCase("""{ "Nested": { "Key": "Value" } }""")]
    [TestCase("""{ "Empty": null }""")]
    [TestCase("""[ "Value" ]""")]
    public void Constructor_NonStringValue_ThrowsInvalidDataExceptionNamingTheFile(string content)
    {
        // Arrange
        var path = WriteFile(SetName, "en-US", content);

        // Act
        var exception = Assert.Throws<InvalidDataException>(() => CreateFactory());

        // Assert
        Assert.That(exception!.Message, Does.Contain(path));
    }

    [Test]
    public void Constructor_MissingRoot_CreatesEmptyFactory()
    {
        // Arrange
        var factory = CreateFactory();
        UseCulture("en-US");

        // Act
        var result = factory.Create(SetName, SetName)["Greeting"];

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.GetCultures(SetName), Is.Empty);
            Assert.That(result.ResourceNotFound, Is.True);
        }
    }

    [Test]
    public void GetCulturesAndGetValue_ReadTheLoadedFilesIgnoringCase()
    {
        // Arrange
        WriteFile(SetName, "en-US", """{ "LanguageName": "English" }""");
        WriteFile(SetName, "uk-UA", """{ "LanguageName": "Українська" }""");
        var factory = CreateFactory();

        // Act
        var cultures = factory.GetCultures(SetName.ToUpperInvariant());
        var value = factory.GetValue(SetName.ToLowerInvariant(), "UK-ua", "LanguageName");
        var missing = factory.GetValue(SetName, "uk-UA", "Missing");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cultures, Is.EquivalentTo(new[] { "en-US", "uk-UA" }));
            Assert.That(value, Is.EqualTo("Українська"));
            Assert.That(missing, Is.Null);
        }
    }

    private JsonAdminStringLocalizerFactory CreateFactory() => new(NullLoggerFactory.Instance, Root);

    private string WriteFile(string set, string culture, string content)
    {
        var directory = Path.Combine(Root, set);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{culture}.json");
        File.WriteAllText(path, content);
        return path;
    }

    private static void UseCulture(string name)
    {
        var culture = CultureInfo.GetCultureInfo(name);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
