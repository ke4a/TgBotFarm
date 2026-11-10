using BotFarm.Core.Abstractions;
using BotFarm.Core.Base;
using BotFarm.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BotTemplate.Services;

/// <summary>
/// Bot-specific <see cref="BotService"/> for BotTemplate.
/// </summary>
public class BotTemplateService : BotService
{
    /// <summary>
    /// Creates the Telegram client and configuration-backed state for BotTemplate.
    /// </summary>
    public BotTemplateService(
        [FromKeyedServices(Constants.Name)] BotIdentity identity,
        ITelegramBotClientFactory clientFactory,
        ILogger<BotTemplateService> logger,
        IOptionsMonitor<BotConfig> botConfigs,
        IWebhookSecretProvider webhookSecretProvider,
        IAppDataPaths dataPaths) : base(identity, clientFactory, logger,
        botConfigs, webhookSecretProvider, dataPaths)
    {
    }

    /// <summary>
    /// Performs any BotTemplate startup work before delegating to the shared bot initialization flow.
    /// </summary>
    public override async Task Initialize(CancellationToken cancellationToken = default)
    {
        await base.Initialize(cancellationToken);
    }
}
