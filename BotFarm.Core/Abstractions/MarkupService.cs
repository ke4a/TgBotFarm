using Telegram.Bot.Types.ReplyMarkups;

namespace BotFarm.Core.Abstractions;

public abstract class MarkupService : IMarkupService
{
    protected readonly ILocalizationService LocalizationService;

    public abstract string Name { get; }

    protected MarkupService(ILocalizationService localizationService)
    {
        LocalizationService = localizationService;
    }

    public InlineKeyboardMarkup GenerateChangeLanguageMarkup(string botName)
    {
        var keyboard = new InlineKeyboardMarkup();
        var languages = LocalizationService.GetAvailableLanguages(botName).ToList();

        for (int i = 0; i < languages.Count; i++)
        {
            if (i % 2 == 0)
            {
                keyboard = keyboard.AddNewRow();
            }

            keyboard = keyboard.AddButton(
                                    LocalizationService.GetLocalizedString(botName, "Language", languages[i]),
                                    $"{Constants.Callbacks.LanguageSet}:{languages[i]}");
        }

        return keyboard;
    }
}
