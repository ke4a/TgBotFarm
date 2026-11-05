using BotFarm.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace BotFarm.Core.Services;

/// <inheritdoc cref="IBotRegistry"/>
internal sealed class BotRegistry : IBotRegistry
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IReadOnlyList<string> _botNames;

    public BotRegistry(IServiceProvider serviceProvider, IEnumerable<IBotModule> botModules)
    {
        _serviceProvider = serviceProvider;
        _botNames = botModules.Select(module => module.Name).ToArray();
    }

    public T GetService<T>(string name) where T : notnull
    {
        return _serviceProvider.GetRequiredKeyedService<T>(name);
    }

    public bool HasService<T>(string name) where T : notnull
    {
        return _serviceProvider.GetKeyedService<T>(name) != null;
    }

    public IReadOnlyList<T> GetServicesForAllBots<T>() where T : notnull
    {
        return _botNames.Select(name => _serviceProvider.GetRequiredKeyedService<T>(name)).ToArray();
    }
}
