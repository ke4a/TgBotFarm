using BotFarm.Core.Abstractions;

namespace BotFarm.Core.Extensions;

public static class NamedServiceCollectionExtensions
{
    /// <summary>
    /// Finds the service with the given name (case-insensitive), throwing if none is found.
    /// </summary>
    public static T GetByName<T>(this IEnumerable<T> services, string name) where T : INamedService
    {
        return services.First(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Finds the service with the given name (case-insensitive), or null if none is found.
    /// </summary>
    public static T? TryGetByName<T>(this IEnumerable<T> services, string name) where T : class, INamedService
    {
        return services.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Determines whether a service with the given name (case-insensitive) exists in the collection.
    /// </summary>
    public static bool HasByName<T>(this IEnumerable<T> services, string name) where T : INamedService
    {
        return services.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}
