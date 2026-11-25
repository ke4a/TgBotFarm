using System.Net;
using System.Net.Http.Headers;
using System.Text;
using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;

namespace BotFarm.IntegrationTests.Host;

/// <summary>
/// Saves a token and a webhook secret for TestBot through the settings service, runs the bot, and checks that
/// neither value reaches the health report, the exported metrics and logs, or the app's own logs.
/// </summary>
[TestFixture]
public class BotSettingsSecretExposureTests
{
    private const string BotName = "TestBot";
    private const string LogsPath = "/v1/logs";
    private const string MetricsPath = "/v1/metrics";

    [Test]
    public async Task SavedTokenAndWebhookSecret_AppearInNeitherHealthNorTelemetryNorLogs()
    {
        var webhookSecret = $"test_exposure-secret-{Guid.NewGuid():N}";
        var administrator = $"exposure-admin-{Guid.NewGuid():N}";
        await using var endpoint = await FakeOtlpEndpoint.StartAsync(HttpStatusCode.OK);
        await using var app = await BotFarmAppFactory.CreateAsync(
            new Dictionary<string, string?> { ["Telemetry:OtlpEndpoint"] = endpoint.BaseUrl },
            seedBotSettings: false);
        var token = app.TokenOf(BotName);

        var result = await app.Services.GetRequiredService<IBotSettingsService>().SaveAsync(BotName, new BotSettingsChange
        {
            Token = token,
            AdminChatId = long.Parse(BotFarmAppFactory.AdminChatId),
            WebhookSecret = webhookSecret,
            ChangedBy = administrator
        });
        Assert.That(result, Is.TypeOf<BotSettingsSaveResult.Saved>());

        var enabled = await app.Services.GetRequiredService<IBotControlService>().SetEnabledAsync(BotName, true);
        Assert.That(enabled.AppliedEnabled, Is.True);
        var update = TelegramUpdates.Message(TelegramUpdates.PrivateChat(TelegramUpdates.NewChatId()))
            .WithCommand("/start")
            .AsUpdate();
        using (var processed = await app.SendUpdateAsync(BotName, update, webhookSecret))
        {
            Assert.That(processed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        using var client = app.CreateClient();
        using var healthRequest = new HttpRequestMessage(HttpMethod.Get, "/health");
        healthRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BotFarmAppFactory.HealthApiKey);
        using var health = await client.SendAsync(healthRequest);
        var healthReport = await health.Content.ReadAsStringAsync();

        Assert.That(app.Services.GetRequiredService<MeterProvider>().ForceFlush(), Is.True);
        Assert.That(app.Services.GetRequiredService<LoggerProvider>().ForceFlush(), Is.True);
        var exports = endpoint.Requests;
        var appLogs = app.Logs.GetSnapshot();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(healthReport, Is.Not.Empty, "The health report is empty.");
            Assert.That(exports.Any(request => request.Path == MetricsPath), Is.True, "No metrics were exported.");
            Assert.That(
                exports.Any(request => request.Path == LogsPath
                                       && Contains(request.Body, Encoding.UTF8.GetBytes(administrator))),
                Is.True,
                "The audit log of the save was not exported.");
            foreach (var (name, secret) in new[] { ("token", token), ("webhook secret", webhookSecret) })
            {
                var bytes = Encoding.UTF8.GetBytes(secret);
                Assert.That(
                    healthReport.Contains(secret, StringComparison.Ordinal),
                    Is.False,
                    $"The health report contains the {name}.");
                Assert.That(
                    exports.Any(request => request.Path == MetricsPath && Contains(request.Body, bytes)),
                    Is.False,
                    $"The exported metrics contain the {name}.");
                Assert.That(
                    exports.Any(request => request.Path == LogsPath && Contains(request.Body, bytes)),
                    Is.False,
                    $"The exported logs contain the {name}.");
                Assert.That(
                    appLogs.Any(record => record.Message.Contains(secret, StringComparison.Ordinal)
                                          || record.Exception?.ToString().Contains(secret, StringComparison.Ordinal) == true),
                    Is.False,
                    $"An app log entry contains the {name}.");
            }
        }
    }

    private static bool Contains(byte[] body, byte[] value)
    {
        return body.AsSpan().IndexOf(value) >= 0;
    }
}
