using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using TestBot.Abstractions;
using Telegram.Bot.Types.ReplyMarkups;

namespace TestBot.Services;

public class TestBotMarkupService : MarkupService, ITestBotMarkupService
{
    public TestBotMarkupService(ILocalizationService localizationService)
        : base(new BotIdentity(Constants.Name), localizationService)
    {
    }

    public InlineKeyboardMarkup GenerateClearChatDataMarkup(string language)
    {
        return new InlineKeyboardMarkup()
            .AddButton(
                LocalizationService.GetLocalizedString(Name, "Yes", language),
                $"{Constants.Callbacks.ChatDataClear}:yes")
            .AddButton(
                LocalizationService.GetLocalizedString(Name, "No", language),
                $"{Constants.Callbacks.ChatDataClear}:no");
    }
}
