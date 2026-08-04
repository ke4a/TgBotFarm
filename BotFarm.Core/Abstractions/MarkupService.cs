using BotFarm.Core.Models;
using Telegram.Bot.Types.ReplyMarkups;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Base class for bot-specific reply markup builders.
/// </summary>
public abstract class MarkupService : IMarkupService
{
    protected readonly ILocalizationService LocalizationService;
    protected readonly BotIdentity Identity;

    public string Name => Identity.Name;

    protected MarkupService(BotIdentity identity, ILocalizationService localizationService)
    {
        Identity = identity;
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
