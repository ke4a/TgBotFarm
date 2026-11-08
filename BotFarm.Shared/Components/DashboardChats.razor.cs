using BotFarm.Core.Abstractions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace BotFarm.Shared.Components;

/// <summary>
/// Lists known chats for the selected bot and supports sending ad-hoc messages to them.
/// </summary>
public partial class DashboardChats : DashboardComponentBase
{
    [Parameter]
    public string Title { get; set; } = "Chats";

    private const int MaxConcurrentChatFetches = 5;
    private readonly int[] _pageSizeOptions = [10, 25, 50, 100];
    private readonly Dictionary<long, string> _chatNames = new();
    private Task<List<long>> _chatIdsTask = Task.FromResult(new List<long>());
    private MudTable<ChatRow>? _table;
    private bool _loadingChatIds;
    private IDatabaseService _databaseService = default!;
    private IBotService _botService = default!;

    private static readonly DialogOptions SendMessageDialogOptions = new()
    {
        MaxWidth = MaxWidth.Small,
        FullWidth = true,
        CloseButton = true,
        CloseOnEscapeKey = true,
        BackdropClick = false,
    };

    [Inject] protected IBotRegistry BotRegistry { get; set; } = default!;
    [Inject] protected ILogger<DashboardChats> Logger { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        _databaseService = BotRegistry.GetService<IDatabaseService>(BotName);
        _botService = BotRegistry.GetService<IBotService>(BotName);
        await LoadChatIds(true);
    }

    /// <summary>
    /// Reloads the list of chat IDs from the database and refetches the current page, including cached chat names.
    /// </summary>
    protected async Task RefreshChats()
    {
        _chatNames.Clear();
        await LoadChatIds(false);
        if (_table != null)
        {
            await _table.ReloadServerData();
        }
    }

    /// <summary>
    /// Loads the IDs of all known chats. Chat details are fetched later, one page at a time.
    /// </summary>
    protected async Task LoadChatIds(bool noToast)
    {
        _loadingChatIds = true;
        try
        {
            _chatIdsTask = FetchChatIds(noToast);
            await _chatIdsTask;
        }
        finally
        {
            _loadingChatIds = false;
        }
    }

    private async Task<List<long>> FetchChatIds(bool noToast)
    {
        try
        {
            var chatIds = (await _databaseService.GetAllChatIds()).ToList();
            if (!noToast)
            {
                Snackbar.Add($"Found {chatIds.Count} chats", Severity.Success);
            }

            return chatIds;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load chats");
            Snackbar.Add($"Failed to load chats: {ex.Message}", Severity.Error);
            return [];
        }
    }

    /// <summary>
    /// Builds the requested table page, fetching Telegram details in parallel only for chats whose names are not cached yet.
    /// Chats that could not be fetched are shown as unavailable and retried on the next load.
    /// </summary>
    protected async Task<TableData<ChatRow>> LoadPage(TableState state, CancellationToken cancellationToken)
    {
        var chatIds = await _chatIdsTask;
        var offset = state.Page * state.PageSize;
        var pageIds = chatIds.Skip(offset).Take(state.PageSize).ToList();
        var names = new string?[pageIds.Count];
        var missing = new List<int>();

        for (var i = 0; i < pageIds.Count; i++)
        {
            if (_chatNames.TryGetValue(pageIds[i], out var name))
            {
                names[i] = name;
            }
            else
            {
                missing.Add(i);
            }
        }

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = MaxConcurrentChatFetches,
            CancellationToken = cancellationToken,
        };

        try
        {
            await Parallel.ForEachAsync(missing, options, async (i, ct) =>
            {
                var id = pageIds[i];
                try
                {
                    var chat = await _botService.Client.GetChat(id, ct);
                    names[i] = GetChatName(chat);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error while getting chat '{ChatId}'", id);
                }
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new TableData<ChatRow> { Items = [], TotalItems = chatIds.Count };
        }

        var rows = new List<ChatRow>(pageIds.Count);
        for (var i = 0; i < pageIds.Count; i++)
        {
            var number = offset + i + 1;
            if (names[i] is { } name)
            {
                _chatNames[pageIds[i]] = name;
                rows.Add(new ChatRow(number, pageIds[i], name, true));
            }
            else
            {
                rows.Add(new ChatRow(number, pageIds[i], "⚠️ Unavailable", false));
            }
        }

        return new TableData<ChatRow> { Items = rows, TotalItems = chatIds.Count };
    }

    /// <summary>
    /// Copies a table value to the user's clipboard and confirms with a snackbar.
    /// </summary>
    protected async Task CopyToClipboard(string text, string label)
    {
        try
        {
            await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", text);
            Snackbar.Add($"{label} copied", Severity.Info);
        }
        catch (JSException ex)
        {
            Logger.LogError(ex, "Failed to copy {Label} to clipboard", label);
            Snackbar.Add($"Failed to copy {label}", Severity.Error);
        }
    }

    /// <summary>
    /// Opens the dialog for composing and sending a message to the chat.
    /// </summary>
    protected Task SendMessage(ChatRow chat)
    {
        var parameters = new DialogParameters<SendMessageDialog>
        {
            { x => x.BotName, BotName },
            { x => x.ChatId, chat.Id },
            { x => x.ChatName, chat.Name },
        };

        return DialogService.ShowAsync<SendMessageDialog>(null, parameters, SendMessageDialogOptions);
    }

    private static string GetChatName(ChatFullInfo chat)
    {
        return !string.IsNullOrWhiteSpace(chat.Title)
            ? chat.Title
            : $"{chat.FirstName} {chat.LastName}".Trim();
    }

    /// <summary>
    /// A table row for one chat. <paramref name="Available"/> is false when Telegram could not return the chat.
    /// </summary>
    protected sealed record ChatRow(int Number, long Id, string Name, bool Available);
}
