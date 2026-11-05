namespace BotFarm.Core.Abstractions;

/// <summary>
/// Resolves bot-scoped services by bot name using keyed DI. The registered bots are the
/// <see cref="IBotModule"/> instances in the container.
/// </summary>
public interface IBotRegistry
{
    /// <summary>Returns the keyed service of type <typeparamref name="T"/> registered for the bot named <paramref name="name"/>.</summary>
    /// <exception cref="InvalidOperationException">No such service is registered for the bot named <paramref name="name"/>.</exception>
    T GetService<T>(string name) where T : notnull;

    /// <summary>Whether a keyed service of type <typeparamref name="T"/> is registered for the bot named <paramref name="name"/>.</summary>
    bool HasService<T>(string name) where T : notnull;

    /// <summary>Returns the keyed service of type <typeparamref name="T"/> of every bot, in bot module order.</summary>
    /// <exception cref="InvalidOperationException">A bot has no such service.</exception>
    IReadOnlyList<T> GetServicesForAllBots<T>() where T : notnull;
}
