using BotFarm.Core.Services.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;

namespace BotFarm.Core.Services
{
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

        public string Handle { get; protected set; }

        public async Task InitializeWebHook(string url)
        {
            await Client.SetWebhookAsync(url);
            currentWebHook = url;
        }

        public async Task<bool> Pause()
        {
            try
            {
                await Client.DeleteWebhookAsync();
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

        public async Task<bool> Resume()
        {
            try
            {
                await Client.SetWebhookAsync(currentWebHook);
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
}
