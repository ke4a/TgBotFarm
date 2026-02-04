using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TestBot.Services;

public class TestBotService : BotService
{
    public TestBotService(
        ILogger<TestBotService> logger,
        IHostApplicationLifetime appLifetime,
        IOptionsMonitor<BotConfig> botConfigs) : base(logger, appLifetime, botConfigs)
    {
        logPrefix = $"[{nameof(TestBotService)}]";
    }

    public override string Name => Constants.Name;

    public override async Task Initialize()
    {
        // bot-specific initialization can be done here
        await base.Initialize();
    }
}
