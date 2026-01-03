using BotFarm.Core.Abstractions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace BotFarm.Shared.Components;

public partial class DashboardChats : DashboardComponentBase
{
    [Parameter]
    public string Title { get; set; } = "Chats";

    private readonly List<ChatFullInfo> _chats = [];
    private bool _loadingChats;
    private string _modalChatTitle = "Send message";
    private IDatabaseService _databaseService = default!;
    private IBotService _botService = default!;

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
                await ShowToastAsync($"Loaded {_chats.Count} chats", true);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load chats");
            await ShowToastAsync($"Failed to load chats: {ex.Message}", false);
        }
        finally
        {
            _loadingChats = false;
        }
    }

    protected async Task SendMessageAsync(ChatFullInfo chat)
    {
        var chatName = GetChatName(chat);
        _modalChatTitle = $"Send message to '{chatName}'";

        var message = await JSRuntime.InvokeAsync<string?>("openQuillModal", _modalChatTitle);
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        try
        {
            await NotificationService.SendMessage(chat.Id, BotName, message);
            await ShowToastAsync("Message sent", true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to send message");
            await ShowToastAsync($"Failed to send message: {ex.Message}", false);
        }
    }

    private string GetChatName(ChatFullInfo chat)
    {
        return !string.IsNullOrWhiteSpace(chat.Title)
            ? chat.Title
            : $"{chat.FirstName} {chat.LastName}".Trim();
    }
}
