using BotFarm.Core.Abstractions;
using BotFarm.Core.Exceptions;
using BotFarm.Core.Models;
using Microsoft.AspNetCore.Components;
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
            "Drop pending updates",
            $"Discard {pendingCount} pending update(s) for {BotName}? Telegram will not deliver them, and this cannot be undone.",
            yesText: "Drop",
            noText: "Cancel",
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
            Snackbar.Add("Pending updates were dropped.", Severity.Success);
        }
        catch (BotControlBusyException)
        {
            Snackbar.Add("This bot is already being updated.", Severity.Info);
        }
        catch (Exception exception)
        {
            Logger.LogWarning(
                "Dropping pending updates failed for {BotName} ({FailureType}).",
                BotName,
                exception.GetType().Name);
            Snackbar.Add("Pending updates could not be dropped.", Severity.Error);
        }
        finally
        {
            await LoadWebhookInfo();
            _dropping = false;
        }
    }
}
