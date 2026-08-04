using BotFarm.Core.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot.Types;

namespace TestBot.Controllers;

/// <summary>
/// Receives Telegram webhook updates for the reference TestBot implementation.
/// </summary>
[ApiController]
[Route("api/TestBot/[controller]")]
public class UpdateController : ControllerBase
{
    private readonly IUpdateService _updateService;

    /// <summary>
    /// Creates the controller bound to TestBot's keyed <see cref="IUpdateService"/>.
    /// </summary>
    public UpdateController([FromKeyedServices(Constants.Name)] IUpdateService updateService)
    {
        _updateService = updateService;
    }

    /// <summary>
    /// Forwards a Telegram webhook update to the TestBot update service.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Post([FromBody] Update update)
    {
        await _updateService.ProcessUpdate(update);

        return Ok();
    }
}
