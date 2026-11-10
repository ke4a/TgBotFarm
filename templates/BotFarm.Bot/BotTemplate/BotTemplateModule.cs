using BotFarm.Core.Abstractions;
using BotFarm.Core.Base;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using BotTemplate;
using BotTemplate.Extensions;

[assembly: BotModule(typeof(BotTemplateModule))]

namespace BotTemplate;

/// <summary>
/// Plugs BotTemplate into the BotFarm host.
/// </summary>
public sealed class BotTemplateModule : IBotModule
{
    /// <inheritdoc />
    public string Name => Constants.Name;

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddBotTemplateServices(configuration);
    }

    /// <inheritdoc />
    public void AddHealthChecks(IHealthChecksBuilder healthChecks)
    {
        healthChecks.Services.AddBotTemplateHealthChecks();
    }
}
