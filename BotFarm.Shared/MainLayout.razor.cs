using BotFarm.Core.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using MudBlazor;

namespace BotFarm.Shared;

/// <summary>
/// Main dashboard layout, including navigation and persisted dark-mode preference handling.
/// </summary>
public partial class MainLayout : LayoutComponentBase
{
    [Inject] private IOptionsMonitor<BotConfig> Options { get; set; } = default!;

    [Inject] private IEnumerable<BotIdentity> Identities { get; set; } = default!;

    [Inject] private IJSRuntime JsRuntime { get; set; } = default!;

    [CascadingParameter] private bool? DarkModePreference { get; set; }

    private bool _drawerOpen = true;
    private bool _isDarkMode;
    private MudThemeProvider _mudThemeProvider = default!;

    protected string LayoutClass
    {
        get
        {
            var classes = new List<string>
            {
                _isDarkMode ? "dark-theme" : "light-theme",
                "d-inherit"
            };

            return string.Join(" ", classes);
        }
    }

    protected string DarkLightModeButtonIcon =>
        _isDarkMode ? Icons.Material.Filled.LightMode : Icons.Material.Outlined.DarkMode;

    protected Color DarkLightModeButtonColor => _isDarkMode ? Color.Warning : Color.Inherit;

    protected override void OnInitialized()
    {
        if (DarkModePreference.HasValue)
        {
            _isDarkMode = DarkModePreference.Value;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || DarkModePreference.HasValue)
        {
            return;
        }

        await InitializeDarkModeFromSystemAsync();
        StateHasChanged();
    }

    protected async Task InitializeDarkModeFromSystemAsync()
    {
        _isDarkMode = await _mudThemeProvider.GetSystemDarkModeAsync();
        await PersistDarkModePreferenceAsync();
    }

    protected async Task ToggleDarkMode()
    {
        _isDarkMode = !_isDarkMode;
        await PersistDarkModePreferenceAsync();
    }

    private async Task PersistDarkModePreferenceAsync()
    {
        var value = _isDarkMode.ToString().ToLowerInvariant();
        await JsRuntime.InvokeVoidAsync("setDarkModePreferenceCookie", value);
    }
}