using BotFarm.Core.Models;
using Microsoft.Extensions.Hosting;
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
    protected readonly IHostApplicationLifetime _appLifetime;
    protected readonly BotIdentity Identity;
    protected string currentWebHook;

    /// <summary>
    /// Initializes bot state from keyed configuration and creates the authenticated Telegram client.
    /// </summary>
    protected BotService(
        BotIdentity identity,
        ITelegramBotClientFactory clientFactory,
        ILogger<BotService> logger,
        IHostApplicationLifetime appLifetime,
        IOptionsMonitor<BotConfig> botConfigs)
    {
        Identity = identity;

        var botConfig = botConfigs.Get(identity.Name);

        ArgumentNullException.ThrowIfNull(botConfig?.Token);

        Enabled = botConfig.Enabled;
        Client = clientFactory.Create(botConfig.Token);

        _logger = logger;
        _appLifetime = appLifetime;
    }

    public bool Enabled { get; protected set; }

    public TelegramBotClient Client { get; protected set; }

    public string Name => Identity.Name;

    public User Me { get; private set; }

    public string TempPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp", Name);

    /// <summary>
    /// Prepares the bot for runtime use and fetches the Telegram account metadata.
    /// </summary>
    public virtual async Task Initialize()
    {
        _logger.LogInformation($"{Identity.LogPrefix} Initializing bot service for {Name}...");
        _ = Directory.CreateDirectory(TempPath);
        Me = await Client.GetMe();
    }

    /// <summary>
    /// Configures Telegram to deliver updates to <paramref name="url"/>.
    /// </summary>
    public virtual async Task InitializeWebHook(string url)
    {
        await Client.SetWebhook(url);
        currentWebHook = url;
    }

    /// <summary>
    /// Deletes the configured webhook so the bot temporarily stops receiving updates.
    /// </summary>
    public virtual async Task<bool> Pause()
    {
        try
        {
            await Client.DeleteWebhook();
            _logger.LogInformation($"{Identity.LogPrefix} Bot updates paused.");

            return true;
        }
        catch (Exception ex)
        {
            var message = $"{Identity.LogPrefix} Could not pause bot updates. Error: '{ex.Message}'";
            _logger.LogError(message);

            return false;
        }
    }

    /// <summary>
    /// Re-applies the last configured webhook and stops the host if that recovery fails.
    /// </summary>
    public virtual async Task<bool> Resume()
    {
        try
        {
            await Client.SetWebhook(currentWebHook);
            _logger.LogInformation($"{Identity.LogPrefix} Bot updates resumed.");

            return true;
        }
        catch (Exception ex)
        {
            var message = $"{Identity.LogPrefix} Could not resume bot updates. Error: '{ex.Message}'";
            _logger.LogError(message);
            _logger.LogWarning($"{Identity.LogPrefix} Stopping application...");
            _appLifetime.StopApplication();

            return false;
        }
    }
}
