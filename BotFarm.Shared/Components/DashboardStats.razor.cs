using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using MudBlazor;
using System.Globalization;

namespace BotFarm.Shared.Components;

/// <summary>
/// Displays aggregate database and bot statistics for the selected bot.
/// </summary>
public partial class DashboardStats : DashboardComponentBase
{
    private bool _loadingStats;
    private long? _chatsCount;
    private MongoDatabaseStats? _dbStats;
    private Dictionary<string, string> _additionalStats = [];
    private IReadOnlyList<ChatLanguageCount> _languageCounts = [];
    private IReadOnlyList<LanguageRow> _languageRows = [];

    /// <summary>
    /// The most languages listed by name. When a bot has more, the smallest are folded into one "other" row so
    /// the list never grows past this many rows plus one.
    /// </summary>
    internal const int MaxNamedLanguages = 5;

    protected IMongoDbDatabaseService _databaseService = default!;

    [Inject] protected IBotRegistry BotRegistry { get; set; } = default!;
    [Inject] protected ILogger<DashboardStats> Logger { get; set; } = default!;
    [Inject] protected IStringLocalizer<DashboardStats> L { get; set; } = default!;

    /// <inheritdoc />
    protected override IStringLocalizer Localizer => L;

    protected override async Task OnInitializedAsync()
    {
        _databaseService = BotRegistry.GetService<IMongoDbDatabaseService>(BotName);
        await LoadStats();
    }

    /// <summary>
    /// Loads the statistics shown on the card. The language breakdown and the database statistics come from a
    /// short-lived cache unless <paramref name="refresh"/> is true; the chat count and additional stats are always read.
    /// </summary>
    /// <param name="refresh">When true, reads the cached statistics from the database again.</param>
    protected async Task LoadStats(bool refresh = false)
    {
        _loadingStats = true;
        try
        {
            var chatsTask = _databaseService.GetChatCount();
            var dbStatsTask = _databaseService.GetDatabaseStats(refresh);
            var languageCountsTask = _databaseService.GetChatCountsByLanguage(refresh);
            var additionalStatsTask = LoadAdditionalStats();

            await Task.WhenAll(chatsTask, dbStatsTask, languageCountsTask, additionalStatsTask);

            _chatsCount = chatsTask.Result;
            _dbStats = dbStatsTask.Result;
            _languageCounts = languageCountsTask.Result;
            _languageRows = BuildLanguageRows(_languageCounts);
            _additionalStats = additionalStatsTask.Result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load stats");
            Snackbar.Add(L["Stats.Error.Load", ex.Message], Severity.Error);
        }
        finally
        {
            _loadingStats = false;
        }
    }

    /// <summary>
    /// Reloads every statistic from the database, bypassing the cache. Bound to the card's refresh button.
    /// </summary>
    protected Task RefreshStats() => LoadStats(refresh: true);

    /// <summary>
    /// Turns the per-language counts, largest first, into the rows shown on the card: up to
    /// <see cref="MaxNamedLanguages"/> named rows, then one row for all other languages when there are more than
    /// one of them. Each row's share is of all chats counted.
    /// </summary>
    private List<LanguageRow> BuildLanguageRows(IReadOnlyList<ChatLanguageCount> counts)
    {
        var total = counts.Sum(item => item.Count);
        if (total <= 0)
        {
            return [];
        }

        var named = counts.Count <= MaxNamedLanguages + 1 ? counts : counts.Take(MaxNamedLanguages).ToList();
        var rows = named
            .Select(item => CreateRow(item.Language ?? L["Stats.LanguageNotSet"], item.Count, total))
            .ToList();

        var others = counts.Skip(named.Count).ToList();
        if (others.Count > 0)
        {
            rows.Add(CreateRow(L["Stats.OtherLanguages", others.Count], others.Sum(item => item.Count), total));
        }

        return rows;
    }

    private static LanguageRow CreateRow(string name, long count, long total)
    {
        var percent = count * 100d / total;
        var percentText = percent is > 0 and < 1
            ? "<1%"
            : $"{Math.Round(percent, MidpointRounding.AwayFromZero).ToString(CultureInfo.CurrentCulture)}%";
        return new LanguageRow(name, count, percent, percentText);
    }

    /// <summary>
    /// One row of the language breakdown: a language (or the other languages together), its chat count, and that
    /// count as a percentage of all chats.
    /// </summary>
    private sealed record LanguageRow(string Name, long Count, double Percent, string PercentText);

    /// <summary>
    /// Allows derived components to contribute extra stat values beside the shared metrics.
    /// </summary>
    protected virtual async Task<Dictionary<string, string>> LoadAdditionalStats()
    {
        return await Task.FromResult(new Dictionary<string, string>());
    }
}
