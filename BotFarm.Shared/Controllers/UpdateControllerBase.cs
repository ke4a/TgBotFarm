using BotFarm.Core.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Telegram.Bot.Types;

namespace BotFarm.Shared.Controllers;

/// <summary>
/// Provides the shared webhook endpoint behavior for bot update controllers.
/// </summary>
public abstract class UpdateControllerBase : ControllerBase
{
    private readonly IUpdateService _updateService;

    /// <summary>
    /// Creates the controller with the bot-specific update service.
    /// </summary>
    protected UpdateControllerBase(IUpdateService updateService)
    {
        _updateService = updateService;
    }

    /// <summary>
    /// Forwards a Telegram webhook update when the bot is ready to process it.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Post([FromBody] Update update)
    {
        if (!_updateService.CanProcessUpdates)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        await _updateService.ProcessUpdate(update);

        return Ok();
    }
}
