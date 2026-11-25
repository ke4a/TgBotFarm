using BotFarm.Core.Abstractions;
using BotFarm.Core.Localization;
using BotFarm.Core.Models;
using BotFarm.Core.Services;
using BotFarm.Core.Services.WebhookUrlResolvers;
using BotFarm.Core.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace BotFarm.Core.Extensions;

/// <summary>
/// Registers BotFarm core infrastructure services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the shared services used by the host app and bot implementations, including <see cref="UpdateMetrics"/> and
    /// <see cref="BotEventMetrics"/>,
    /// the <see cref="TelemetryOptions"/> bound to the <c>Telemetry</c> section and checked at startup, the
    /// <see cref="IMetricsLinks"/> built from them, and the stored bot settings that fill each bot's
    /// <see cref="BotConfig"/>.
    /// </summary>
    public static IServiceCollection AddCoreServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddHybridCache();
        services.AddHttpClient();
        services.TryAddSingleton(TimeProvider.System);
        services.AddMetrics();
        services.TryAddSingleton<UpdateMetrics>();
        services.TryAddSingleton<BotEventMetrics>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<TelemetryOptions>, TelemetryOptionsValidator>());
        services.AddOptions<TelemetryOptions>()
            .Bind(configuration.GetSection(Constants.Telemetry.SectionName))
            .ValidateOnStart();
        services.TryAddSingleton<IMetricsLinks, MetricsLinks>();
        services.AddDataProtection();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<BotConfig>, BotSettingsOptionsSetup>());

        services.AddSingleton<IAppDataPaths>(new AppDataPaths(configuration))
            .AddSingleton<ILocalizationService, JsonLocalizationService>()
            .AddSingleton<ITelegramBotClientFactory, TelegramBotClientFactory>()
            .AddSingleton<ITelegramTokenVerifier, TelegramTokenVerifier>()
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
            .AddSingleton<IBotSettingsStore>(serviceProvider =>
            {
                return new MongoBotSettingsStore(() =>
                {
                    return serviceProvider.GetRequiredService<IMongoClient>()
                        .GetDatabase(MongoBotSettingsStore.DatabaseName);
                });
            })
            .AddSingleton<BotSettingsProtector>()
            .AddSingleton<BotSettingsProvider>()
            .AddSingleton<IBotSettingsProvider>(serviceProvider =>
                serviceProvider.GetRequiredService<BotSettingsProvider>())
            .AddSingleton<BotControlCoordinator>()
            .AddSingleton<IBotControlService>(serviceProvider =>
                serviceProvider.GetRequiredService<BotControlCoordinator>())
            .AddSingleton<IBotSettingsService>(serviceProvider =>
                serviceProvider.GetRequiredService<BotControlCoordinator>());

        return services;
    }

    /// <summary>
    /// Registers the named bot's <see cref="BotConfig"/>, filled from the bot's stored settings and refreshed whenever
    /// they are loaded or saved. Configuration is not bound.
    /// </summary>
    public static IServiceCollection AddBotConfig(this IServiceCollection services, string botName)
    {
        services.AddOptions<BotConfig>(botName);
        services.AddSingleton<IOptionsChangeTokenSource<BotConfig>>(serviceProvider =>
            new BotSettingsChangeTokenSource(botName, serviceProvider.GetRequiredService<BotSettingsProvider>()));

        return services;
    }

    /// <summary>
    /// Adds admin UI localization: the JSON-backed <see cref="IStringLocalizerFactory"/>, the
    /// <see cref="IAdminLanguageCatalog"/>, and <see cref="RequestLocalizationOptions"/> that accept the catalog's
    /// languages, default to en-US and read the culture from the <c>.AspNetCore.Culture</c> cookie.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="root">
    /// The folder that holds the admin translation sets; <c>AdminLanguages</c> under the application base directory
    /// when null.
    /// </param>
    public static IServiceCollection AddAdminLocalization(this IServiceCollection services, string? root = null)
    {
        services.AddLocalization();
        services.AddSingleton(serviceProvider =>
            new JsonAdminStringLocalizerFactory(serviceProvider.GetRequiredService<ILoggerFactory>(), root));
        services.Replace(ServiceDescriptor.Singleton<IStringLocalizerFactory>(serviceProvider =>
            serviceProvider.GetRequiredService<JsonAdminStringLocalizerFactory>()));
        services.AddSingleton<IAdminLanguageCatalog, AdminLanguageCatalog>();

        services.AddOptions<RequestLocalizationOptions>()
            .Configure<IAdminLanguageCatalog>((options, catalog) =>
            {
                var cultures = catalog.Languages.Select(language => language.Culture).ToList();
                options.SupportedCultures = cultures;
                options.SupportedUICultures = cultures;
                options.DefaultRequestCulture = new RequestCulture(catalog.Default.Culture);
                options.RequestCultureProviders =
                [
                    new CookieRequestCultureProvider(),
                    new AcceptLanguageMatchProvider(catalog),
                ];
            });

        return services;
    }
}
