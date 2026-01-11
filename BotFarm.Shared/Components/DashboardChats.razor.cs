using BotFarm.Core.Abstractions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace BotFarm.Shared.Components;

public partial class DashboardChats : DashboardComponentBase
{
    [Parameter]
    public string Title { get; set; } = "Chats";

    private readonly List<ChatFullInfo> _chats = [];
    private bool _loadingChats;
    private bool _dialogVisible;
    private string _modalChatTitle = "Send message";
    private ChatFullInfo? _selectedChat;
    private IDatabaseService _databaseService = default!;
    private IBotService _botService = default!;

    private readonly DialogOptions _dialogOptions = new()
    {
        MaxWidth = MaxWidth.Small,
        FullWidth = true,
        CloseButton = true,
        CloseOnEscapeKey = true,
        BackdropClick = false,
    };

    [Inject] protected IEnumerable<IDatabaseService> DatabaseServices { get; set; } = default!;
    [Inject] protected IEnumerable<IBotService> BotServices { get; set; } = default!;
    [Inject] protected ILogger<DashboardChats> Logger { get; set; } = default!;
    [Inject] protected INotificationService NotificationService { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        _databaseService = DatabaseServices.First(s => s.Name.Equals(BotName, StringComparison.OrdinalIgnoreCase));
        _botService = BotServices.First(s => s.Name.Equals(BotName, StringComparison.OrdinalIgnoreCase));
        await LoadChatsAsync(true);
    }

    protected async Task LoadChatsAsync(bool noToast)
    {
        _loadingChats = true;
        try
        {
            var chatIds = (await _databaseService.GetAllChatIds()).ToList();
            _chats.Clear();
            foreach (var id in chatIds)
            {
                try
                {
                    var chat = await _botService.Client.GetChat(id);
                    _chats.Add(chat);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, $"Error while getting chat '{id}'");
                }
            }

            if (!noToast)
            {
                Snackbar.Add($"Loaded {_chats.Count} chats", Severity.Success);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load chats");
            Snackbar.Add($"Failed to load chats: {ex.Message}", Severity.Error);
        }
        finally
        {
            _loadingChats = false;
        }
    }

    protected async Task SendMessageAsync(ChatFullInfo chat)
    {
        _selectedChat = chat;
        var chatName = GetChatName(chat);
        _modalChatTitle = $"Send message to '{chatName}'";
        _dialogVisible = true;

        await Task.Delay(200);
        await JSRuntime.InvokeVoidAsync("initializeQuillEditor");
    }

    private async Task ConfirmSendMessage()
    {
        if (_selectedChat == null)
        {
            return;
        }

        var message = await JSRuntime.InvokeAsync<string?>("getQuillContent");
        if (string.IsNullOrWhiteSpace(message))
        {
            _dialogVisible = false;
            return;
        }

        try
        {
            await NotificationService.SendMessage(_selectedChat.Id, BotName, message);
            Snackbar.Add("Message sent", Severity.Success);
            _dialogVisible = false;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to send message");
            Snackbar.Add($"Failed to send message: {ex.Message}", Severity.Error);
        }
    }

    private void CancelSendMessage()
    {
        _dialogVisible = false;
        _selectedChat = null;
    }

    private string GetChatName(ChatFullInfo chat)
    {
        return !string.IsNullOrWhiteSpace(chat.Title)
            ? chat.Title
            : $"{chat.FirstName} {chat.LastName}".Trim();
    }
}
