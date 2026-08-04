namespace BotFarm.Core.Abstractions;

/// <summary>
/// Resolves bot-scoped services by bot name using keyed DI.
/// </summary>
public interface IBotRegistry
{
    /// <summary>Returns the keyed service of type <typeparamref name="T"/> registered for <paramref name="name"/>.</summary>
    T GetService<T>(string name) where T : notnull;

    /// <summary>Whether a keyed service of type <typeparamref name="T"/> is registered for <paramref name="name"/>.</summary>
    bool HasService<T>(string name) where T : notnull;

    /// <summary>All registered bot services, for operations that genuinely apply to every bot.</summary>
    IEnumerable<IBotService> AllBotServices { get; }
}
