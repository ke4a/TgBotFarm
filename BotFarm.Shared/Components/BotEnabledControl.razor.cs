using BotFarm.Core.Abstractions;
using BotFarm.Core.Exceptions;
using BotFarm.Core.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using MudBlazor;

namespace BotFarm.Shared.Components;

/// <summary>
/// On/off switch for one bot, with its "Applying..." state and a retryable problem message.
/// The parent owns the bot's status and reloads it whenever <see cref="OnStatusChanged"/> fires.
/// </summary>
public partial class BotEnabledControl : ComponentBase
{
    private bool _working;
    private string? _actionError;

    [Inject] protected IBotControlService BotControlService { get; set; } = default!;
    [Inject] protected ISnackbar Snackbar { get; set; } = default!;
    [Inject] protected ILogger<BotEnabledControl> Logger { get; set; } = default!;

    /// <summary>The bot's current runtime status.</summary>
    [Parameter, EditorRequired]
    public BotRuntimeStatus Status { get; set; } = default!;

    /// <summary>Shown on the left of the switch, such as the bot's name.</summary>
    [Parameter]
    public RenderFragment? Label { get; set; }

    [Parameter]
    public string? Class { get; set; }

    /// <summary>
    /// Fires once a command has started (<c>false</c>) and again when it has finished (<c>true</c>),
    /// so the parent can reload the status.
    /// </summary>
    [Parameter]
    public EventCallback<bool> OnStatusChanged { get; set; }

    protected override void OnParametersSet()
    {
        // A command error stays until the reloaded status shows the bot's state is known again.
        if (_actionError is not null && !_working && Status.Status != BotRuntimeState.Unknown)
        {
            _actionError = null;
        }
    }

    protected bool IsSwitchDisabled =>
        _working
        || Status.Status is BotRuntimeState.Unknown or BotRuntimeState.Pending or BotRuntimeState.Error
        || Status.DesiredEnabled is null;

    protected bool IsApplying =>
        Status.Status == BotRuntimeState.Pending
        || (_working && _actionError is null && Status.Status == BotRuntimeState.Applied);

    protected string? ProblemMessage
    {
        get
        {
            if (_actionError is not null)
            {
                return _actionError;
            }

            return Status.Status switch
            {
                BotRuntimeState.Unknown when Status.DesiredEnabled is not null =>
                    "The requested state is not confirmed. Click Retry to save and apply it.",
                BotRuntimeState.Unknown =>
                    "Bot state is unknown. Click Retry to load and apply its saved state.",
                BotRuntimeState.Error =>
                    "The saved state could not be applied after several attempts. Click Retry to try again.",
                _ => null
            };
        }
    }

    protected Task SetEnabled(bool enabled) =>
        RunAsync(
            () => BotControlService.SetEnabledAsync(Status.BotName, enabled),
            "The requested state could not be saved. Click Retry to save and apply it.");

    protected Task Retry() =>
        RunAsync(
            () => BotControlService.RetryAsync(Status.BotName),
            "The saved state could not be loaded. Click Retry to load and apply it.");

    private async Task RunAsync(Func<Task<BotRuntimeStatus>> action, string failureMessage)
    {
        if (_working)
        {
            return;
        }

        var botName = Status.BotName;
        _working = true;
        _actionError = null;
        try
        {
            StateHasChanged();
            var pendingAction = action();
            await OnStatusChanged.InvokeAsync(false);
            _ = await pendingAction;
        }
        catch (BotControlBusyException)
        {
            Snackbar.Add("This bot is already being updated.", Severity.Info);
        }
        catch (Exception exception)
        {
            Logger.LogWarning(
                "Bot state operation failed for {BotName} ({FailureType}).",
                botName,
                exception.GetType().Name);
            _actionError = failureMessage;
        }
        finally
        {
            _working = false;
            await OnStatusChanged.InvokeAsync(true);
            StateHasChanged();
        }
    }
}
