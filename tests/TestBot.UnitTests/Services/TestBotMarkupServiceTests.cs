using BotFarm.Core.Abstractions;
using NSubstitute;
using TestBot.Services;

namespace TestBot.UnitTests.Services;

[TestFixture]
public class TestBotMarkupServiceTests
{
    [Test]
    public void GenerateClearChatDataMarkup_ReturnsLocalizedConfirmationButtons()
    {
        var localizationService = Substitute.For<ILocalizationService>();
        localizationService.GetLocalizedString(Constants.Name, "Yes", "en").Returns("Yes");
        localizationService.GetLocalizedString(Constants.Name, "No", "en").Returns("No");
        var service = new TestBotMarkupService(localizationService);

        var result = service.GenerateClearChatDataMarkup("en");
        var row = result.InlineKeyboard.Single().ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(row, Has.Count.EqualTo(2));
            Assert.That(row[0].Text, Is.EqualTo("Yes"));
            Assert.That(row[0].CallbackData, Is.EqualTo($"{Constants.Callbacks.ChatDataClear}:yes"));
            Assert.That(row[1].Text, Is.EqualTo("No"));
            Assert.That(row[1].CallbackData, Is.EqualTo($"{Constants.Callbacks.ChatDataClear}:no"));
        }
    }
}
