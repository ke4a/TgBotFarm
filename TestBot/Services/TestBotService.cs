using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TestBot.Services;

/// <summary>
/// Bot-specific <see cref="BotService"/> for the reference TestBot implementation.
/// </summary>
public class TestBotService : BotService
{
    /// <summary>
    /// Creates the Telegram client and configuration-backed state for TestBot.
    /// </summary>
    public TestBotService(
        ITelegramBotClientFactory clientFactory,
        ILogger<TestBotService> logger,
        IHostApplicationLifetime appLifetime,
        IOptionsMonitor<BotConfig> botConfigs) : base(new BotIdentity(Constants.Name), clientFactory, logger, appLifetime, botConfigs)
    {
    }

    /// <summary>
    /// Performs any TestBot startup work before delegating to the shared bot initialization flow.
    /// </summary>
    public override async Task Initialize()
    {
        // bot-specific initialization can be done here
        await base.Initialize();
    }
}
