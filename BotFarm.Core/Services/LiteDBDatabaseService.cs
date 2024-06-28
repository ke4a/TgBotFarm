using BotFarm.Core.Services.Interfaces;
using LiteDB;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BotFarm.Core.Services
{
    public abstract class LiteDBDatabaseService : IDatabaseService
    {
        protected readonly ILogger<LiteDBDatabaseService> _logger;
        protected readonly IHostApplicationLifetime _appLifetime;
        protected readonly INotificationService _notificationService;

        protected string logPrefix = $"[{nameof(LiteDBDatabaseService)}]";

        protected LiteDatabase Instance { get; set; }

        public string Name { get; protected set; }

        public string DatabaseName { get; protected set; }

        public LiteDBDatabaseService(
            ILogger<LiteDBDatabaseService> logger,
            IHostApplicationLifetime appLifetime,
            INotificationService notificationService)
        {
            _logger = logger;
            _appLifetime = appLifetime;
            _notificationService = notificationService;
        }

        public virtual IEnumerable<string> GetCollectionNames()
        {
            return Instance.GetCollectionNames();
        }

        public virtual IEnumerable<BsonDocument> GetCollectionData(string collectionName)
        {
            return Instance.Engine.FindAll(collectionName);
        }

        public virtual async Task<bool> Release()
        {
            try
            {
                Instance.Dispose();
                _logger.LogInformation($"{logPrefix} Released database file.");
                return true;
            }
            catch (Exception ex)
            {
                var message = $"{logPrefix} Could not release database file. Error: '{ex.Message}'";
                _logger.LogError(message);
                await _notificationService.SendErrorNotification(message, Name);
                return false;
            }
        }

        public virtual async Task<bool> Reconnect()
        {
            try
            {
                Instance = new LiteDatabase(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DatabaseName));
                _logger.LogInformation($"{logPrefix} Reconnected to database.");
                return true;
            }
            catch (Exception ex)
            {
                var message = $"{logPrefix} Could not reconnect to database. Error: '{ex.Message}'";
                _logger.LogError(message);
                await _notificationService.SendErrorNotification(message, Name);
                _logger.LogWarning("Stopping application...");
                _appLifetime.StopApplication();
                return false;
            }
        }
    }
}
