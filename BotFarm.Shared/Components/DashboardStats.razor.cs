using BotFarm.Core.Abstractions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace BotFarm.Shared.Components;

public partial class DashboardStats : DashboardComponentBase
{
    [Parameter]
    public string CountKey { get; set; } = "ChatsCount";

    [Parameter]
    public string Title { get; set; } = "Bot stats";

    [Parameter]
    public string CountLabel { get; set; } = "Chats count:";

    private bool _loadingStats;
    private int? _chatsCount;
    private IDatabaseService _databaseService = default!;

    [Inject] protected IEnumerable<IDatabaseService> DatabaseServices { get; set; } = default!;
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
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load stats");
            await ShowToastAsync($"Failed to load stats: {ex.Message}", false);
        }
        finally
        {
            _loadingStats = false;
        }
    }
}
