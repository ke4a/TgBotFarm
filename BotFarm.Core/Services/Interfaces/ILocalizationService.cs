namespace BotFarm.Core.Services.Interfaces
{
    public interface ILocalizationService
    {
        string? GetLocalizedString(string key, string languageKey);

        IEnumerable<string> GetAvailableLanguages();
    }
}
