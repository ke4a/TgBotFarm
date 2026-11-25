using FluentResults;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using Telegram.Bot.Exceptions;

namespace BotFarm.Shared.Components;

/// <summary>
/// Shared base class for dashboard components that operate on a selected bot.
/// </summary>
public abstract class DashboardComponentBase : ComponentBase
{
    [Inject] protected IJSRuntime JSRuntime { get; set; } = default!;
    [Inject] protected ISnackbar Snackbar { get; set; } = default!;
    [Inject] protected IDialogService DialogService { get; set; } = default!;

    /// <summary>
    /// The time zone times are shown in, cascaded by <see cref="MainLayout"/>.
    /// </summary>
    [CascadingParameter] protected DisplayTimeZone DisplayTimeZone { get; set; } = DisplayTimeZone.ServerLocal;

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

    /// <summary>
    /// Logs a failed Telegram request as an error with the bot name, the exception and, when Telegram answered, its
    /// error code.
    /// </summary>
    /// <param name="logger">The component's logger.</param>
    /// <param name="exception">The Telegram client failure.</param>
    /// <param name="operation">What the component asked Telegram to do, for example <c>sending a message</c>.</param>
    /// <param name="chatId">The chat the request was about, when there is one.</param>
    protected void LogTelegramFailure(ILogger logger, RequestException exception, string operation, long? chatId = null)
    {
        if (chatId is { } id)
        {
            logger.LogError(
                exception,
                "Telegram request failed for bot {BotName} while {Operation} for chat '{ChatId}' (error code {ErrorCode}).",
                BotName,
                operation,
                id,
                (exception as ApiRequestException)?.ErrorCode);
            return;
        }

        logger.LogError(
            exception,
            "Telegram request failed for bot {BotName} while {Operation} (error code {ErrorCode}).",
            BotName,
            operation,
            (exception as ApiRequestException)?.ErrorCode);
    }

    /// <summary>
    /// The localized message for a failed Telegram request: the Telegram error code when Telegram answered, otherwise
    /// that Telegram could not be reached. It never contains the exception text.
    /// </summary>
    protected string GetTelegramFailureMessage(RequestException exception) =>
        exception is ApiRequestException apiException
            ? Localizer["Common.TelegramRejected", apiException.ErrorCode]
            : Localizer["Common.TelegramUnreachable"];

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
