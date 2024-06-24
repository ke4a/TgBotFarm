using BotFarm.Core.Services.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BotFarm.Core.Services
{
    public class JsonLocalizationService : ILocalizationService
    {
        private readonly Dictionary<string, Dictionary<string, string>> translations = new();

        public JsonLocalizationService(IWebHostEnvironment hostingEnvironment)
        {
            foreach (var file in hostingEnvironment.ContentRootFileProvider.GetDirectoryContents("Languages"))
            {
                using (var reader = new StreamReader(file.PhysicalPath))
                {
                    var json = JObject.Parse(reader.ReadToEnd()).ToString();
                    var translation = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                    translations.Add(Path.GetFileNameWithoutExtension(file.Name), translation);
                }
            }
        }

        public IEnumerable<string> GetAvailableLanguages()
        {
            return translations.Keys;
        }

        public string? GetLocalizedString(string key, string languageKey)
        {
            return translations[languageKey]?[key];
        }
    }
}
