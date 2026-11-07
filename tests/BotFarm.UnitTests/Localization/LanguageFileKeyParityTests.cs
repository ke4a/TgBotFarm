using System.Text.Json;

namespace BotFarm.UnitTests.Localization;

/// <summary>
/// Checks that every bot's language files define the same keys with non-empty values.
/// </summary>
[TestFixture]
public class LanguageFileKeyParityTests
{
    private static readonly string LanguagesPath = Path.Combine(AppContext.BaseDirectory, "Languages");

    private static IEnumerable<TestCaseData> BotLanguageDirectories()
    {
        if (!Directory.Exists(LanguagesPath))
        {
            yield break;
        }

        foreach (var directory in Directory.EnumerateDirectories(LanguagesPath).Order())
        {
            yield return new TestCaseData(directory).SetName($"LanguageFiles_{Path.GetFileName(directory)}");
        }
    }

    [Test]
    public void LanguagesFolder_ContainsAtLeastOneBotDirectory()
    {
        // Act
        var directories = BotLanguageDirectories().ToList();

        // Assert
        Assert.That(directories, Is.Not.Empty, $"No bot language directories found in '{LanguagesPath}'.");
    }

    [TestCaseSource(nameof(BotLanguageDirectories))]
    public void LanguageFiles_AllLocalesHaveSameKeysAndNonEmptyValues(string botDirectory)
    {
        // Arrange
        var locales = Directory.GetFiles(botDirectory, "*.json")
            .Order()
            .ToDictionary(path => Path.GetFileNameWithoutExtension(path)!, ReadMapping);
        var allKeys = locales.Values.SelectMany(mapping => mapping.Keys).ToHashSet();

        // Act
        var missingKeys = locales
            .Select(locale => (Locale: locale.Key, Missing: allKeys.Except(locale.Value.Keys).Order().ToList()))
            .Where(result => result.Missing.Count > 0)
            .Select(result => $"{result.Locale}: {string.Join(", ", result.Missing)}")
            .ToList();
        var emptyValues = locales
            .SelectMany(locale => locale.Value
                .Where(entry => string.IsNullOrWhiteSpace(entry.Value))
                .Select(entry => $"{locale.Key}: {entry.Key}"))
            .ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(locales, Is.Not.Empty, $"No language files found in '{botDirectory}'.");
            Assert.That(missingKeys, Is.Empty, "Locales are missing keys:" + Environment.NewLine + string.Join(Environment.NewLine, missingKeys));
            Assert.That(emptyValues, Is.Empty, "Empty values:" + Environment.NewLine + string.Join(Environment.NewLine, emptyValues));
        }
    }

    private static Dictionary<string, string> ReadMapping(string file)
    {
        // StreamReader detects and skips a UTF-8 BOM.
        using var reader = new StreamReader(file);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd())
            ?? throw new InvalidDataException($"'{file}' does not contain a JSON object.");
    }
}
