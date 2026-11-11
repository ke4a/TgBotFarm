using FluentResults;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
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
    /// The localizer the derived component uses, which also supplies the shared <c>Common.*</c> texts of
    /// this base class.
    /// </summary>
    protected abstract IStringLocalizer Localizer { get; }

    /// <summary>
    /// Copies <paramref name="text"/> to the user's clipboard and confirms with a snackbar.
    /// </summary>
    /// <param name="text">The text to copy.</param>
    /// <param name="label">The operator-visible name of what is copied, shown in the snackbar.</param>
    /// <param name="logger">Logs a failed copy.</param>
    protected async Task CopyToClipboard(string text, string label, ILogger logger)
    {
        try
        {
            await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", text);
            Snackbar.Add(Localizer["Common.Copied", label], Severity.Info);
        }
        catch (JSException ex)
        {
            logger.LogError(ex, "Failed to copy {Label} to clipboard", label);
            Snackbar.Add(Localizer["Common.Error.CopyFailed", label], Severity.Error);
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
