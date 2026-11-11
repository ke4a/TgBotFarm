using BotFarm.Core.Abstractions;
using BotFarm.Core.Base;
using BotFarm.Core.Models;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using BotTemplate.Abstractions;

namespace BotTemplate.Services;

/// <summary>
/// Bot-specific database service for BotTemplate. Bot-owned collections that store per-chat data
/// should also be moved in an override of <c>MigrateBotChatData</c> and deleted in an override of
/// <c>DeleteBotChatData</c>.
/// </summary>
public class BotTemplateDatabaseService : MongoDbDatabaseService, IBotTemplateDatabaseService
{
    /// <summary>
    /// Creates the BotTemplate database service bound to the bot's database.
    /// </summary>
    public BotTemplateDatabaseService(
        [FromKeyedServices(Constants.Name)] BotIdentity identity,
        IMongoClient client,
        ILogger<BotTemplateDatabaseService> logger,
        INotificationService notificationService,
        HybridCache cache) : base(identity, client, logger, notificationService, cache)
    {
    }
}
