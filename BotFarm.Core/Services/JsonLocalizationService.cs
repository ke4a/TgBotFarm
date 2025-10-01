using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using Microsoft.AspNetCore.Hosting;
using System.Text.Json;

namespace BotFarm.Core.Services;

public class JsonLocalizationService : ILocalizationService
{
    private readonly List<Translation> Translations = [];

    public JsonLocalizationService(IWebHostEnvironment hostingEnvironment)
    {
        var provider = hostingEnvironment.ContentRootFileProvider;
        foreach (var dir in provider.GetDirectoryContents("Languages").Where(i => i.IsDirectory))
        {
            var translation = new Translation
            {
                BotName = dir.Name,
                Languages = []
            };

            foreach (var file in Directory.GetFiles(dir.PhysicalPath, "*.json"))
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

    public IEnumerable<string> GetAvailableLanguages(string botName)
    {
        return Translations.First(t => t.BotName.Equals(botName, StringComparison.InvariantCultureIgnoreCase))
                           .Languages
                           .Select(l => l.Locale);
    }

    public string GetLocalizedString(string botName, string key, string language)
    {
        return Translations.FirstOrDefault(t => t.BotName.Equals(botName, StringComparison.InvariantCultureIgnoreCase))?
                           .Languages.FirstOrDefault(l => l.Locale.Equals(language, StringComparison.InvariantCultureIgnoreCase))?
                           .Mapping[key] ?? string.Empty;
    }
}
