namespace BotFarm.Core;

/// <summary>
/// Shared command and callback constants used by bots built on BotFarm.
/// </summary>
public class Constants
{
    public const string DefaultLanguage = "en-US";

    public struct Commands
    {
        public const string Start = "/start";
        public const string ChangeLanguage = "/changelanguage";
    }

    public struct Callbacks
    {
        public const string LanguageSet = "language-set";
    }
}
