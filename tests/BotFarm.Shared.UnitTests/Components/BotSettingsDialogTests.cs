using AngleSharp.Dom;
using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.Shared.Components;
using BotFarm.TestKit;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;

namespace BotFarm.Shared.UnitTests.Components;

/// <summary>
/// Opens <see cref="BotSettingsDialog"/> in a dialog provider and saves through a substitute settings service.
/// </summary>
[TestFixture]
public class BotSettingsDialogTests : AdminUiTestFixture
{
    private const string TestBotName = "TestBot";
    private const string Token = "123456:TEST-TOKEN";
    private const string ModulePath = "./_content/BotFarm.Shared/Components/BotSettingsDialog.razor.js";

    private BunitContext _context = default!;
    private BunitAuthorizationContext _authorization = default!;
    private IBotSettingsService _settingsService = default!;

    /// <summary>
    /// The changes passed to <see cref="IBotSettingsService.SaveAsync"/> so far, in order.
    /// </summary>
    private List<BotSettingsChange> SavedChanges => _settingsService.ReceivedCalls()
        .Where(call => call.GetMethodInfo().Name == nameof(IBotSettingsService.SaveAsync))
        .Select(call => (BotSettingsChange)call.GetArguments()[1]!)
        .ToList();

    private static readonly BotSettingsView StoredView = new()
    {
        BotName = TestBotName,
        State = BotSettingsState.Configured,
        HasToken = true,
        TelegramBotId = 123456,
        AdminChatId = 42,
        Emoji = "🤖",
        HasCustomWebhookSecret = true
    };

    [SetUp]
    public void SetUp()
    {
        _context = new BunitContext();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddAdminLocalizationForTests();
        _context.Services.AddMudServices();
        _context.Services.AddLogging();
        _authorization = _context.AddAuthorization();
        _authorization.SetAuthorized("admin");
        _settingsService = Substitute.For<IBotSettingsService>();
        _settingsService.SaveAsync(TestBotName, Arg.Any<BotSettingsChange>(), Arg.Any<CancellationToken>())
            .Returns(Saved(null));
        _context.Services.AddSingleton(_settingsService);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _context.DisposeAsync();
    }

    [Test]
    public async Task SecretInputs_StartEmptyAsTextFieldsThatBrowsersDoNotSave()
    {
        var provider = await OpenAsync(StoredView);

        using (Assert.EnterMultipleScope())
        {
            foreach (var input in new[] { TokenInput(provider), WebhookSecretInput(provider) })
            {
                Assert.That(input.GetAttribute("type"), Is.EqualTo("text"));
                Assert.That(input.GetAttribute("autocomplete"), Is.EqualTo("off"));
                Assert.That(input.GetAttribute("spellcheck"), Is.EqualTo("false"));
                Assert.That(input.GetAttribute("autocapitalize"), Is.EqualTo("off"));
                Assert.That(input.HasAttribute("data-1p-ignore"), Is.True);
                Assert.That(input.HasAttribute("data-lpignore"), Is.True);
                Assert.That(input.HasAttribute("data-bwignore"), Is.True);
                Assert.That(input.GetAttribute("value"), Is.Null.Or.Empty);
            }

            Assert.That(provider.Find(".admin-chat-input input").GetAttribute("value"), Is.EqualTo("42"));
            Assert.That(provider.Find(".emoji-value").TextContent, Is.EqualTo("🤖"));
        }
    }

    [Test]
    public async Task Emoji_HasNoTextInput()
    {
        var provider = await OpenAsync(StoredView);

        Assert.That(provider.FindAll(".emoji-field input"), Is.Empty);
    }

