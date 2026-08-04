using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TestBot.Services;

public class TestBotService : BotService
{
    public TestBotService(
        ITelegramBotClientFactory clientFactory,
        ILogger<TestBotService> logger,
        IHostApplicationLifetime appLifetime,
        IOptionsMonitor<BotConfig> botConfigs) : base(new BotIdentity(Constants.Name), clientFactory, logger, appLifetime, botConfigs)
    {
    }

    public override async Task Initialize()
    {
        // bot-specific initialization can be done here
        await base.Initialize();
    }
}
