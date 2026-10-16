using System.Reflection;
using BotFarm.TestKit;
using Microsoft.JSInterop;
using MudBlazor;

namespace BotFarm.Shared.UnitTests.Components;

[TestFixture]
public class MainLayoutTests
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