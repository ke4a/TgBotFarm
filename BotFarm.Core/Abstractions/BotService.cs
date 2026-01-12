using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace BotFarm.Core.Abstractions;

public abstract class BotService : IBotService
{
    protected readonly ILogger<BotService> _logger;
    protected readonly IHostApplicationLifetime _appLifetime;
    protected string currentWebHook;

    protected string logPrefix = $"[{nameof(BotService)}]";

    public BotService(
        ILogger<BotService> logger,
        IHostApplicationLifetime appLifetime)
    {
        _logger = logger;
        _appLifetime = appLifetime;
    }

    public TelegramBotClient Client { get; protected set; }

    public abstract string Name { get; }
    
    public User Me { get; private set; }

    public string TempPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp", Name);

    public virtual async Task Initialize()
    {
        _logger.LogInformation($"{logPrefix} Initializing bot service for {Name}...");
        Me = await Client.GetMe();
    }

    public virtual async Task InitializeWebHook(string url)
    {
        await Client.SetWebhook(url);
        currentWebHook = url;
    }

    public virtual async Task<bool> Pause()
    {
        try
        {
            await Client.DeleteWebhook();
            _logger.LogInformation($"{logPrefix} Bot updates paused.");

            return true;
        }
        catch (Exception ex)
        {
            var message = $"{logPrefix} Could not pause bot updates. Error: '{ex.Message}'";
            _logger.LogError(message);

            return false;
        }
    }

    public virtual async Task<bool> Resume()
    {
        try
        {
            await Client.SetWebhook(currentWebHook);
            _logger.LogInformation($"{logPrefix} Bot updates resumed.");

            return true;
        }
        catch (Exception ex)
        {
            var message = $"{logPrefix} Could not resume bot updates. Error: '{ex.Message}'";
            _logger.LogError(message);
            _logger.LogWarning($"{logPrefix} Stopping application...");
            _appLifetime.StopApplication();

            return false;
        }
    }
}
