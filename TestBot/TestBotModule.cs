using BotFarm.Core.Abstractions;
using BotFarm.Core.Base;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TestBot;
using TestBot.Extensions;

[assembly: BotModule(typeof(TestBotModule))]

namespace TestBot;

/// <summary>
/// Plugs the reference TestBot implementation into the BotFarm host.
/// </summary>
public sealed class TestBotModule : IBotModule
{
    /// <inheritdoc />
    public string Name => Constants.Name;

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddTestBotServices(configuration);
    }

    /// <inheritdoc />
    public void AddHealthChecks(IHealthChecksBuilder healthChecks)
    {
        healthChecks.Services.AddTestBotHealthChecks();
    }
}
