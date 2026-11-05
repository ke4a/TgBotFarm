using BotFarm.Core.Abstractions;
using BotFarm.Core.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TestBot.Abstractions;
using TestBot.Handlers.Callbacks;
using TestBot.Handlers.Commands;
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
        services.AddBotConfig(Constants.Name, configuration);

        services.AddScoped<ITestBotMarkupService, TestBotMarkupService>()
                .AddKeyedScoped<IUpdateService, TestBotUpdateService>(Constants.Name)
                .AddKeyedScoped<ICommandHandler, ClearChatDataCommandHandler>(Constants.Name)
                .AddKeyedScoped<ICommandHandler, GetLastGifCommandHandler>(Constants.Name)
                .AddKeyedScoped<ICallbackHandler, ClearChatDataCallbackHandler>(Constants.Name)
                .AddSingleton<TestBotDatabaseService>()
                .AddKeyedSingleton<IDatabaseService>(Constants.Name, (s, _) => s.GetRequiredService<TestBotDatabaseService>())
                .AddKeyedSingleton<IMongoDbDatabaseService>(Constants.Name, (s, _) => s.GetRequiredService<TestBotDatabaseService>())
                .AddSingleton<ITestBotDatabaseService>(s => s.GetRequiredService<TestBotDatabaseService>())
                .AddKeyedSingleton<IBotService, TestBotService>(Constants.Name);
        return services;
    }
}
