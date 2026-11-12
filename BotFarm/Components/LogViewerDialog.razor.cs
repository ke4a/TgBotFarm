using BotFarm.Logging;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace BotFarm.Components;

/// <summary>
/// Shows a log file with a level filter and per-level highlighting. For a log file that is still being
/// written to, it also follows new entries as they are appended (live tail).
/// </summary>
public partial class LogViewerDialog : IAsyncDisposable
{
    private const string ModulePath = "./Components/LogViewerDialog.razor.js";

    /// <summary>
    /// How often a live log file is checked for new entries.
    /// </summary>
    internal static TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The newest matching entries rendered at most; older ones are left out to keep the page responsive.
    /// </summary>
    internal static int MaxRenderedEntries { get; set; } = 5000;

    private LogFileTail? _tail;
    private List<LogEntry> _visible = [];
    private int _matchingCount;
    private int _hiddenOlder;
    private LogLevelFilter _filter = LogLevelFilter.All;
    private bool _canTail;
    private bool _tailing;
    private string? _error;
    private bool _loading = true;
    private ElementReference _scrollContainer;
    private IJSObjectReference? _module;
    private CancellationTokenSource? _pollCts;

    /// <summary>
    /// Full path of the log file to show.
    /// </summary>
    [Parameter, EditorRequired] public string FilePath { get; set; } = string.Empty;

    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
    [Inject] private ILogger<LogViewerDialog> Logger { get; set; } = default!;
    [Inject] private IStringLocalizer<LogViewerDialog> L { get; set; } = default!;

    private string CountText => _loading || _tail is null
        ? string.Empty
        : _filter == LogLevelFilter.All
            ? L["Logs.Count.All", _tail.Entries.Count]
            : L["Logs.Count.Filtered", _matchingCount, _tail.Entries.Count];

    protected override async Task OnInitializedAsync()
    {
        _canTail = LogFileTail.IsLiveLogFile(FilePath);
        _tail = new LogFileTail(FilePath);

        try
        {
            await _tail.ReadAsync(includePartialLine: !_canTail);
            RebuildVisible();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.LogError(ex, "Failed to read log file {LogFile}.", FilePath);
            _error = L["Logs.Error.Read", ex.Message];
            _canTail = false;
            return;
        }
        finally
        {
            _loading = false;
        }

        if (_canTail)
        {
            StartTailing();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        try
        {
            _module = await JSRuntime.InvokeAsync<IJSObjectReference>("import", ModulePath);
            // The first render can happen while the file is still being read, before tailing starts,
            // so follow whenever the file can be tailed.
            await _module.InvokeVoidAsync("attach", _scrollContainer, _canTail);
        }
        catch (JSDisconnectedException)
        {
        }
    }

    private void SetFilter(LogLevelFilter filter)
    {
        _filter = filter;
        RebuildVisible();
    }

    private async Task ToggleTail()
    {
        if (_tailing)
        {
            StopTailing();
            return;
        }

        StartTailing();
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("follow", _scrollContainer);
        }
    }

    private void StartTailing()
    {
        _tailing = true;
        _pollCts = new CancellationTokenSource();
        _ = PollAsync(_pollCts.Token);
    }

    private void StopTailing()
    {
        _tailing = false;
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await InvokeAsync(() => ReadNewEntriesAsync(cancellationToken));
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ReadNewEntriesAsync(CancellationToken cancellationToken)
    {
        if (_tail is null || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var result = await _tail.ReadAsync(cancellationToken: cancellationToken);
            if (result.Changed)
            {
                RebuildVisible();
                StateHasChanged();
            }
        }
        catch (FileNotFoundException)
        {
            // The logger is between moving the old file away and creating the new one; try again next tick.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.LogWarning(ex, "Stopped following log file {LogFile}.", FilePath);
            _error = L["Logs.Error.Follow", ex.Message];
            StopTailing();
            _canTail = false;
            StateHasChanged();
        }
    }

    private void RebuildVisible()
    {
        var matching = _tail is null ? [] : _tail.Entries.Where(entry => entry.Matches(_filter)).ToList();
        _matchingCount = matching.Count;
        _hiddenOlder = Math.Max(0, matching.Count - MaxRenderedEntries);
        _visible = _hiddenOlder > 0 ? matching.GetRange(_hiddenOlder, MaxRenderedEntries) : matching;
    }

    private static string LevelClass(LogEntryLevel level) => $"log-entry--{level.ToString().ToLowerInvariant()}";

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        StopTailing();

        if (_module is null)
        {
            return;
        }

        try
        {
            await _module.InvokeVoidAsync("detach", _scrollContainer);
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }
    }
}
