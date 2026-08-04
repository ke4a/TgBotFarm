using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using System.Text.Json;

namespace BotFarm.Core.Services;

/// <summary>
/// Loads per-bot translation files from disk and serves localized strings from memory.
/// </summary>
internal sealed class JsonLocalizationService : ILocalizationService
{
    private readonly string languagesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Languages");
    private readonly List<Translation> Translations = [];

    /// <summary>
    /// Reads every bot language file during startup.
    /// </summary>
    public JsonLocalizationService()
    {
        var botDirectories = new DirectoryInfo(languagesPath).EnumerateDirectories();

        foreach (var dir in botDirectories)
        {
            var translation = new Translation
            {
                BotName = dir.Name,
                Languages = []
            };

            foreach (var file in Directory.GetFiles(dir.FullName, "*.json"))
            {
                var language = new Language
                {
                    Locale = Path.GetFileNameWithoutExtension(file)
                };
                using (var reader = new StreamReader(file))
                {
                    var json = JsonDocument.Parse(reader.ReadToEnd());
                    language.Mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                }
                translation.Languages.Add(language);
            }

            Translations.Add(translation);
        }
    }

    /// <summary>
    /// Lists the language codes available for the named bot.
    /// </summary>
    public IEnumerable<string> GetAvailableLanguages(string botName)
    {
        return Translations.First(t => t.BotName.Equals(botName, StringComparison.OrdinalIgnoreCase))
                           .Languages
                           .Select(l => l.Locale);
    }

    /// <summary>
    /// Returns the localized value for <paramref name="key"/> or an empty string when missing.
    /// </summary>
    public string GetLocalizedString(string botName, string key, string language)
    {
        return Translations.FirstOrDefault(t => t.BotName.Equals(botName, StringComparison.OrdinalIgnoreCase))?
                           .Languages.FirstOrDefault(l => l.Locale.Equals(language, StringComparison.OrdinalIgnoreCase))?
                           .Mapping[key] ?? string.Empty;
    }
}
