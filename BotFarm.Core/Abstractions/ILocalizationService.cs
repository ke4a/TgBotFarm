namespace BotFarm.Core.Abstractions;

/// <summary>
/// Resolves localized strings and language availability for each bot.
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// Returns the localized value for <paramref name="key"/> in <paramref name="language"/>.
    /// </summary>
    string GetLocalizedString(string botName, string key, string language);

    /// <summary>
    /// Lists the language codes configured for the named bot.
    /// </summary>
    IEnumerable<string> GetAvailableLanguages(string botName);
}
