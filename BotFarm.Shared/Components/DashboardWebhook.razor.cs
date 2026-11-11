using BotFarm.Core.Abstractions;
using BotFarm.Core.Exceptions;
using BotFarm.Core.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using MudBlazor;
using Telegram.Bot.Types;

namespace BotFarm.Shared.Components;

/// <summary>
/// Shows the selected bot's Telegram webhook registration and delivery health, and lets the operator
/// discard the updates Telegram has queued for it.
/// </summary>
public partial class DashboardWebhook : DashboardComponentBase
{
    private WebhookInfo? _info;
    private BotRuntimeStatus? _status;
    private bool _loading;
    private bool _dropping;

    [Inject] protected IBotControlService BotControlService { get; set; } = default!;
    [Inject] protected ILogger<DashboardWebhook> Logger { get; set; } = default!;
    [Inject] protected IStringLocalizer<DashboardWebhook> L { get; set; } = default!;

    /// <inheritdoc />
    protected override IStringLocalizer Localizer => L;

    protected bool CanDropPendingUpdates =>
        _info is { PendingUpdateCount: > 0 }
        && !_dropping
        && !_loading
        && _status?.Status != BotRuntimeState.Pending;

    protected override Task OnInitializedAsync() => Refresh();

    protected Task Refresh()
    {
        LoadStatus();
        return LoadWebhookInfo();
    }

    private void LoadStatus()
    {
        _status = BotControlService.GetStatuses().FirstOrDefault(status => status.BotName == BotName);
    }

    /// <summary>
    /// Reloads the on/off status, and the webhook info once a command has finished, since turning the bot
    /// on or off registers or removes its webhook.
    /// </summary>
    private async Task HandleStatusChanged(bool commandFinished)
    {
        LoadStatus();
        if (commandFinished)
        {
            await LoadWebhookInfo();
        }
    }

    /// <summary>
    /// Reads the webhook info from Telegram. If it can't be read, the card shows it as unavailable.
    /// </summary>
    protected async Task LoadWebhookInfo()
    {
        _loading = true;
        try
        {
            _info = await BotControlService.GetWebhookInfoAsync(BotName);
        }
        catch (Exception exception)
        {
            Logger.LogWarning(
                "Webhook info could not be loaded for {BotName} ({FailureType}).",
                BotName,
                exception.GetType().Name);
            _info = null;
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task ConfirmDropPendingUpdates()
    {
        var pendingCount = _info?.PendingUpdateCount ?? 0;
        var confirmed = await DialogService.ShowMessageBoxAsync(
            L["Webhook.Drop.Title"],
            L["Webhook.Drop.Confirm", pendingCount, BotName],
            yesText: L["Webhook.Drop.Yes"],
            noText: L["Common.Cancel"],
            options: new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Small });

        if (confirmed == true)
        {
            await DropPendingUpdates();
        }
    }

    protected async Task DropPendingUpdates()
    {
        if (_dropping)
        {
            return;
        }

        _dropping = true;
        try
        {
            await BotControlService.DropPendingUpdatesAsync(BotName);
            Snackbar.Add(L["Webhook.Drop.Done"], Severity.Success);
        }
        catch (BotControlBusyException)
        {
            Snackbar.Add(L["Common.BotBusy"], Severity.Info);
        }
        catch (Exception exception)
        {
            Logger.LogWarning(
                "Dropping pending updates failed for {BotName} ({FailureType}).",
                BotName,
                exception.GetType().Name);
            Snackbar.Add(L["Webhook.Drop.Error"], Severity.Error);
        }
        finally
        {
            await LoadWebhookInfo();
            _dropping = false;
        }
    }
}
