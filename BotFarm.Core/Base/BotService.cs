using BotFarm.Core.Abstractions;
using BotFarm.Core.Exceptions;
using BotFarm.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace BotFarm.Core.Base;

/// <summary>
/// Base class for bot-specific bot services.
/// </summary>
public abstract class BotService : IBotService
{
    protected readonly ILogger<BotService> _logger;
    protected readonly BotIdentity Identity;
    protected readonly IWebhookSecretProvider _webhookSecretProvider;
    private readonly IAppDataPaths _dataPaths;
    private readonly ITelegramBotClientFactory _clientFactory;
    private readonly IOptionsMonitor<BotConfig> _botConfigs;
    private readonly TimeSpan? _clientTimeout;
    private readonly Lock _clientLock = new();
    private TelegramBotClient? _client;

    /// <summary>
    /// Initializes bot state. The Telegram client is created from the bot's current token on first use.
    /// </summary>
    /// <param name="clientTimeout">
    /// How long the client's requests may take; null keeps the factory default, which stays under
    /// Telegram's webhook timeout.
    /// </param>
    protected BotService(
        BotIdentity identity,
        ITelegramBotClientFactory clientFactory,
        ILogger<BotService> logger,
        IOptionsMonitor<BotConfig> botConfigs,
        IWebhookSecretProvider webhookSecretProvider,
        IAppDataPaths dataPaths,
        TimeSpan? clientTimeout = null)
    {
        Identity = identity;
        _clientFactory = clientFactory;
        _botConfigs = botConfigs;
        _clientTimeout = clientTimeout;
        _logger = logger;
        _webhookSecretProvider = webhookSecretProvider;
        _dataPaths = dataPaths;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The client is created from the token in the bot's <see cref="BotConfig"/> on first use and kept until
    /// <see cref="ReloadClient"/>.
    /// </remarks>
    public TelegramBotClient Client
    {
        get
        {
            lock (_clientLock)
            {
                if (_client is not null)
                {
                    return _client;
                }

                var token = _botConfigs.Get(Name).Token;
                if (string.IsNullOrWhiteSpace(token))
                {
                    throw new BotNotConfiguredException(Name);
                }

                return _client = _clientFactory.Create(token, _clientTimeout);
            }
        }
    }

    /// <inheritdoc />
    public void ReloadClient()
    {
        lock (_clientLock)
        {
            _client = null;
        }
    }

    public string Name => Identity.Name;

    public User? Me { get; private set; }

    public string TempPath => Path.Combine(_dataPaths.Temp, Name);

    /// <summary>
    /// Prepares the bot for runtime use and fetches the Telegram account metadata.
    /// </summary>
    public virtual async Task Initialize(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("{LogPrefix} Initializing bot service for {BotName}...", Identity.LogPrefix, Name);
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
    /// Discards queued updates. A registered webhook is set again at the same URL with the bot's secret;
    /// without one, the queue is cleared and the bot stays without a webhook.
    /// </summary>
    public virtual async Task DropPendingUpdates(CancellationToken cancellationToken = default)
    {
        var info = await Client.GetWebhookInfo(cancellationToken);
        if (string.IsNullOrEmpty(info.Url))
        {
            await Client.DeleteWebhook(dropPendingUpdates: true, cancellationToken: cancellationToken);
        }
        else
        {
            await Client.SetWebhook(
                info.Url,
                secretToken: _webhookSecretProvider.GetSecret(Name),
                dropPendingUpdates: true,
                cancellationToken: cancellationToken);
        }

        _logger.LogInformation("{LogPrefix} Pending updates dropped.", Identity.LogPrefix);
    }

    /// <summary>
    /// Deletes the configured webhook so the bot temporarily stops receiving updates.
    /// </summary>
    public virtual async Task<bool> Pause(CancellationToken cancellationToken = default)
    {
        try
        {
            await Client.DeleteWebhook(cancellationToken: cancellationToken);
            _logger.LogInformation("{LogPrefix} Bot updates paused.", Identity.LogPrefix);

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{BotName} could not pause bot updates.", Name);

            return false;
        }
    }
}