using BotFarm.Core.Abstractions;
using BotFarm.Core.Services;
using BotFarm.Core.Services.WebhookUrlResolvers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;

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
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddHybridCache();
        services.AddHttpClient();

        services.AddSingleton<ILocalizationService, JsonLocalizationService>()
            .AddSingleton<ITelegramBotClientFactory, TelegramBotClientFactory>()
            .AddSingleton<IMongoClientFactory, MongoClientFactory>()
            .AddSingleton<IBotRegistry, BotRegistry>()
            .AddTransient<INotificationService, TelegramNotificationService>()
            .AddTransient<IBackupService, MongoDbBackupService>()
            .AddTransient<ILocalBackupHelperService, LocalBackupHelperService>();

        // Order matters: resolvers are tried in registration order, and StaticWebhookUrlResolver
        // is a catch-all fallback that must come last. The tunnel-based resolvers are only
        // relevant for local development, so they're excluded entirely from production DI.
        if (environment.IsDevelopment())
        {
            services.AddSingleton<IWebhookUrlResolver, DevTunnelWebhookUrlResolver>()
                .AddSingleton<IWebhookUrlResolver, LocalTunnelWebhookUrlResolver>()
                .AddSingleton<IWebhookUrlResolver, NgrokWebhookUrlResolver>();
        }

        services.AddSingleton<IWebhookUrlResolver, StaticWebhookUrlResolver>()
            .AddSingleton<IBotWebhookInitializer, BotWebhookInitializerService>()
            .AddSingleton<IBotControlStateStore>(serviceProvider =>
            {
                var clientFactory = serviceProvider.GetRequiredService<IMongoClientFactory>();
                return new MongoBotControlStateStore(() =>
                {
                    var connectionString = configuration.GetConnectionString("MongoDb")
                                           ?? throw new InvalidOperationException(
                                               "MongoDB connection string not found in configuration.");
                    return clientFactory.Create(connectionString)
                        .GetDatabase(MongoBotControlStateStore.DatabaseName);
                });
            })
            .AddSingleton<BotControlCoordinator>()
            .AddSingleton<IBotControlService>(serviceProvider =>
                serviceProvider.GetRequiredService<BotControlCoordinator>());

        return services;
    }
}