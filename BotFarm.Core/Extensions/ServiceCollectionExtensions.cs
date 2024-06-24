using BotFarm.Core.Models;
using BotFarm.Core.Services;
using BotFarm.Core.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BotFarm.Core.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddCoreServices(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.Configure<WebDAVSettings>(configuration.GetSection(nameof(WebDAVSettings)))
                    .Configure<AuthenticationConfig>(configuration.GetSection(nameof(AuthenticationConfig)));

            services.AddSingleton<ILocalizationService, JsonLocalizationService>()
                    .AddScoped<IBackupService, LiteDBBackupService>()
                    .AddScoped<ICloudService, WebDavCloudService>()
                    .AddSingleton<INotificationService, TelegramNotificationService>();

            return services;
        }
    }
}
