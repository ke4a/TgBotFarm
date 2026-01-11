using BotFarm.Core.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.Extensions.Options;
using MudBlazor;

namespace BotFarm.Shared;

public partial class MainLayout : LayoutComponentBase
{
    [Inject] private IOptionsMonitor<BotConfig> Options { get; set; } = default!;

    [Inject] private IEnumerable<BotRegistration> Registrations { get; set; } = default!;

    [Inject] private ProtectedLocalStorage ProtectedLocalStorage { get; set; } = default!;

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

    protected override async Task OnInitializedAsync()
    {
        var darkModePreference = await ProtectedLocalStorage.GetAsync<bool>("darkModePreference");
        if (darkModePreference.Success)
        {
            _isDarkMode = darkModePreference.Value;
        }
        else
        {
            _isDarkMode = await _mudThemeProvider.GetSystemDarkModeAsync();
        }
    }

    protected async Task ToggleDarkMode()
    {
        _isDarkMode = !_isDarkMode;
        await ProtectedLocalStorage.SetAsync("darkModePreference", _isDarkMode);
    }
}
