using BotFarm.Core.Abstractions;
using BotFarm.Core.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using BotTemplate.Abstractions;
using BotTemplate.Services;

namespace BotTemplate.Extensions;

/// <summary>
/// Registers BotTemplate with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds BotTemplate services, keyed registrations, and configuration bindings.
    /// Command and callback handlers are registered as keyed <see cref="ICommandHandler"/> and
    /// <see cref="ICallbackHandler"/> services under <see cref="Constants.Name"/>.
    /// </summary>
    public static IServiceCollection AddBotTemplateServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddBotConfig(Constants.Name, configuration);

        services.AddScoped<IBotTemplateMarkupService, BotTemplateMarkupService>()
                .AddKeyedScoped<IUpdateService, BotTemplateUpdateService>(Constants.Name)
                .AddSingleton<BotTemplateDatabaseService>()
                .AddKeyedSingleton<IDatabaseService>(Constants.Name, (s, _) => s.GetRequiredService<BotTemplateDatabaseService>())
                .AddKeyedSingleton<IMongoDbDatabaseService>(Constants.Name, (s, _) => s.GetRequiredService<BotTemplateDatabaseService>())
                .AddSingleton<IBotTemplateDatabaseService>(s => s.GetRequiredService<BotTemplateDatabaseService>())
                .AddKeyedSingleton<IBotService, BotTemplateService>(Constants.Name);
        return services;
    }
}
