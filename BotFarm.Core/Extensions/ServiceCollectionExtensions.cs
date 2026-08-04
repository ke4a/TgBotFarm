using BotFarm.Core.Abstractions;
using BotFarm.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BotFarm.Core.Extensions;

/// <summary>
/// Registers BotFarm core infrastructure services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the shared services used by the host app and bot implementations.
    /// </summary>
    public static IServiceCollection AddCoreServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHybridCache();

        services.AddSingleton<ILocalizationService, JsonLocalizationService>()
                .AddSingleton<ITelegramBotClientFactory, TelegramBotClientFactory>()
                .AddSingleton<IMongoClientFactory, MongoClientFactory>()
                .AddSingleton<IBotRegistry, BotRegistry>()
                .AddTransient<INotificationService, TelegramNotificationService>()
                .AddTransient<IBackupService, MongoDbBackupService>()
                .AddTransient<ILocalBackupHelperService, LocalBackupHelperService>();

        return services;
    }
}