    [Test]
    public async Task WithoutAnEmoji_ShowsNoneAndNoClearButton()
    {
        var provider = await OpenAsync(StoredView with { Emoji = null });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(provider.Find(".emoji-value").TextContent, Is.EqualTo("None"));
            Assert.That(provider.FindAll(".emoji-clear"), Is.Empty);
        }
    }

    [Test]
    public async Task ChooseEmoji_MountsThePickerFromTheDialogModule()
    {
        var module = _context.JSInterop.SetupModule(ModulePath);
        module.SetupVoid("mount", _ => true).SetVoidResult();
        var provider = await OpenAsync(StoredView);

        ClickButton(provider, "Choose");

        provider.WaitForNUnitAssertion(() => Assert.That(provider.FindAll(".emoji-picker-host"), Has.Count.EqualTo(1)));
        provider.WaitForNUnitAssertion(() => module.VerifyInvoke("mount"));
    }

    [Test]
    public async Task PickedEmoji_ReplacesTheEmojiAndClosesThePicker()
    {
        _context.JSInterop.SetupModule(ModulePath).SetupVoid("mount", _ => true).SetVoidResult();
        var provider = await OpenAsync(StoredView);
        ClickButton(provider, "Choose");

        await PickEmoji(provider, "🚀");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(provider.Find(".emoji-value").TextContent, Is.EqualTo("🚀"));
            Assert.That(provider.FindAll(".emoji-picker-host"), Is.Empty);
        }
    }

    [Test]
    public async Task PickerThatFailsToLoad_IsClosedWithAnError()
    {
        _context.JSInterop.SetupModule(ModulePath)
            .SetupVoid("mount", _ => true)
            .SetException(new JSException("Simulated load failure."));
        var provider = await OpenAsync(StoredView);

        ClickButton(provider, "Choose");

        provider.WaitForNUnitAssertion(() => Assert.That(provider.FindAll(".emoji-picker-host"), Is.Empty));
        Assert.That(
            _context.Services.GetRequiredService<ISnackbar>().ShownSnackbars.Select(snackbar => snackbar.Message),
            Has.Member("The emoji picker could not be loaded."));
    }

    [Test]
    public async Task Save_WithEmptySecretInputs_KeepsTheStoredValuesAndSendsTheCurrentValues()
    {
        var provider = await OpenAsync(StoredView);

        ClickButton(provider, "Save");

        provider.WaitForNUnitAssertion(() => Assert.That(SavedChanges, Has.Count.EqualTo(1)));
        var change = SavedChanges[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Token, Is.Null.Or.Empty);
            Assert.That(change.WebhookSecret, Is.Null.Or.Empty);
            Assert.That(change.UseDerivedWebhookSecret, Is.False);
            Assert.That(change.AdminChatId, Is.EqualTo(42));
            Assert.That(change.Emoji, Is.EqualTo("🤖"));
            Assert.That(change.ConfirmAccountChange, Is.False);
        }
    }

    [Test]
    public async Task Save_SendsTheEnteredValues()
    {
        var provider = await OpenAsync(StoredView);

        TokenInput(provider).Change(Token);
        provider.Find(".admin-chat-input input").Change("-10042");
        WebhookSecretInput(provider).Change("custom-secret");
        await PickEmoji(provider, "🚀");
        ClickButton(provider, "Save");

        provider.WaitForNUnitAssertion(() => Assert.That(SavedChanges, Has.Count.EqualTo(1)));
        var change = SavedChanges[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(change.Token == Token, Is.True);
            Assert.That(change.AdminChatId, Is.EqualTo(-10042));
            Assert.That(change.WebhookSecret == "custom-secret", Is.True);
            Assert.That(change.Emoji, Is.EqualTo("🚀"));
        }
    }

    [Test]
    public async Task Save_WithTheEmojiCleared_ClearsIt()
    {
        var provider = await OpenAsync(StoredView);

        ClickButton(provider, "Clear");
        ClickButton(provider, "Save");

        provider.WaitForNUnitAssertion(() => Assert.That(SavedChanges, Has.Count.EqualTo(1)));
        Assert.That(SavedChanges[0].Emoji, Is.Null.Or.Empty);
    }

    [Test]
    public async Task DerivedSecretAction_RemovesTheCustomSecret()
    {
        var provider = await OpenAsync(StoredView);

        WebhookSecretInput(provider).Change("custom-secret");
        provider.Find(".derived-secret-action input").Change(true);
        ClickButton(provider, "Save");

        provider.WaitForNUnitAssertion(() => Assert.That(SavedChanges, Has.Count.EqualTo(1)));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(SavedChanges[0].UseDerivedWebhookSecret, Is.True);
            Assert.That(SavedChanges[0].WebhookSecret, Is.Null.Or.Empty);
        }
    }

    [Test]
    public async Task DerivedSecretAction_IsHiddenWithoutACustomSecret()
    {
        var provider = await OpenAsync(StoredView with { HasCustomWebhookSecret = false });

        Assert.That(provider.FindAll(".derived-secret-action"), Is.Empty);
    }

    [Test]
    public async Task AccountChange_ShowsBothBotIdsAndResubmitsWithConfirmation()
    {
        _settingsService.SaveAsync(
                TestBotName,
                Arg.Is<BotSettingsChange>(change => !change.ConfirmAccountChange),
                Arg.Any<CancellationToken>())
            .Returns(new BotSettingsSaveResult.NeedsAccountChangeConfirmation(123456, 654321));
        var provider = await OpenAsync(StoredView);

        TokenInput(provider).Change("654321:OTHER-TEST-TOKEN");
        ClickButton(provider, "Save");

        provider.WaitForNUnitAssertion(() =>
        {
            var message = provider.Find(".account-change").TextContent;
            Assert.That(message, Does.Contain("123456").And.Contain("654321"));
        });

        ClickButton(provider, "Change account");

        provider.WaitForNUnitAssertion(() => Assert.That(SavedChanges, Has.Count.EqualTo(2)));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(SavedChanges[1].ConfirmAccountChange, Is.True);
            Assert.That(SavedChanges[1].Token == "654321:OTHER-TEST-TOKEN", Is.True);
        }
    }

    [Test]
    public async Task SaveThatVerifiedAToken_NamesTheTelegramUsername()
    {
        _settingsService.SaveAsync(TestBotName, Arg.Any<BotSettingsChange>(), Arg.Any<CancellationToken>())
            .Returns(Saved("test_bot"));
        var snackbars = _context.Render<MudSnackbarProvider>();
        var provider = await OpenAsync(StoredView);

        TokenInput(provider).Change(Token);
        ClickButton(provider, "Save");

        snackbars.WaitForNUnitAssertion(() => Assert.That(snackbars.Markup, Does.Contain("@test_bot")));
        provider.WaitForNUnitAssertion(() => Assert.That(provider.FindAll(".token-input"), Is.Empty));
    }

    [TestCase(BotRuntimeState.Error, "could not be re-applied")]
    [TestCase(BotRuntimeState.Unknown, "state is not confirmed")]
    public async Task SaveWithoutAConfirmedBotState_ShowsAWarning(BotRuntimeState state, string expected)
    {
        _settingsService.SaveAsync(TestBotName, Arg.Any<BotSettingsChange>(), Arg.Any<CancellationToken>())
            .Returns(new BotSettingsSaveResult.Saved(
                StoredView,
                new BotRuntimeStatus(TestBotName, true, false, state, DateTime.UtcNow, null),
                "test_bot"));
        var snackbars = _context.Render<MudSnackbarProvider>();
        var provider = await OpenAsync(StoredView);

        ClickButton(provider, "Save");

        snackbars.WaitForNUnitAssertion(() =>
        {
            Assert.That(snackbars.Markup, Does.Contain(expected));
            Assert.That(snackbars.FindAll(".mud-alert-filled-warning"), Is.Not.Empty);
        });
    }

    [TestCaseSource(nameof(Rejections))]
    public async Task Rejection_ShowsItsLocalizedMessage(BotSettingsRejection rejection)
    {
        _settingsService.SaveAsync(TestBotName, Arg.Any<BotSettingsChange>(), Arg.Any<CancellationToken>())
            .Returns(new BotSettingsSaveResult.Rejected([rejection]));
        var expected = AdminLocalizationTestExtensions.CreateAdminLocalizer<BotSettingsDialog>()[
            $"BotSettingsDialog.Rejection.{rejection}"];
        Assume.That(expected.ResourceNotFound, Is.False);
        var provider = await OpenAsync(StoredView);

        ClickButton(provider, "Save");

        provider.WaitForNUnitAssertion(() =>
            Assert.That(provider.Find(".settings-problems").TextContent, Does.Contain(expected.Value)));
        Assert.That(provider.FindAll(".token-input"), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Save_SendsTheCurrentUsersNameAsChangedBy()
    {
        _authorization.SetAuthorized("alice");
        var provider = await OpenAsync(StoredView);

        ClickButton(provider, "Save");

        provider.WaitForNUnitAssertion(() => Assert.That(SavedChanges, Has.Count.EqualTo(1)));
        Assert.That(SavedChanges[0].ChangedBy, Is.EqualTo("alice"));
    }

    [Test]
    public async Task CardAndReopenedDialog_AfterASave_ContainNeitherTheTokenNorTheWebhookSecret()
    {
        const string webhookSecret = "test_render-webhook-secret";
        _settingsService.GetView(TestBotName).Returns(StoredView);
        _settingsService.SaveAsync(TestBotName, Arg.Any<BotSettingsChange>(), Arg.Any<CancellationToken>())
            .Returns(Saved("test_bot"));
        var snackbars = _context.Render<MudSnackbarProvider>();
        var provider = _context.Render<MudDialogProvider>();
        var card = _context.Render<DashboardBotSettings>(parameters => parameters.Add(p => p.BotName, TestBotName));

        card.Find("button[aria-label='Edit settings']").Click();
        provider.WaitForNUnitAssertion(() => Assert.That(provider.FindAll(".token-input input"), Has.Count.EqualTo(1)));
        TokenInput(provider).Change(Token);
        WebhookSecretInput(provider).Change(webhookSecret);
        ClickButton(provider, "Save");
        provider.WaitForNUnitAssertion(() => Assert.That(provider.FindAll(".token-input"), Is.Empty));
        snackbars.WaitForNUnitAssertion(() => Assert.That(snackbars.Markup, Does.Contain("@test_bot")));

        card.Find("button[aria-label='Edit settings']").Click();
        provider.WaitForNUnitAssertion(() => Assert.That(provider.FindAll(".token-input input"), Has.Count.EqualTo(1)));

        using (Assert.EnterMultipleScope())
        {
            foreach (var (name, markup) in new[]
                     {
                         ("card", card.Markup), ("reopened dialog", provider.Markup), ("snackbars", snackbars.Markup)
                     })
            {
                Assert.That(markup.Contains(Token, StringComparison.Ordinal), Is.False, $"The {name} shows the token.");
                Assert.That(
                    markup.Contains(webhookSecret, StringComparison.Ordinal),
                    Is.False,
                    $"The {name} shows the webhook secret.");
            }
        }
    }

    [Test]
    public async Task RejectionsAndAccountChange_ShowNeitherTheTokenNorTheWebhookSecret()
    {
        const string otherToken = "654321:OTHER-TEST-TOKEN";
        const string webhookSecret = "test_render-webhook-secret";
        _settingsService.SaveAsync(
                TestBotName,
                Arg.Is<BotSettingsChange>(change => !change.ConfirmAccountChange),
                Arg.Any<CancellationToken>())
            .Returns(
                new BotSettingsSaveResult.Rejected(Enum.GetValues<BotSettingsRejection>()),
                new BotSettingsSaveResult.NeedsAccountChangeConfirmation(123456, 654321));
        var snackbars = _context.Render<MudSnackbarProvider>();
        var provider = await OpenAsync(StoredView);

        TokenInput(provider).Change(otherToken);
        WebhookSecretInput(provider).Change(webhookSecret);
        ClickButton(provider, "Save");
        provider.WaitForNUnitAssertion(() => Assert.That(provider.FindAll(".settings-problems"), Has.Count.EqualTo(1)));
        var problems = provider.Find(".settings-problems").TextContent;

        ClickButton(provider, "Save");
        provider.WaitForNUnitAssertion(() => Assert.That(provider.FindAll(".account-change"), Has.Count.EqualTo(1)));

        using (Assert.EnterMultipleScope())
        {
            foreach (var (name, markup) in new[]
                     {
                         ("rejection messages", problems),
                         ("account change confirmation", provider.Markup),
                         ("snackbars", snackbars.Markup)
                     })
            {
                Assert.That(markup.Contains(otherToken, StringComparison.Ordinal), Is.False, $"The {name} show the token.");
                Assert.That(
                    markup.Contains("OTHER-TEST-TOKEN", StringComparison.Ordinal),
                    Is.False,
                    $"The {name} show the token's secret part.");
                Assert.That(
                    markup.Contains(webhookSecret, StringComparison.Ordinal),
                    Is.False,
                    $"The {name} show the webhook secret.");
            }
        }
    }

    [Test]
    public async Task UnreadableToken_MarksTheTokenRequiredAndSaveWithoutItIsNotSent()
    {
        var provider = await OpenAsync(UnreadableView("Token"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(TokenInput(provider).HasAttribute("required"), Is.True);
            Assert.That(WebhookSecretInput(provider).HasAttribute("required"), Is.False);
            Assert.That(provider.Find(".token-input").TextContent, Does.Contain("could not be decrypted"));
        }

        ClickButton(provider, "Save");

        provider.WaitForNUnitAssertion(() =>
            Assert.That(provider.Find(".settings-problems").TextContent, Does.Contain("Token: the stored token could not be decrypted")));
        Assert.That(SavedChanges, Is.Empty);
    }

    [Test]
    public async Task UnreadableToken_SaveWithANewToken_SendsIt()
    {
        var provider = await OpenAsync(UnreadableView("Token"));

        TokenInput(provider).Change(Token);
        ClickButton(provider, "Save");

        provider.WaitForNUnitAssertion(() => Assert.That(SavedChanges, Has.Count.EqualTo(1)));
        Assert.That(SavedChanges[0].Token == Token, Is.True);
    }

    [Test]
    public async Task UnreadableWebhookSecret_MarksItRequiredAndSaveWithoutItIsNotSent()
    {
        var provider = await OpenAsync(UnreadableView("Token", "WebhookSecret"));

        Assert.That(WebhookSecretInput(provider).HasAttribute("required"), Is.True);

        TokenInput(provider).Change(Token);
        ClickButton(provider, "Save");

        provider.WaitForNUnitAssertion(() =>
            Assert.That(provider.Find(".settings-problems").TextContent, Does.Contain("Webhook secret: the stored secret could not be decrypted")));
        Assert.That(SavedChanges, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UnreadableWebhookSecret_SaveWithANewOrDerivedSecret_SendsIt(bool useDerived)
    {
        var provider = await OpenAsync(UnreadableView("WebhookSecret"));

        if (useDerived)
        {
            provider.Find(".derived-secret-action input").Change(true);
        }
        else
        {
            WebhookSecretInput(provider).Change("new-secret");
        }

        ClickButton(provider, "Save");

        provider.WaitForNUnitAssertion(() => Assert.That(SavedChanges, Has.Count.EqualTo(1)));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(SavedChanges[0].UseDerivedWebhookSecret, Is.EqualTo(useDerived));
            Assert.That(SavedChanges[0].WebhookSecret == "new-secret", Is.EqualTo(!useDerived));
        }
    }

    [Test]
    public async Task ReadableSettings_MarkNoSecretRequired()
    {
        var provider = await OpenAsync(StoredView);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(TokenInput(provider).HasAttribute("required"), Is.False);
            Assert.That(WebhookSecretInput(provider).HasAttribute("required"), Is.False);
        }
    }

    private static BotSettingsView UnreadableView(params string[] unreadable) =>
        StoredView with { State = BotSettingsState.Unreadable, UnreadableSettings = unreadable };

    private static IEnumerable<BotSettingsRejection> Rejections() => Enum.GetValues<BotSettingsRejection>();

    private async Task<IRenderedComponent<MudDialogProvider>> OpenAsync(BotSettingsView view)
    {
        var provider = _context.Render<MudDialogProvider>();
        var dialogService = _context.Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<BotSettingsDialog>
        {
            { x => x.BotName, TestBotName },
            { x => x.View, view },
        };
        await provider.InvokeAsync(() => dialogService.ShowAsync<BotSettingsDialog>("Settings", parameters));
        provider.WaitForNUnitAssertion(() => Assert.That(provider.FindAll(".token-input input"), Has.Count.EqualTo(1)));

        return provider;
    }

    private static BotSettingsSaveResult.Saved Saved(string? verifiedUsername) =>
        new(
            StoredView,
            new BotRuntimeStatus(TestBotName, false, false, BotRuntimeState.Applied, DateTime.UtcNow, null),
            verifiedUsername);

    private static IElement TokenInput(IRenderedComponent<MudDialogProvider> provider) =>
        provider.Find(".token-input input");

    private static IElement WebhookSecretInput(IRenderedComponent<MudDialogProvider> provider) =>
        provider.Find(".webhook-secret-input input");

    private static Task PickEmoji(IRenderedComponent<MudDialogProvider> provider, string emoji)
    {
        var dialog = provider.FindComponent<BotSettingsDialog>();
        return dialog.InvokeAsync(() => dialog.Instance.PickEmoji(emoji));
    }

    private static void ClickButton(IRenderedComponent<MudDialogProvider> provider, string text) =>
        provider.FindAll("button").Single(button => button.TextContent.Trim() == text).Click();
}
