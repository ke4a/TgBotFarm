using System.Reflection;
using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.TestKit;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;

namespace BotFarm.Shared.UnitTests.Components;

[TestFixture]
public class MainLayoutTests : AdminUiTestFixture
{
    private TestableMainLayout _component = null!;
    private RecordingJsRuntime _jsRuntime = null!;

    private sealed record JsInvocation(string Identifier, object?[]? Arguments);

    private sealed class RecordingJsRuntime : IJSRuntime
    {
        public List<JsInvocation> Calls { get; } = [];
        private static bool SystemDarkMode => true;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            Calls.Add(new JsInvocation(identifier, args));
            var result = typeof(TValue) == typeof(bool)
                ? (TValue)(object)SystemDarkMode
                : default!;
            return ValueTask.FromResult(result);
        }
    }

    private class TestableMainLayout : MainLayout
    {
        private static readonly PropertyInfo DarkModePreferenceProperty =
            typeof(MainLayout).GetProperty(
                "DarkModePreference",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static readonly InstanceFieldAccessor<MudThemeProvider> MudThemeProviderField =
            ReflectionTestHelper.CreateInstanceFieldAccessor<MainLayout, MudThemeProvider>("_mudThemeProvider");

        public string CurrentLayoutClass => LayoutClass;

        public void SetDarkModePreference(bool? value)
            => DarkModePreferenceProperty.SetValue(this, value);

        public void SetThemeProvider(MudThemeProvider provider)
            => MudThemeProviderField.Set(this, provider);

        public void SetJsRuntime(IJSRuntime jsRuntime)
        {
            var jsRuntimeProperty = typeof(MainLayout).GetProperty(
                "JsRuntime",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            jsRuntimeProperty.SetValue(this, jsRuntime);
        }

        public void InvokeOnInitialized() => OnInitialized();

        public Task InvokeOnAfterRenderAsync(bool firstRender) => OnAfterRenderAsync(firstRender);

        public Task InvokeInitializeDarkModeFromSystemAsync() => InitializeDarkModeFromSystemAsync();

        public Task InvokeToggleDarkMode() => ToggleDarkMode();
    }

    [SetUp]
    public void SetUp()
    {
        _jsRuntime = new RecordingJsRuntime();
        _component = new TestableMainLayout();
        _component.SetJsRuntime(_jsRuntime);
    }

    [TestCase(true, "dark-theme")]
    [TestCase(false, "light-theme")]
    public void OnInitialized_WithCookiePreference_UsesStoredTheme(bool preference, string expectedTheme)
    {
        _component.SetDarkModePreference(preference);

        _component.InvokeOnInitialized();

        Assert.That(_component.CurrentLayoutClass, Does.Contain(expectedTheme));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task OnAfterRenderAsync_WithCookiePreference_DoesNotReadBrowserOrPersist(bool preference)
    {
        _component.SetDarkModePreference(preference);

        await _component.InvokeOnAfterRenderAsync(firstRender: true);

        Assert.That(_jsRuntime.Calls, Is.Empty);
    }

    [Test]
    public async Task OnAfterRenderAsync_WhenNotFirstRender_DoesNothing()
    {
        await _component.InvokeOnAfterRenderAsync(firstRender: false);

        Assert.That(_jsRuntime.Calls, Is.Empty);
    }

    [Test]
    public async Task InitializeDarkModeFromSystemAsync_PersistsDetectedPreference()
    {
        var themeProvider = new MudThemeProvider();
        SetJavaScriptRuntime(themeProvider, _jsRuntime);
        _component.SetThemeProvider(themeProvider);

        await _component.InvokeInitializeDarkModeFromSystemAsync();

        Assert.That(_component.CurrentLayoutClass, Does.Contain("dark-theme"));
        AssertPreferencePersisted("true");
    }

    [TestCase(true, "false")]
    [TestCase(false, "true")]
    public async Task ToggleDarkMode_PersistsUpdatedPreference(bool preference, string expectedValue)
    {
        _component.SetDarkModePreference(preference);
        _component.InvokeOnInitialized();

        await _component.InvokeToggleDarkMode();

        AssertPreferencePersisted(expectedValue);
    }

    [Test]
    public async Task Render_InRussian_ShowsRussianNavigationAndAppBarLabels()
    {
        // Arrange
        using var _ = new CultureScope("ru-RU");
        await using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddAdminLocalizationForTests();
        context.Services.AddMudServices();
        context.Services.AddLogging();
        context.Services.AddSingleton(Substitute.For<AntiforgeryStateProvider>());
        var botOptions = Substitute.For<IOptionsMonitor<BotConfig>>();
        botOptions.Get(Arg.Any<string>()).Returns(new BotConfig());
        context.Services.AddSingleton(botOptions);
        var botModule = Substitute.For<IBotModule>();
        botModule.Name.Returns("TestBot");
        context.Services.AddSingleton(botModule);

        // Act
        var cut = context.Render<MainLayout>(parameters => parameters
            .Add(layout => layout.Body, (RenderFragment)(builder => builder.AddContent(0, "body"))));

        // Assert
        var navLabels = cut.FindAll(".mud-nav-link-text").Select(link => link.TextContent.Trim()).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(navLabels, Is.SupersetOf(new[] { "Главная", "Панель управления", "Состояние" }));
            Assert.That(cut.Find(".app-nav-label").TextContent.Trim(), Is.EqualTo("Боты"));
            Assert.That(cut.FindAll("[aria-label='Показать или скрыть меню навигации']"), Is.Not.Empty);
            Assert.That(cut.FindAll("[aria-label='Включить тёмную тему']"), Is.Not.Empty);
            Assert.That(cut.Find("button[type=submit]").GetAttribute("aria-label"), Is.EqualTo("Выйти"));
            Assert.That(cut.Find("button[type=submit]").GetAttribute("title"), Is.EqualTo("Выйти"));
            Assert.That(cut.FindAll("[aria-label='Язык интерфейса']"), Is.Not.Empty);
        }
    }

    private void AssertPreferencePersisted(string expectedValue)
    {
        var call = _jsRuntime.Calls.SingleOrDefault(candidate =>
            candidate.Identifier == "setDarkModePreferenceCookie");

        Assert.That(call, Is.Not.Null);
        Assert.That(call!.Arguments, Is.EqualTo(new object?[] { expectedValue }));
    }

    private static void SetJavaScriptRuntime(object target, IJSRuntime jsRuntime)
    {
        for (var type = target.GetType(); type is not null; type = type.BaseType)
        {
            var property = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                                              BindingFlags.DeclaredOnly)
                .FirstOrDefault(candidate =>
                    candidate.PropertyType == typeof(IJSRuntime) && candidate.SetMethod is not null);
            if (property is not null)
            {
                property.SetValue(target, jsRuntime);
                return;
            }

            var field = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.DeclaredOnly)
                .FirstOrDefault(candidate => candidate.FieldType == typeof(IJSRuntime));
            if (field is null)
            {
                continue;
            }

            field.SetValue(target, jsRuntime);
            return;
        }

        throw new InvalidOperationException($"No {nameof(IJSRuntime)} member found on {target.GetType().FullName}.");
    }
}