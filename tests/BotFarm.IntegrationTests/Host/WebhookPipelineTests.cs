using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json.Nodes;
using BotFarm.Core.Telemetry;
using BotFarm.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace BotFarm.IntegrationTests.Host;

/// <summary>
/// Starts the app with every bot enabled and delivers Telegram updates to each bot's webhook. Every test runs once
/// for each bot the app references.
/// </summary>
/// <remarks>
/// The tests rely on the <c>/start</c> command, which the shared update service answers with the bot's welcome
/// message.
/// </remarks>
[TestFixtureSource(typeof(BotFarmAppFactory), nameof(BotFarmAppFactory.BotNames))]
public class WebhookPipelineTests(string botName)
{
    private const string StartCommand = "/start";

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
    public void Startup_RegistersTheWebhookAtThePublicUrlWithTheSecret()
    {
        var calls = _app.Telegram.Calls.Where(call => call.BotName == botName).Select(call => call.Method).ToList();
        var setWebhook = _app.Telegram.CallsTo(botName, "setWebhook").Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(calls.IndexOf("getMe"), Is.LessThan(calls.IndexOf("setWebhook")));
            Assert.That(
                setWebhook.Parameters["url"]?.GetValue<string>(),
                Is.EqualTo($"https://{BotFarmAppFactory.PublicHost}{BotFarmAppFactory.WebhookPath(botName)}"));
            Assert.That(
                setWebhook.Parameters["secret_token"]?.GetValue<string>(),
                Is.EqualTo(_app.GetWebhookSecret(botName)));
        }
    }

    [Test]
    public async Task StartCommand_RepliesWithTheBotsWelcomeMessage()
    {
        var chatId = TelegramUpdates.NewChatId();

        using var response = await _app.SendUpdateAsync(botName, StartUpdate(chatId));

        var replies = _app.Telegram.CallsTo(botName, "sendMessage", chatId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(replies, Has.Count.EqualTo(1));
            Assert.That(replies.FirstOrDefault()?.Parameters["text"]?.GetValue<string>(), Is.Not.Empty);
        }
    }

    [TestCase(null)]
    [TestCase("wrong-secret")]
    public async Task Update_WithoutTheWebhookSecret_IsRejectedAndNotProcessed(string? secret)
    {
        var chatId = TelegramUpdates.NewChatId();

        using var response = await _app.SendUpdateAsync(botName, StartUpdate(chatId), secret);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(_app.Telegram.CallsTo(botName, "sendMessage", chatId), Is.Empty);
        }
    }

    [Test]
    public async Task UpdateDeliveredTwice_IsProcessedOnce()
    {
        var chatId = TelegramUpdates.NewChatId();
        var update = StartUpdate(chatId);
        (await _app.SendUpdateAsync(botName, update)).Dispose();

        using var redelivery = await _app.SendUpdateAsync(botName, update);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(redelivery.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(_app.Telegram.CallsTo(botName, "sendMessage", chatId), Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task GroupCommand_IsAnsweredOnlyWhenAddressedToTheBot()
    {
        var chatId = -TelegramUpdates.NewChatId();
        var chat = TelegramUpdates.GroupChat(chatId);

        using var toOtherBot = await _app.SendUpdateAsync(
            botName,
            TelegramUpdates.Message(chat).WithCommand($"{StartCommand}@other_bot").AsUpdate());
        var repliesToOtherBotsCommand = _app.Telegram.CallsTo(botName, "sendMessage", chatId).Count;
        using var toThisBot = await _app.SendUpdateAsync(
            botName,
            TelegramUpdates.Message(chat).WithCommand($"{StartCommand}@{FakeTelegramApi.UsernameOf(botName)}").AsUpdate());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(toOtherBot.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(toThisBot.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(repliesToOtherBotsCommand, Is.Zero);
            Assert.That(_app.Telegram.CallsTo(botName, "sendMessage", chatId), Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task ProcessedUpdates_AreCountedByKind()
    {
        using var updates = new MetricCollector<long>(
            _app.Services.GetRequiredService<IMeterFactory>(), UpdateMetrics.MeterName, "botfarm.updates");
        var chat = TelegramUpdates.PrivateChat(TelegramUpdates.NewChatId());
        var command = StartUpdate(chat["id"]!.GetValue<long>());
        JsonObject[] deliveries =
        [
            command,
            StartUpdate(chat["id"]!.GetValue<long>()),
            TelegramUpdates.CallbackQuery(TelegramUpdates.Message(chat), "integration-test"),
            TelegramUpdates.Message(chat).WithText("hello").AsUpdate(),
            // A redelivery of an update already accepted is skipped and not counted again.
            command
        ];
        foreach (var delivery in deliveries)
        {
            using var response = await _app.SendUpdateAsync(botName, delivery);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        var measurements = updates.GetMeasurementSnapshot();
        var byKind = measurements
            .GroupBy(m => (string)m.Tags["kind"]!)
            .ToDictionary(group => group.Key, group => group.Sum(m => m.Value));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurements.Select(m => m.Tags["bot"]), Is.All.EqualTo(botName));
            Assert.That(byKind, Is.EquivalentTo(new Dictionary<string, long>
            {
                ["command"] = 2,
                ["callback_query"] = 1,
                ["other"] = 1
            }));
        }
    }

    [Test]
    public async Task WrongSecretAndRepeatedUpdate_AreCountedAsRejectedAndDuplicate()
    {
        using var outcomes = new MetricCollector<long>(
            _app.Services.GetRequiredService<IMeterFactory>(), UpdateMetrics.MeterName, "botfarm.update.outcomes");
        var update = StartUpdate(TelegramUpdates.NewChatId());

        using var rejected = await _app.SendUpdateAsync(botName, update, "wrong-secret");
        using var accepted = await _app.SendUpdateAsync(botName, update);
        using var duplicate = await _app.SendUpdateAsync(botName, update);

        var measurements = outcomes.GetMeasurementSnapshot();
        var byOutcome = measurements
            .GroupBy(m => (string)m.Tags["outcome"]!)
            .ToDictionary(group => group.Key, group => group.Sum(m => m.Value));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(accepted.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(duplicate.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(measurements.Select(m => m.Tags["bot"]), Is.All.EqualTo(botName));
            Assert.That(byOutcome, Is.EquivalentTo(new Dictionary<string, long>
            {
                ["rejected"] = 1,
                ["duplicate"] = 1
            }));
        }
    }

    private static JsonObject StartUpdate(long chatId)
    {
        return TelegramUpdates.Message(TelegramUpdates.PrivateChat(chatId)).WithCommand(StartCommand).AsUpdate();
    }
}
