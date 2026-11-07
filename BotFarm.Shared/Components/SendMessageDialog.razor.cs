using BotFarm.Core.Abstractions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;

namespace BotFarm.Shared.Components;

/// <summary>
/// Dialog with a rich-text editor for composing and sending a Telegram HTML message to one chat.
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
    [Parameter, EditorRequired]
    public long ChatId { get; set; }

    /// <summary>
    /// The chat name shown in the dialog title.
    /// </summary>
    [Parameter]
    public string ChatName { get; set; } = string.Empty;

    [Inject] protected ILogger<SendMessageDialog> Logger { get; set; } = default!;
    [Inject] protected INotificationService NotificationService { get; set; } = default!;

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
            Snackbar.Add("Failed to load message editor", Severity.Error);
        }
    }

    /// <summary>
    /// Sends the editor content to the chat and closes the dialog on success.
    /// Ignored while a previous send is still in flight.
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
                Snackbar.Add("Message is empty", Severity.Warning);
                return;
            }

            await NotificationService.SendMessage(ChatId, BotName, message);
            Snackbar.Add("Message sent", Severity.Success);
            MudDialog.Close(DialogResult.Ok(true));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to send message");
            Snackbar.Add($"Failed to send message: {ex.Message}", Severity.Error);
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
