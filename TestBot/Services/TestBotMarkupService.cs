using BotFarm.Core.Abstractions;
using TestBot.Abstractions;
using Telegram.Bot.Types.ReplyMarkups;

namespace TestBot.Services;

public class TestBotMarkupService : MarkupService, ITestBotMarkupService
{
    public override string Name => Constants.Name;

    public TestBotMarkupService(ILocalizationService localizationService)
        : base(localizationService)
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
