using BotFarm.Core.Abstractions;
using NSubstitute;

namespace BotFarm.TestKit;

/// <summary>
/// Creates substitute <see cref="IBotRegistry"/> instances for tests.
/// </summary>
public static class BotRegistryFactory
{
    /// <summary>
    /// Creates a registry whose <see cref="IBotRegistry.GetServicesForAllBots{T}"/> returns <paramref name="bots"/>
    /// for <see cref="IBotService"/>.
    /// </summary>
    public static IBotRegistry CreateSubstitute(params IBotService[] bots)
    {
        var registry = Substitute.For<IBotRegistry>();
        registry.GetServicesForAllBots<IBotService>().Returns(bots);
        return registry;
    }
}
