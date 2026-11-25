using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using MudBlazor;

namespace BotFarm.Shared;

/// <summary>
/// Main dashboard layout, including navigation, the language picker and persisted dark-mode preference handling.
/// </summary>
public partial class MainLayout : LayoutComponentBase
{
    [Inject] private IOptionsMonitor<BotConfig> Options { get; set; } = default!;

    [Inject] private IEnumerable<IBotModule> BotModules { get; set; } = default!;

    [Inject] private IJSRuntime JsRuntime { get; set; } = default!;

    [Inject] private IStringLocalizer<MainLayout> L { get; set; } = default!;

    [CascadingParameter] private bool? DarkModePreference { get; set; }

    /// <summary>
    /// The browser's time zone as stored in its cookie, or <see langword="null"/> before the browser has reported it.
    /// </summary>
    [CascadingParameter(Name = "TimeZoneId")] private string? TimeZoneId { get; set; }

    /// <summary>
    /// Dashboard theme: neutral surfaces, a single violet accent, Inter typography and rounded corners.
    /// </summary>
    protected static readonly MudTheme Theme = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#6d5df5",
            Secondary = "#e5487d",
            Info = "#3b82f6",
            Success = "#16a34a",
            Warning = "#d97706",
            Error = "#dc2626",
            Background = "#f5f6f8",
            Surface = "#ffffff",
            AppbarBackground = "rgba(255,255,255,0.8)",
            AppbarText = "#1f2330",
            DrawerBackground = "#ffffff",
            DrawerText = "#3b4050",
            DrawerIcon = "#6b7080",
            TextPrimary = "#1f2330",
            TextSecondary = "#6b7080",
            ActionDefault = "#6b7080",
            LinesDefault = "#e6e8ee",
            TableLines = "#eef0f4",
            TableHover = "#f5f6fa",
            Divider = "#e6e8ee",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#8b7ff8",
            Secondary = "#f06292",
            Info = "#60a5fa",
            Success = "#4ade80",
            Warning = "#fbbf24",
            Error = "#f87171",
            Background = "#0f1115",
            Surface = "#171a21",
            AppbarBackground = "rgba(15,17,21,0.75)",
            AppbarText = "#e6e8ee",
            DrawerBackground = "#13151b",
            DrawerText = "#c4c8d4",
            DrawerIcon = "#8a90a0",
            TextPrimary = "#e6e8ee",
            TextSecondary = "#8a90a0",
            ActionDefault = "#a0a6b4",
            LinesDefault = "#262a34",
            LinesInputs = "#353a46",
            TableLines = "#22262f",
            TableHover = "#1d2029",
            Divider = "#262a34",
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = ["Inter", "system-ui", "-apple-system", "Segoe UI", "Roboto", "sans-serif"],
            },
            H6 = new H6Typography { FontWeight = "600", FontSize = "1.05rem", LetterSpacing = "-0.01em" },
            Button = new ButtonTypography { FontWeight = "500", TextTransform = "none" },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "10px",
            DrawerWidthLeft = "232px",
        },
    };

    private bool _drawerOpen = true;
    private bool _isDarkMode;
    private DisplayTimeZone _displayTimeZone = DisplayTimeZone.ServerLocal;
    private MudThemeProvider _mudThemeProvider = default!;

    protected string LayoutClass => _isDarkMode ? "app-layout dark-theme" : "app-layout light-theme";

    protected string DarkLightModeButtonIcon =>
        _isDarkMode ? Icons.Material.Filled.LightMode : Icons.Material.Outlined.DarkMode;

    protected Color DarkLightModeButtonColor => _isDarkMode ? Color.Warning : Color.Inherit;

    protected override void OnInitialized()
    {
        _displayTimeZone = DisplayTimeZone.FromId(TimeZoneId);
        if (DarkModePreference.HasValue)
        {
            _isDarkMode = DarkModePreference.Value;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        var changed = await UpdateTimeZoneFromBrowserAsync();
        if (!DarkModePreference.HasValue)
        {
            await InitializeDarkModeFromSystemAsync();
            changed = true;
        }

        if (changed)
        {
            StateHasChanged();
        }
    }

    /// <summary>
    /// Reads the browser's time zone, which also refreshes its cookie, and switches the shown times to it.
    /// </summary>
    /// <returns><see langword="true"/> when the shown time zone changed.</returns>
    protected async Task<bool> UpdateTimeZoneFromBrowserAsync()
    {
        string? timeZoneId;
        try
        {
            timeZoneId = await JsRuntime.InvokeAsync<string?>("getBrowserTimeZone");
        }
        catch (JSException)
        {
            // Without the browser's time zone, times stay in the zone already shown.
            return false;
        }

        var browserTimeZone = DisplayTimeZone.FromId(timeZoneId);
        if (browserTimeZone.Zone.Id == _displayTimeZone.Zone.Id)
        {
            return false;
        }

        _displayTimeZone = browserTimeZone;
        return true;
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