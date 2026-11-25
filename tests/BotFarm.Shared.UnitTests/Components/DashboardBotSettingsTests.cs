using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.Shared;
using BotFarm.Shared.Components;
using BotFarm.TestKit;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;

namespace BotFarm.Shared.UnitTests.Components;

/// <summary>
/// Renders <see cref="DashboardBotSettings"/> and checks what it shows and that it opens the edit dialog.
/// </summary>
[TestFixture]
public class DashboardBotSettingsTests : AdminUiTestFixture
{
    private const string TestBotName = "TestBot";

    private BunitContext _context = default!;
    private IBotSettingsService _settingsService = default!;

    [SetUp]
    public void SetUp()
    {
        _context = new BunitContext();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddAdminLocalizationForTests();
        _context.Services.AddMudServices();
        _context.Services.AddLogging();
        _context.AddAuthorization().SetAuthorized("admin");
        _settingsService = Substitute.For<IBotSettingsService>();
        _context.Services.AddSingleton(_settingsService);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _context.DisposeAsync();
    }

    [Test]
    public void ShowsTheStoredSettingsWithoutSecrets()
    {
        var updatedAt = new DateTime(2026, 11, 20, 9, 15, 0, DateTimeKind.Utc);
        GivenView(new BotSettingsView
        {
            BotName = TestBotName,
            State = BotSettingsState.Configured,
            HasToken = true,
            TelegramBotId = 123456,
            TelegramUsername = "test_bot",
            AdminChatId = -10042,
            Emoji = "🤖",
            HasCustomWebhookSecret = true,
            UpdatedAtUtc = updatedAt,
            UpdatedBy = "alice"
        });

        var cut = Render();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cut.FindAll(".settings-warning"), Is.Empty);
            Assert.That(cut.FindAll(".settings-state"), Is.Empty);
            Assert.That(cut.Find(".settings-bot-id").TextContent, Does.Contain("123456"));
            Assert.That(cut.Find(".settings-username").TextContent, Does.Contain("@test_bot"));
            Assert.That(cut.Find(".settings-admin-chat").TextContent, Does.Contain("-10042"));
            Assert.That(cut.Find(".settings-emoji").TextContent, Does.Contain("🤖"));
            Assert.That(cut.Find(".settings-webhook-secret").TextContent, Does.Contain("Custom"));
            Assert.That(
                cut.Find(".settings-last-change").TextContent,
                Does.Contain(updatedAt.ToLocalTime().ToString("dd MMM yyyy HH:mm")).And.Contain("alice"));
        }
    }

    [Test]
    public void WithoutUsernameOrCustomSecret_HidesTheUsernameAndShowsTheDerivedSecret()
    {
        GivenView(new BotSettingsView
        {
            BotName = TestBotName,
            State = BotSettingsState.Configured,
            HasToken = true,
            TelegramBotId = 123456,
            AdminChatId = 42
        });

        var cut = Render();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cut.FindAll(".settings-username"), Is.Empty);
            Assert.That(cut.Find(".settings-webhook-secret").TextContent, Does.Contain("Derived from the token"));
            Assert.That(cut.Find(".settings-emoji").TextContent, Does.Contain("Not set"));
            Assert.That(cut.FindAll(".settings-last-change"), Is.Empty);
        }
    }

    [TestCase(BotSettingsState.Incomplete, "Not configured", "mud-warning-text")]
    [TestCase(BotSettingsState.Unreadable, "Not configured", "mud-error-text")]
    [TestCase(BotSettingsState.Unknown, "Unknown", "mud-error-text")]
    public void SettingsThatAreNotUsable_ShowAWarningIconInTheHeader(BotSettingsState state, string expected, string color)
    {
        GivenView(new BotSettingsView { BotName = TestBotName, State = state });

        var cut = Render();

        using (Assert.EnterMultipleScope())
        {
            var warning = cut.Find(".card-header .settings-warning");
            Assert.That(warning.TextContent, Does.Contain(expected));
            Assert.That(warning.ClassList, Does.Contain(color));
            Assert.That(cut.Find(".settings-bot-id").TextContent, Does.Contain("Not set"));
            Assert.That(cut.Find(".settings-admin-chat").TextContent, Does.Contain("Not set"));
        }
    }

    [Test]
    public void Edit_OpensTheSettingsDialog()
    {
        GivenView(new BotSettingsView { BotName = TestBotName, State = BotSettingsState.Incomplete });
        var provider = _context.Render<MudDialogProvider>();
        var cut = Render();

        cut.Find("button[aria-label='Edit settings']").Click();

        provider.WaitForNUnitAssertion(() =>
        {
            Assert.That(provider.Markup, Does.Contain("Settings of TestBot"));
            Assert.That(provider.FindAll(".token-input input"), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void AfterTheDialogSaves_ReloadsTheView()
    {
        var views = new Queue<BotSettingsView>(
        [
            new BotSettingsView { BotName = TestBotName, State = BotSettingsState.Incomplete },
            new BotSettingsView { BotName = TestBotName, State = BotSettingsState.Configured, TelegramBotId = 123456 }
        ]);
        _settingsService.GetView(TestBotName).Returns(_ => views.Count > 1 ? views.Dequeue() : views.Peek());
        _settingsService.SaveAsync(TestBotName, Arg.Any<BotSettingsChange>(), Arg.Any<CancellationToken>())
            .Returns(new BotSettingsSaveResult.Saved(
                views.Last(),
                new BotRuntimeStatus(TestBotName, false, false, BotRuntimeState.Applied, DateTime.UtcNow, null),
                "test_bot"));
        var provider = _context.Render<MudDialogProvider>();
        var cut = Render();

        cut.Find("button[aria-label='Edit settings']").Click();
        provider.WaitForNUnitAssertion(() => Assert.That(provider.FindAll("button"), Is.Not.Empty));
        provider.FindAll("button").Single(button => button.TextContent.Trim() == "Save").Click();

        cut.WaitForNUnitAssertion(() => Assert.That(cut.FindAll(".settings-warning"), Is.Empty));
        Assert.That(cut.Find(".settings-bot-id").TextContent, Does.Contain("123456"));
    }

    [TestCase(new[] { "Token" }, "Token")]
    [TestCase(new[] { "Token", "WebhookSecret" }, "Token, WebhookSecret")]
    [TestCase(new[] { "WebhookSecret" }, "WebhookSecret")]
    public void UnreadableSettings_AreShownAsTokenMustBeReEnteredNamingTheSettings(string[] unreadable, string names)
    {
        GivenView(new BotSettingsView
        {
            BotName = TestBotName,
            State = BotSettingsState.Unreadable,
            HasToken = true,
            TelegramBotId = 123456,
            AdminChatId = 42,
            Emoji = "🤖",
            HasCustomWebhookSecret = true,
            UnreadableSettings = unreadable
        });

        var cut = Render();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cut.Find(".settings-warning").TextContent, Does.Contain("Not configured"));
            Assert.That(
                cut.Find(".settings-unreadable").TextContent,
                Does.Contain("Token must be re-entered").And.Contain(names));
            Assert.That(cut.Find(".settings-bot-id").TextContent, Does.Contain("123456"));
            Assert.That(cut.Find(".settings-admin-chat").TextContent, Does.Contain("42"));
            Assert.That(cut.Find(".settings-emoji").TextContent, Does.Contain("🤖"));
        }
    }

    [TestCase(BotSettingsState.Configured)]
    [TestCase(BotSettingsState.Incomplete)]
    [TestCase(BotSettingsState.Unknown)]
    public void ReadableSettings_ShowNoReEntryNotice(BotSettingsState state)
    {
        GivenView(new BotSettingsView { BotName = TestBotName, State = state });

        var cut = Render();

        Assert.That(cut.FindAll(".settings-unreadable"), Is.Empty);
    }

    [Test]
    public void Edit_ForUnreadableSettings_OpensTheDialogWithTheUnreadableFieldsRequired()
    {
        GivenView(new BotSettingsView
        {
            BotName = TestBotName,
            State = BotSettingsState.Unreadable,
            HasToken = true,
            TelegramBotId = 123456,
            AdminChatId = 42,
            HasCustomWebhookSecret = true,
            UnreadableSettings = ["Token"]
        });
        var provider = _context.Render<MudDialogProvider>();
        var cut = Render();

        cut.Find("button[aria-label='Edit settings']").Click();

        provider.WaitForNUnitAssertion(() =>
        {
            Assert.That(provider.Find(".token-input input").HasAttribute("required"), Is.True);
            Assert.That(provider.Find(".webhook-secret-input input").HasAttribute("required"), Is.False);
        });
    }

    [Test]
    public void LastChange_IsShownInTheCascadedTimeZone()
    {
        GivenView(new BotSettingsView
        {
            BotName = TestBotName,
            State = BotSettingsState.Configured,
            UpdatedAtUtc = new DateTime(2026, 11, 20, 22, 15, 0, DateTimeKind.Utc),
            UpdatedBy = "alice"
        });
        var plusTwo = TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");

        var cut = _context.Render<DashboardBotSettings>(parameters => parameters
            .Add(p => p.BotName, TestBotName)
            .AddCascadingValue(new DisplayTimeZone(plusTwo)));

        Assert.That(cut.Find(".settings-last-change").TextContent, Does.Contain("21 Nov 2026 00:15"));
    }

    private IRenderedComponent<DashboardBotSettings> Render() =>
        _context.Render<DashboardBotSettings>(parameters => parameters.Add(p => p.BotName, TestBotName));

    private void GivenView(BotSettingsView view) => _settingsService.GetView(TestBotName).Returns(view);
}
