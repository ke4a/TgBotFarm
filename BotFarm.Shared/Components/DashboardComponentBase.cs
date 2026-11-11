using FluentResults;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;

namespace BotFarm.Shared.Components;

/// <summary>
/// Shared base class for dashboard components that operate on a selected bot.
/// </summary>
public abstract class DashboardComponentBase : ComponentBase
{
    [Inject] protected IJSRuntime JSRuntime { get; set; } = default!;
    [Inject] protected ISnackbar Snackbar { get; set; } = default!;
    [Inject] protected IDialogService DialogService { get; set; } = default!;

    [Parameter, EditorRequired]
    public string BotName { get; set; } = string.Empty;

    /// <summary>
    /// Copies <paramref name="text"/> to the user's clipboard and confirms with a snackbar.
    /// </summary>
    protected async Task CopyToClipboard(string text, string label, ILogger logger)
    {
        try
        {
            await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", text);
            Snackbar.Add($"{label} copied", Severity.Info);
        }
        catch (JSException ex)
        {
            logger.LogError(ex, "Failed to copy {Label} to clipboard", label);
            Snackbar.Add($"Failed to copy {label}", Severity.Error);
        }
    }

    protected static string GetResultMessage(ResultBase result, string successFallback, string failureFallback)
    {
        var success = result.Successes.FirstOrDefault();
        var fail = result.Errors.FirstOrDefault();

        return result.IsSuccess
            ? !string.IsNullOrWhiteSpace(success?.Message) ? success.Message : successFallback
            : !string.IsNullOrWhiteSpace(fail?.Message) ? fail.Message : failureFallback;
    }

    protected static string? GetResultMetadata(ResultBase result, string key)
    {
        var success = result.Successes.FirstOrDefault();
        if (success?.Metadata == null)
        {
            return null;
        }

        return success.Metadata.TryGetValue(key, out var value)
            ? value?.ToString()
            : null;
    }
}
