using FluentResults;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;

namespace BotFarm.Shared.Components;

public abstract class DashboardComponentBase : ComponentBase
{
    [Inject] protected IJSRuntime JSRuntime { get; set; } = default!;
    [Inject] protected ISnackbar Snackbar { get; set; } = default!;
    [Inject] protected IDialogService DialogService { get; set; } = default!;

    [Parameter, EditorRequired]
    public string BotName { get; set; } = string.Empty;

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
