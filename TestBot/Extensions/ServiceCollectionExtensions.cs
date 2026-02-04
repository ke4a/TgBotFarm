using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TestBot.Abstractions;
using TestBot.Services;

namespace TestBot.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTestBotServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(new BotRegistration(Constants.Name));
        services.Configure<BotConfig>(Constants.Name, configuration.GetSection($"Bots:{Constants.Name}:{nameof(BotConfig)}"));

        services.AddScoped<ITestBotMarkupService, TestBotMarkupService>()
                .AddKeyedScoped<IUpdateService, TestBotUpdateService>(Constants.Name)
                // add TestBot database service for different DI scenarios
                .AddSingleton<TestBotDatabaseService>()
                .AddSingleton<IDatabaseService>(s => s.GetRequiredService<TestBotDatabaseService>())
                .AddSingleton<IMongoDbDatabaseService>(s => s.GetRequiredService<TestBotDatabaseService>())
                .AddSingleton<ITestBotDatabaseService>(s => s.GetRequiredService<TestBotDatabaseService>())
                // add both keyed and regular IBotService for different DI scenarios
                .AddKeyedSingleton<IBotService, TestBotService>(Constants.Name)
                .AddSingleton<IBotService>(s => s.GetRequiredKeyedService<IBotService>(Constants.Name));
        return services;
    }
}
