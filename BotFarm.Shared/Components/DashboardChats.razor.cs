using BotFarm.Core.Abstractions;
using BotFarm.Core.Helpers;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace BotFarm.Shared.Components;

/// <summary>
/// Lists known chats for the selected bot, supports sending ad-hoc messages to them, and deletes the data of
/// single chats or of every chat the bot can no longer reach.
/// </summary>
public partial class DashboardChats : DashboardComponentBase
{
    [Parameter]
    public string Title { get; set; } = "Chats";

    private const int MaxConcurrentChatFetches = 5;

    /// <summary>
    /// Longest chat name shown in the table; longer names are cut with an ellipsis and shown in full in a tooltip.
    /// </summary>
    protected const int MaxNameLength = 45;
    private readonly int[] _pageSizeOptions = [10, 25, 50, 100];
    private readonly Dictionary<long, string> _chatNames = new();
    private Task<List<long>> _chatIdsTask = Task.FromResult(new List<long>());
    private MudTable<ChatRow>? _table;
    private bool _loadingChatIds;
    private bool _working;
    private int _checkDone;
    private int _checkTotal;
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
        await ReloadChats(false);
    }

    private async Task ReloadChats(bool noToast)
    {
        await LoadChatIds(noToast);
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

    /// <summary>
    /// Asks for confirmation, then deletes the chat and all data the bot stores for it.
    /// </summary>
    protected async Task DeleteChat(ChatRow chat)
    {
        var label = chat.Available ? $"'{chat.Name}' ({chat.Id})" : chat.Id.ToString();
        var confirmed = await DialogService.ShowMessageBoxAsync(
            "Delete chat data",
            $"Delete chat {label} from {BotName} together with all data the bot stores for it? This cannot be undone.",
            yesText: "Delete",
            noText: "Cancel",
            options: new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Small });

        if (confirmed != true)
        {
            return;
        }

        _working = true;
        try
        {
            await _databaseService.DeleteChat(chat.Id);
            _chatNames.Remove(chat.Id);
            Snackbar.Add($"Chat {chat.Id} deleted", Severity.Success);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to delete chat '{ChatId}'", chat.Id);
            Snackbar.Add($"Failed to delete chat {chat.Id}: {ex.Message}", Severity.Error);
        }
        finally
        {
            _working = false;
        }

        await ReloadChats(true);
    }

    /// <summary>
    /// Checks every known chat with Telegram, then, after confirmation, deletes the chats the bot can no longer
    /// reach (see <see cref="ChatReachability.IsUnreachable"/>) together with all their data. Chats whose check
    /// failed for another reason are kept.
    /// </summary>
    protected async Task CleanUpUnreachableChats()
    {
        _working = true;
        try
        {
            List<long> chatIds;
            try
            {
                chatIds = (await _databaseService.GetAllChatIds()).ToList();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to load chats");
                Snackbar.Add($"Failed to load chats: {ex.Message}", Severity.Error);
                return;
            }

            var (unreachable, failed) = await FindUnreachableChats(chatIds);
            var failedNote = failed > 0 ? $" {failed} chat(s) could not be checked and were kept." : string.Empty;

            if (unreachable.Count == 0)
            {
                Snackbar.Add($"No unreachable chats found.{failedNote}", failed > 0 ? Severity.Warning : Severity.Info);
                return;
            }

            var confirmed = await DialogService.ShowMessageBoxAsync(
                "Clean up unreachable chats",
                $"{unreachable.Count} of {chatIds.Count} chat(s) are unreachable: the bot was removed or blocked, or the chat no longer exists. " +
                $"Delete them from {BotName} together with all data the bot stores for them? This cannot be undone.{failedNote}",
                yesText: "Delete",
                noText: "Cancel",
                options: new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Small });

            if (confirmed != true)
            {
                return;
            }

            var deleted = 0;
            foreach (var chatId in unreachable)
            {
                try
                {
                    await _databaseService.DeleteChat(chatId);
                    _chatNames.Remove(chatId);
                    deleted++;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failed to delete chat '{ChatId}'", chatId);
                }
            }

            if (deleted == unreachable.Count)
            {
                Snackbar.Add($"Deleted {deleted} unreachable chat(s)", Severity.Success);
            }
            else
            {
                Snackbar.Add($"Deleted {deleted} of {unreachable.Count} unreachable chat(s); see logs for failures", Severity.Warning);
            }
        }
        finally
        {
            _working = false;
            _checkDone = 0;
            _checkTotal = 0;
        }

        await ReloadChats(true);
    }

    /// <summary>
    /// Requests every chat from Telegram, a few at a time, and returns the chats the bot can no longer reach and
    /// the number of chats whose check failed for another reason.
    /// </summary>
    private async Task<(List<long> Unreachable, int Failed)> FindUnreachableChats(List<long> chatIds)
    {
        var unreachable = new System.Collections.Concurrent.ConcurrentBag<long>();
        var failed = 0;
        _checkDone = 0;
        _checkTotal = chatIds.Count;
        await ReportCheckProgress();

        var options = new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentChatFetches };
        await Parallel.ForEachAsync(chatIds, options, async (id, ct) =>
        {
            try
            {
                await _botService.Client.GetChat(id, ct);
            }
            catch (Exception ex) when (ChatReachability.IsUnreachable(ex))
            {
                unreachable.Add(id);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failed);
                Logger.LogWarning(ex, "Could not check chat '{ChatId}'", id);
            }

            var done = Interlocked.Increment(ref _checkDone);
            if (done % 10 == 0 || done == chatIds.Count)
            {
                await ReportCheckProgress();
            }
        });

        return (unreachable.Order().ToList(), failed);
    }

    /// <summary>
    /// Re-renders the component so the chat check progress is shown.
    /// </summary>
    protected virtual Task ReportCheckProgress()
    {
        return InvokeAsync(StateHasChanged);
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
