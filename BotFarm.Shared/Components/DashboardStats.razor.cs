using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using MudBlazor;

namespace BotFarm.Shared.Components;

public partial class DashboardStats : DashboardComponentBase
{
    private bool _loadingStats;
    private int? _chatsCount;
    private MongoDatabaseStats? _dbStats;
    private Dictionary<string, string> _additionalStats { get; set; } = [];

    protected IMongoDbDatabaseService _databaseService = default!;

    [Inject] protected IEnumerable<IMongoDbDatabaseService> DatabaseServices { get; set; } = default!;
    [Inject] protected ILogger<DashboardStats> Logger { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        _databaseService = DatabaseServices.First(s => s.Name.Equals(BotName, StringComparison.OrdinalIgnoreCase));
        await LoadStatsAsync();
    }

    protected async Task LoadStatsAsync()
    {
        _loadingStats = true;
        try
        {
            var chatsTask = _databaseService.GetAllChatIds();
            var dbStatsTask = _databaseService.GetDatabaseStats();
            var additionalStatsTask = LoadAdditionalStatsAsync();

            await Task.WhenAll(chatsTask, dbStatsTask, additionalStatsTask);

            _chatsCount = chatsTask.Result.Count();
            _dbStats = dbStatsTask.Result;
            _additionalStats = additionalStatsTask.Result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load stats");
            Snackbar.Add($"Failed to load stats: {ex.Message}", Severity.Error);
        }
        finally
        {
            _loadingStats = false;
        }
    }

    protected virtual async Task<Dictionary<string, string>> LoadAdditionalStatsAsync()
    {
        return await Task.FromResult(new Dictionary<string, string>());
    }
}
