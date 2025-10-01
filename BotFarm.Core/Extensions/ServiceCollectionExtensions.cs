using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BotFarm.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCoreServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<WebDAVSettings>(configuration.GetSection(nameof(WebDAVSettings)))
                .Configure<AuthenticationConfig>(configuration.GetSection(nameof(AuthenticationConfig)));

        services.AddSingleton<ILocalizationService, JsonLocalizationService>()
                .AddSingleton<IBackupService, LiteDBBackupService>()
                .AddSingleton<ICloudService, WebDavCloudService>()
                .AddSingleton<INotificationService, TelegramNotificationService>();

        return services;
    }
}
