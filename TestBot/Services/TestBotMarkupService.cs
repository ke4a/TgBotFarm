using BotFarm.Core.Abstractions;
using BotFarm.Core.Base;
using BotFarm.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using TestBot.Abstractions;
using Telegram.Bot.Types.ReplyMarkups;

namespace TestBot.Services;

/// <summary>
/// Bot-specific markup builder for the reference TestBot implementation.
/// </summary>
public class TestBotMarkupService : MarkupService, ITestBotMarkupService
{
    /// <summary>
    /// Creates the markup service that reuses the shared localization infrastructure.
    /// </summary>
    public TestBotMarkupService(
        [FromKeyedServices(Constants.Name)] BotIdentity identity,
        ILocalizationService localizationService)
        : base(identity, localizationService)
    {
    }

    /// <summary>
    /// Builds the yes/no confirmation keyboard for clearing stored chat data.
    /// </summary>
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
