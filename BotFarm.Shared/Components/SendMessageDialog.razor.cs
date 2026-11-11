using BotFarm.Core.Abstractions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;

namespace BotFarm.Shared.Components;

/// <summary>
/// Dialog with a rich-text editor for composing a Telegram HTML message. By default the message is sent to one
/// chat; with <see cref="SubmitHandler"/> set, the message is handed to the caller instead.
/// </summary>
public partial class SendMessageDialog : DashboardComponentBase, IAsyncDisposable
{
    private const string ModulePath = "./_content/BotFarm.Shared/Components/SendMessageDialog.razor.js";

    private ElementReference _editor;
    private ElementReference _toolbar;
    private IJSObjectReference? _module;
    private bool _sending;

    [CascadingParameter]
    protected IMudDialogInstance MudDialog { get; set; } = default!;

    /// <summary>
    /// The ID of the chat the message is sent to.
    /// </summary>
    [Parameter]
    public long ChatId { get; set; }

    /// <summary>
    /// The chat name shown in the dialog title.
    /// </summary>
    [Parameter]
    public string ChatName { get; set; } = string.Empty;

    /// <summary>
    /// The dialog title. Defaults to naming <see cref="ChatName"/>.
    /// </summary>
    [Parameter]
    public string? Title { get; set; }

    /// <summary>
    /// The text of the send button. Defaults to the localized "Send".
    /// </summary>
    [Parameter]
    public string? SendText { get; set; }

    /// <summary>
    /// When set, the composed message is passed to this handler instead of being sent to <see cref="ChatId"/>.
    /// The dialog closes with the message as its result when the handler returns <see langword="true"/>, and
    /// stays open with the message kept otherwise.
    /// </summary>
    [Parameter]
    public Func<string, Task<bool>>? SubmitHandler { get; set; }

    [Inject] protected ILogger<SendMessageDialog> Logger { get; set; } = default!;
    [Inject] protected INotificationService NotificationService { get; set; } = default!;
    [Inject] protected IStringLocalizer<SendMessageDialog> L { get; set; } = default!;

    /// <inheritdoc />
    protected override IStringLocalizer Localizer => L;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        try
        {
            _module = await JSRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);
            await _module.InvokeVoidAsync("initialize", _editor, _toolbar);
        }
        catch (JSException ex)
        {
            Logger.LogError(ex, "Failed to initialize message editor");
            Snackbar.Add(L["SendMessage.EditorFailed"], Severity.Error);
        }
    }

    /// <summary>
    /// Sends the editor content to the chat, or passes it to <see cref="SubmitHandler"/>, and closes the dialog on
    /// success. Ignored while a previous send is still in flight.
    /// </summary>
    protected async Task Send()
    {
        if (_sending || _module == null)
        {
            return;
        }

        _sending = true;
        try
        {
            var message = await _module.InvokeAsync<string?>("getContent", _editor);
            if (string.IsNullOrWhiteSpace(message))
            {
                Snackbar.Add(L["SendMessage.Empty"], Severity.Warning);
                return;
            }

            if (SubmitHandler != null)
            {
                if (await SubmitHandler(message))
                {
                    MudDialog.Close(DialogResult.Ok(message));
                }

                return;
            }

            await NotificationService.SendMessage(ChatId, BotName, message);
            Snackbar.Add(L["SendMessage.Sent"], Severity.Success);
            MudDialog.Close(DialogResult.Ok(true));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to send message");
            Snackbar.Add(L["SendMessage.Error", ex.Message], Severity.Error);
        }
        finally
        {
            _sending = false;
        }
    }

    private void Cancel() => MudDialog.Cancel();

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        if (_module == null)
        {
            return;
        }

        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The circuit is gone, so the module was already released with it.
        }
    }
}
