namespace BotFarm.Core.Abstractions;

public interface ILocalizationService
{
    string GetLocalizedString(string botName, string key, string language);

    IEnumerable<string> GetAvailableLanguages(string botName);
}
