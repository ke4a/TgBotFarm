using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// A bot's entry point into the host: registers the bot's services and health checks
/// and tells the host where the bot's pages live. Each bot project has exactly one,
/// declared with <c>[assembly: BotModule(typeof(...))]</c>; the host creates it at startup.
/// </summary>
/// <remarks>
/// Implementations need a public parameterless constructor.
/// </remarks>
public interface IBotModule
{
    /// <summary>
    /// The bot's name. Matches the bot's section under <c>Bots</c> in configuration and must be unique.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Registers the bot's services, keyed registrations and configuration bindings.
    /// </summary>
    void AddServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>
    /// Adds the bot's health checks to the shared health-check pipeline.
    /// </summary>
    void AddHealthChecks(IHealthChecksBuilder healthChecks);

    /// <summary>
    /// The assembly holding the bot's routable pages and components. Defaults to the module's own assembly.
    /// </summary>
    Assembly ComponentsAssembly => GetType().Assembly;
}
