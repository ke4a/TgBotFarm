using BotFarm.Core.Abstractions;
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

    [Inject] private IEnumerable<IBotModule> BotModules { get; set; } = default!;

    [Inject] private IJSRuntime JsRuntime { get; set; } = default!;

    [CascadingParameter] private bool? DarkModePreference { get; set; }

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
    private MudThemeProvider _mudThemeProvider = default!;

    protected string LayoutClass => _isDarkMode ? "app-layout dark-theme" : "app-layout light-theme";

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