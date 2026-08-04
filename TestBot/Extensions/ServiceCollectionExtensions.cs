using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TestBot.Abstractions;
using TestBot.Services;

namespace TestBot.Extensions;

/// <summary>
/// Registers the reference TestBot implementation with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds TestBot services, keyed registrations, and configuration bindings.
    /// </summary>
    public static IServiceCollection AddTestBotServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(new BotIdentity(Constants.Name));
        services.Configure<BotConfig>(Constants.Name, configuration.GetSection($"Bots:{Constants.Name}:{nameof(BotConfig)}"));

        services.AddScoped<ITestBotMarkupService, TestBotMarkupService>()
                .AddKeyedScoped<IUpdateService, TestBotUpdateService>(Constants.Name)
                // add TestBot database service for different DI scenarios
                .AddSingleton<TestBotDatabaseService>()
                .AddSingleton<IDatabaseService>(s => s.GetRequiredService<TestBotDatabaseService>())
                .AddSingleton<IMongoDbDatabaseService>(s => s.GetRequiredService<TestBotDatabaseService>())
                .AddKeyedSingleton<IMongoDbDatabaseService>(Constants.Name, (s, _) => s.GetRequiredService<TestBotDatabaseService>())
                .AddSingleton<ITestBotDatabaseService>(s => s.GetRequiredService<TestBotDatabaseService>())
                // add both keyed and regular IBotService for different DI scenarios
                .AddKeyedSingleton<IBotService, TestBotService>(Constants.Name)
                .AddSingleton<IBotService>(s => s.GetRequiredKeyedService<IBotService>(Constants.Name));
        return services;
    }
}
