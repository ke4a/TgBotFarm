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
    private const string DarkModeStorageKey = "darkModePreference";

    [Inject] private IOptionsMonitor<BotConfig> Options { get; set; } = default!;

    [Inject] private IEnumerable<BotIdentity> Identities { get; set; } = default!;

    [Inject] private IJSRuntime JsRuntime { get; set; } = default!;

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

    protected string DarkLightModeButtonIcon => _isDarkMode ? Icons.Material.Filled.LightMode : Icons.Material.Outlined.DarkMode;
    protected Color DarkLightModeButtonColor => _isDarkMode ? Color.Warning : Color.Inherit;

    private bool _hasStoredPreference;

    protected override async Task OnInitializedAsync()
    {
        var storedValue = await JsRuntime.InvokeAsync<string?>("localStorage.getItem", DarkModeStorageKey);
        if (bool.TryParse(storedValue, out var darkModePreference))
        {
            _isDarkMode = darkModePreference;
            _hasStoredPreference = true;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _hasStoredPreference)
        {
            return;
        }

        _isDarkMode = await _mudThemeProvider.GetSystemDarkModeAsync();
        await JsRuntime.InvokeVoidAsync("localStorage.setItem", DarkModeStorageKey, _isDarkMode.ToString());
        StateHasChanged();
    }

    protected async Task ToggleDarkMode()
    {
        _isDarkMode = !_isDarkMode;
        await JsRuntime.InvokeVoidAsync("localStorage.setItem", DarkModeStorageKey, _isDarkMode.ToString());
    }
}
