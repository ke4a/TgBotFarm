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
    private IDatabaseService _databaseService = default!;
    private MongoDatabaseStats? _dbStats;
    private IMongoDbDatabaseService _databaseService = default!;

    [Inject] protected IEnumerable<IDatabaseService> DatabaseServices { get; set; } = default!;
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
            var chats = await _databaseService.GetAllChatIds();
            _chatsCount = chats.Count();
            var chatsTask = _databaseService.GetAllChatIds();
            var dbStatsTask = _databaseService.GetDatabaseStats();
            await Task.WhenAll(chatsTask, dbStatsTask);

            _chatsCount = chatsTask.Result.Count();
            _dbStats = dbStatsTask.Result;
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
}
