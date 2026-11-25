using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace BotFarm.Shared.Components;

/// <summary>
/// Shows the selected bot's settings without any secret and opens <see cref="BotSettingsDialog"/> to change them.
/// </summary>
public partial class DashboardBotSettings : DashboardComponentBase
{
    [Inject] protected IBotSettingsService SettingsService { get; set; } = default!;
    [Inject] protected IStringLocalizer<DashboardBotSettings> L { get; set; } = default!;

    /// <inheritdoc />
    protected override IStringLocalizer Localizer => L;

    /// <summary>
    /// The bot's settings as last read.
    /// </summary>
    protected BotSettingsView View { get; private set; } = new();

    /// <summary>
    /// Fires after the settings were saved, so the page can reload what depends on them, such as the bot's status.
    /// </summary>
    [Parameter]
    public EventCallback OnSaved { get; set; }

    protected string NotSet => L["BotSettings.NotSet"];

    /// <summary>
    /// What is wrong with the bot's settings, shown as a warning icon next to the card title, or
    /// <see langword="null"/> when the bot is configured.
    /// </summary>
    protected string? Warning => View.State switch
    {
        BotSettingsState.Configured => null,
        BotSettingsState.Unknown => L["BotSettings.State.Unknown"].Value,
        _ => L["BotSettings.State.NotConfigured"].Value
    };

    /// <summary>
    /// Red when the settings could not be read or decrypted, amber when they are only incomplete.
    /// </summary>
    protected Color WarningColor => View.State is BotSettingsState.Incomplete ? Color.Warning : Color.Error;

    protected override void OnParametersSet() => Reload();

    private void Reload() => View = SettingsService.GetView(BotName);

    /// <summary>
    /// Opens the edit dialog and reads the settings again once it closes.
    /// </summary>
    protected async Task OpenDialog()
    {
        var saved = await BotSettingsDialog.ShowAsync(DialogService, SettingsService, L, BotName);
        Reload();
        if (saved)
        {
            await OnSaved.InvokeAsync();
        }
    }
}
