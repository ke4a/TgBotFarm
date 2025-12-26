namespace BotFarm.Core;

public class Constants
{
    public const string DefaultLanguage = "en-US";

    public struct Commands
    {
        public const string ChangeLanguage = "/changelanguage";
    }

    public struct Callbacks
    {
        public const string LanguageSet = "language-set";
    }
}
