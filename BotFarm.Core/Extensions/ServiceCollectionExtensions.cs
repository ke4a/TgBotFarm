using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.Core.Services;
using BotFarm.Core.Services.WebhookUrlResolvers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<BotConfig>, BotConfigValidator>());

        services.AddSingleton<IAppDataPaths>(new AppDataPaths(configuration))
            .AddSingleton<ILocalizationService, JsonLocalizationService>()
            .AddSingleton<ITelegramBotClientFactory, TelegramBotClientFactory>()
            .AddSingleton<IWebhookSecretProvider, WebhookSecretProvider>()
            .AddSingleton<IUpdateDeliveryGuard, UpdateDeliveryGuard>()
            .AddSingleton<IMongoClient>(_ =>
            {
                var connectionString = configuration.GetConnectionString("MongoDb")
                    ?? throw new InvalidOperationException(
                        "MongoDB connection string not found in configuration.");
                return new MongoClient(connectionString);
            })
            .AddSingleton<IBotRegistry, BotRegistry>()
            .AddTransient<INotificationService, TelegramNotificationService>()
            .AddSingleton<IBroadcastService, TelegramBroadcastService>()
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
                return new MongoBotControlStateStore(() =>
                {
                    return serviceProvider.GetRequiredService<IMongoClient>()
                        .GetDatabase(MongoBotControlStateStore.DatabaseName);
                });
            })
            .AddSingleton<BotControlCoordinator>()
            .AddSingleton<IBotControlService>(serviceProvider =>
                serviceProvider.GetRequiredService<BotControlCoordinator>());

        return services;
    }

    /// <summary>
    /// Binds the named bot's <see cref="BotConfig"/> from <c>Bots:{botName}:BotConfig</c> and checks it when the
    /// app starts, so a missing or malformed setting stops startup with a message naming that setting.
    /// </summary>
    public static IServiceCollection AddBotConfig(
        this IServiceCollection services,
        string botName,
        IConfiguration configuration)
    {
        services.AddOptions<BotConfig>(botName)
            .Bind(configuration.GetSection(BotConfigValidator.SectionPath(botName)))
            .ValidateOnStart();

        return services;
    }
}