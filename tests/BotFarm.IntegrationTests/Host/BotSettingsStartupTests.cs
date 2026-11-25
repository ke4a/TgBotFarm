using System.Net;
using System.Text.Json.Nodes;
using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace BotFarm.IntegrationTests.Host;

/// <summary>
/// Starts the app without any stored bot settings, then configures and enables TestBot from the settings service,
/// as the dashboard does, without restarting the app.
/// </summary>
[TestFixture]
public class BotSettingsStartupTests
{
    private const string BotName = "TestBot";

    private BotFarmAppFactory _app = null!;

    [OneTimeSetUp]
    public async Task StartApp()
    {
        _app = await BotFarmAppFactory.CreateAsync(seedBotSettings: false);
    }

    [OneTimeTearDown]
    public async Task StopApp()
    {
        await _app.DisposeAsync();
    }

    [Test]
    public async Task BotWithoutSettings_IsGatedUntilConfiguredAndEnabled()
    {
        var botControl = _app.Services.GetRequiredService<IBotControlService>();
        var settings = _app.Services.GetRequiredService<IBotSettingsService>();

        var startupStatus = botControl.GetStatuses().Single(status => status.BotName == BotName);
        // Telegram sends the secret registered with the webhook; without a token the app has none to compare.
        using var gated = await _app.SendUpdateAsync(
            BotName,
            StartUpdate(TelegramUpdates.NewChatId()),
            secret: "previously-registered-secret");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(startupStatus.Status, Is.EqualTo(BotRuntimeState.NotConfigured));
            Assert.That(gated.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(settings.GetView(BotName).State, Is.EqualTo(BotSettingsState.Incomplete));
        }

        var result = await settings.SaveAsync(BotName, new BotSettingsChange
        {
            Token = _app.TokenOf(BotName),
            AdminChatId = long.Parse(BotFarmAppFactory.AdminChatId),
            ChangedBy = "integration-test"
        });

        Assert.That(result, Is.TypeOf<BotSettingsSaveResult.Saved>());
        var saved = (BotSettingsSaveResult.Saved)result;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved.View.State, Is.EqualTo(BotSettingsState.Configured));
            Assert.That(saved.VerifiedTelegramUsername, Is.EqualTo(FakeTelegramApi.UsernameOf(BotName)));
            Assert.That(saved.Status.Status, Is.EqualTo(BotRuntimeState.Applied));
            Assert.That(saved.Status.AppliedEnabled, Is.False);
            Assert.That(botControl.CanProcessUpdates(BotName), Is.False);
        }

        var enabled = await botControl.SetEnabledAsync(BotName, true);
        var chatId = TelegramUpdates.NewChatId();
        using var processed = await _app.SendUpdateAsync(BotName, StartUpdate(chatId));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(enabled.Status, Is.EqualTo(BotRuntimeState.Applied));
            Assert.That(enabled.AppliedEnabled, Is.True);
            Assert.That(processed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(_app.Telegram.CallsTo(BotName, "sendMessage", chatId), Has.Count.EqualTo(1));
            Assert.That(settings.GetView(BotName).TelegramUsername, Is.EqualTo(FakeTelegramApi.UsernameOf(BotName)));
        }
    }

    private static JsonObject StartUpdate(long chatId)
    {
        return TelegramUpdates.Message(TelegramUpdates.PrivateChat(chatId)).WithCommand("/start").AsUpdate();
    }
}
