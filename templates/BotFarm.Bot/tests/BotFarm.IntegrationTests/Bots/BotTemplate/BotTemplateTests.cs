using System.Net;
using BotFarm.Core.Abstractions;
using BotFarm.IntegrationTests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using BotTemplate.Models;
using BotTemplate.Services;
using BotTemplateConstants = BotTemplate.Constants;
using CoreConstants = BotFarm.Core.Constants;

namespace BotFarm.IntegrationTests.Bots.BotTemplate;

/// <summary>
/// BotTemplate's integration tests: its database code against a real MongoDB server, and its updates delivered
/// through the running app.
/// </summary>
/// <remarks>
/// The platform tests under <c>Host/</c> already run for BotTemplate. Add tests here for what BotTemplate does on its
/// own, such as its commands and the queries of its database service.
/// </remarks>
[TestFixture]
public class BotTemplateTests
{
    private const string BotName = BotTemplateConstants.Name;

    private BotFarmAppFactory _app = null!;

    [OneTimeSetUp]
    public async Task StartApp()
    {
        _app = await BotFarmAppFactory.CreateAsync();
    }

    [OneTimeTearDown]
    public async Task StopApp()
    {
        await _app.DisposeAsync();
    }

    [Test]
    public async Task StartCommand_RepliesWithTheWelcomeFromTheBotsTranslations()
    {
        var chatId = TelegramUpdates.NewChatId();

        using var response = await _app.SendUpdateAsync(
            BotName,
            TelegramUpdates.Message(TelegramUpdates.PrivateChat(chatId)).WithCommand("/start").AsUpdate());

        var replies = _app.Telegram.CallsTo(BotName, "sendMessage", chatId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            // The "Welcome" text of Languages/BotTemplate/en-US.json.
            Assert.That(
                replies.SingleOrDefault()?.Parameters["text"]?.GetValue<string>(),
                Is.EqualTo("Hi! I'm BotTemplate."));
        }
    }

    [Test]
    public async Task DatabaseService_GivesANewChatTheDefaultLanguage()
    {
        await using var bot = BotDatabaseScope.Create((identity, cache) => new BotTemplateDatabaseService(
            identity,
            MongoServer.Client,
            NullLogger<BotTemplateDatabaseService>.Instance,
            Substitute.For<INotificationService>(),
            cache));

        var language = await bot.Service.GetChatLanguage<BotTemplateChatSettings>(-100);

        Assert.That(language, Is.EqualTo(CoreConstants.DefaultLanguage));
    }
}
