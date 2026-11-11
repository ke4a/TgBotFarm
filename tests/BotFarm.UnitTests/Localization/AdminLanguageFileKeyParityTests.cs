using System.Text.Json;

namespace BotFarm.UnitTests.Localization;

/// <summary>
/// Checks that the admin UI translation sets shipped with the host define the same keys in every language,
/// cover the shared languages, and name each shared language.
/// </summary>
[TestFixture]
public class AdminLanguageFileKeyParityTests
{
    private const string HostSet = "BotFarm";
    private const string SharedSet = "BotFarm.Shared";
    private const string DefaultCulture = "en-US";
    private const string LanguageNameKey = "LanguageName";

    private static readonly string AdminLanguagesPath = Path.Combine(AppContext.BaseDirectory, "AdminLanguages");

    private static IEnumerable<TestCaseData> SetDirectories()
    {
        if (!Directory.Exists(AdminLanguagesPath))
        {
            yield break;
        }

        foreach (var directory in Directory.EnumerateDirectories(AdminLanguagesPath).Order())
        {
            yield return new TestCaseData(directory).SetName($"AdminLanguageFiles_{Path.GetFileName(directory)}");
        }
    }

    [Test]
    public void AdminLanguagesFolder_ContainsHostAndSharedSets()
    {
        // Act
        var sets = SetNames();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.Exists(AdminLanguagesPath), Is.True, $"'{AdminLanguagesPath}' does not exist.");
            Assert.That(sets, Does.Contain(HostSet));
            Assert.That(sets, Does.Contain(SharedSet));
        }
    }

    [Test]
    public void EverySet_HasEnglishFile()
    {
        // Act
        var setsWithoutEnglish = SetNames()
            .Where(set => !Cultures(set).Contains(DefaultCulture, StringComparer.OrdinalIgnoreCase))
            .ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(SetNames(), Is.Not.Empty, $"No admin translation sets found in '{AdminLanguagesPath}'.");
            Assert.That(setsWithoutEnglish, Is.Empty, $"Sets without {DefaultCulture}.json: {string.Join(", ", setsWithoutEnglish)}");
        }
    }

    [TestCaseSource(nameof(SetDirectories))]
    public void SetFiles_AllLanguagesHaveSameKeysAndNonEmptyValues(string setDirectory)
    {
        // Arrange
        var languages = Directory.GetFiles(setDirectory, "*.json")
            .Order()
            .ToDictionary(path => Path.GetFileNameWithoutExtension(path)!, ReadMapping);
        var allKeys = languages.Values.SelectMany(mapping => mapping.Keys).ToHashSet();

        // Act
        var missingKeys = languages
            .Select(language => (Culture: language.Key, Missing: allKeys.Except(language.Value.Keys).Order().ToList()))
            .Where(result => result.Missing.Count > 0)
            .Select(result => $"{result.Culture}: {string.Join(", ", result.Missing)}")
            .ToList();
        var emptyValues = languages
            .SelectMany(language => language.Value
                .Where(entry => string.IsNullOrWhiteSpace(entry.Value))
                .Select(entry => $"{language.Key}: {entry.Key}"))
            .ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(languages, Is.Not.Empty, $"No translation files found in '{setDirectory}'.");
            Assert.That(missingKeys, Is.Empty, "Languages are missing keys:" + Environment.NewLine + string.Join(Environment.NewLine, missingKeys));
            Assert.That(emptyValues, Is.Empty, "Empty values:" + Environment.NewLine + string.Join(Environment.NewLine, emptyValues));
        }
    }

    [Test]
    public void HostSet_HasExactlyTheSharedLanguages()
    {
        // Act
        var hostCultures = Cultures(HostSet);
        var sharedCultures = Cultures(SharedSet);

        // Assert
        Assert.That(hostCultures, Is.EquivalentTo(sharedCultures));
    }

    [Test]
    public void OtherSets_UseOnlySharedLanguages()
    {
        // Arrange
        var sharedCultures = Cultures(SharedSet).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Act
        var unsupported = SetNames()
            .Where(set => set != HostSet && set != SharedSet)
            .SelectMany(set => Cultures(set)
                .Where(culture => !sharedCultures.Contains(culture))
                .Select(culture => $"{set}: {culture}"))
            .ToList();

        // Assert
        Assert.That(unsupported, Is.Empty, "Languages the shared admin UI does not support:" + Environment.NewLine + string.Join(Environment.NewLine, unsupported));
    }

    [Test]
    public void SharedSetFiles_HaveLanguageName()
    {
        // Act
        var withoutName = Directory.GetFiles(Path.Combine(AdminLanguagesPath, SharedSet), "*.json")
            .Order()
            .Where(path => !ReadMapping(path).ContainsKey(LanguageNameKey))
            .Select(Path.GetFileName)
            .ToList();

        // Assert
        Assert.That(withoutName, Is.Empty, $"Files without {LanguageNameKey}: {string.Join(", ", withoutName)}");
    }

    private static List<string> SetNames() =>
        Directory.Exists(AdminLanguagesPath)
            ? Directory.EnumerateDirectories(AdminLanguagesPath).Select(Path.GetFileName).OfType<string>().Order().ToList()
            : [];

    private static List<string> Cultures(string set)
    {
        var directory = Path.Combine(AdminLanguagesPath, set);
        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order().ToList()
            : [];
    }

    private static Dictionary<string, string> ReadMapping(string file)
    {
        // StreamReader detects and skips a UTF-8 BOM.
        using var reader = new StreamReader(file);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd())
            ?? throw new InvalidDataException($"'{file}' does not contain a JSON object.");
    }
}
