using BotFarm.Core.Abstractions;
using BotFarm.Shared.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace TestBot.Controllers;

/// <summary>
/// Receives Telegram webhook updates for the reference TestBot implementation.
/// </summary>
[ApiController]
[Route("api/TestBot/[controller]")]
public class UpdateController : UpdateControllerBase
{
    /// <summary>
    /// Creates the controller bound to TestBot's keyed <see cref="IUpdateService"/>.
    /// </summary>
    public UpdateController([FromKeyedServices(Constants.Name)] IUpdateService updateService)
        : base(updateService)
    {
    }
}