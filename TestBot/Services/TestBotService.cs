using BotFarm.Core.Abstractions;
using BotFarm.Core.Base;
using BotFarm.Core.Models;
using Microsoft.Extensions.DependencyInjection;
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
        [FromKeyedServices(Constants.Name)] BotIdentity identity,
        ITelegramBotClientFactory clientFactory,
        ILogger<TestBotService> logger,
        IOptionsMonitor<BotConfig> botConfigs,
        IWebhookSecretProvider webhookSecretProvider,
        IAppDataPaths dataPaths) : base(identity, clientFactory, logger,
        botConfigs, webhookSecretProvider, dataPaths)
    {
    }

    /// <summary>
    /// Performs any TestBot startup work before delegating to the shared bot initialization flow.
    /// </summary>
    public override async Task Initialize(CancellationToken cancellationToken = default)
    {
        // bot-specific initialization can be done here
        await base.Initialize(cancellationToken);
    }
}