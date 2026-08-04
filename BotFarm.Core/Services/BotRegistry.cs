using BotFarm.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace BotFarm.Core.Services;

/// <inheritdoc cref="IBotRegistry"/>
public sealed class BotRegistry : IBotRegistry
{
    private readonly IServiceProvider _serviceProvider;

    public BotRegistry(IServiceProvider serviceProvider, IEnumerable<IBotService> allBotServices)
    {
        _serviceProvider = serviceProvider;
        AllBotServices = allBotServices;
    }

    public IEnumerable<IBotService> AllBotServices { get; }

    public T GetService<T>(string name) where T : notnull
    {
        return _serviceProvider.GetRequiredKeyedService<T>(name);
    }

    public bool HasService<T>(string name) where T : notnull
    {
        return _serviceProvider.GetKeyedService<T>(name) != null;
    }
}
