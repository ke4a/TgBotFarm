using BotFarm.Core.Abstractions;

namespace BotTemplate.Abstractions;

/// <summary>
/// Bot-specific database operations used by BotTemplate's handlers and update service.
/// </summary>
public interface IBotTemplateDatabaseService : IMongoDbDatabaseService
{
}
