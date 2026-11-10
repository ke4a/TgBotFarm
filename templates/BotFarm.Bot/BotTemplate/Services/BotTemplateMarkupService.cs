using BotFarm.Core.Abstractions;
using BotFarm.Core.Base;
using BotFarm.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using BotTemplate.Abstractions;

namespace BotTemplate.Services;

/// <summary>
/// Bot-specific markup builder for BotTemplate.
/// </summary>
public class BotTemplateMarkupService : MarkupService, IBotTemplateMarkupService
{
    /// <summary>
    /// Creates the markup service that reuses the shared localization infrastructure.
    /// </summary>
    public BotTemplateMarkupService(
        [FromKeyedServices(Constants.Name)] BotIdentity identity,
        ILocalizationService localizationService)
        : base(identity, localizationService)
    {
    }
}
