using BotFarm.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Base class for bot-specific bot services.
/// </summary>
public abstract class BotService : IBotService
{
    protected readonly ILogger<BotService> _logger;
    protected readonly BotIdentity Identity;
    protected readonly IWebhookSecretProvider _webhookSecretProvider;

    /// <summary>
    /// Initializes bot state from keyed configuration and creates the authenticated Telegram client.
    /// </summary>
    protected BotService(
        BotIdentity identity,
        ITelegramBotClientFactory clientFactory,
        ILogger<BotService> logger,
        IOptionsMonitor<BotConfig> botConfigs,
        IWebhookSecretProvider webhookSecretProvider)
    {
        Identity = identity;

        var botConfig = botConfigs.Get(identity.Name);

        ArgumentNullException.ThrowIfNull(botConfig?.Token);

        Client = clientFactory.Create(botConfig.Token);

        _logger = logger;
        _webhookSecretProvider = webhookSecretProvider;
    }

    public TelegramBotClient Client { get; protected set; }

    public string Name => Identity.Name;

    public User Me { get; private set; }

    public string TempPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp", Name);

    /// <summary>
    /// Prepares the bot for runtime use and fetches the Telegram account metadata.
    /// </summary>
    public virtual async Task Initialize(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"{Identity.LogPrefix} Initializing bot service for {Name}...");
        _ = Directory.CreateDirectory(TempPath);
        Me = await Client.GetMe(cancellationToken);
    }

    /// <summary>
    /// Configures Telegram to deliver updates to <paramref name="url"/>, authenticated with the bot's webhook secret.
    /// </summary>
    public virtual async Task InitializeWebHook(string url, CancellationToken cancellationToken = default)
    {
        await Client.SetWebhook(
            url,
            secretToken: _webhookSecretProvider.GetSecret(Name),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Deletes the configured webhook so the bot temporarily stops receiving updates.
    /// </summary>
    public virtual async Task<bool> Pause(CancellationToken cancellationToken = default)
    {
        try
        {
            await Client.DeleteWebhook(cancellationToken: cancellationToken);
            _logger.LogInformation($"{Identity.LogPrefix} Bot updates paused.");

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "{BotName} could not pause bot updates ({FailureType}).",
                Name,
                ex.GetType().Name);

            return false;
        }
    }
}